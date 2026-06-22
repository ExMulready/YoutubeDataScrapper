"""Core agentic loop: Claude API multi-turn tool use for trading decisions."""

from __future__ import annotations

import asyncio
import dataclasses
import datetime
import json
import logging
from typing import Any

import anthropic

from agent.circuit_breaker import CircuitBreaker
from agent.logger import CycleRecord, TradeLogger, TradeRecord
from agent.mcp_client import RobinhoodMCPClient
from agent.prompts import build_system_prompt
from agent.strategy import AggressivenessProfile, validate_trade_request

logger = logging.getLogger(__name__)
MODEL = "claude-sonnet-4-6"
MAX_TOKENS = 4096
MAX_TOOL_ITERATIONS = 20


@dataclasses.dataclass
class MarketSnapshot:
    timestamp: str
    portfolio_value: float
    cash_available: float
    buying_power: float
    open_positions: list[dict]
    index_quotes: dict[str, Any]
    watchlist_symbols: list[str]
    today_orders: list[dict]
    trades_today_count: int
    daily_pnl: float


@dataclasses.dataclass
class CycleResult:
    cycle_id: int
    started_at: str
    finished_at: str
    trades_placed: int
    trades_rejected: int
    claude_summary: str
    input_tokens: int
    output_tokens: int
    total_tool_calls: int
    error: str | None


_TOOL_DEFINITIONS: list[dict] = [
    {"name": "get_equity_quotes", "description": "Get current prices for a list of stock symbols.",
     "input_schema": {"type": "object", "properties": {"symbols": {"type": "array", "items": {"type": "string"}}}, "required": ["symbols"]}},
    {"name": "get_equity_historicals", "description": "Get OHLCV price history for a symbol.",
     "input_schema": {"type": "object", "properties": {"symbol": {"type": "string"}, "interval": {"type": "string", "enum": ["5minute", "10minute", "hour", "day", "week"]}, "span": {"type": "string", "enum": ["day", "week", "month", "3month", "year", "5year"]}}, "required": ["symbol", "interval", "span"]}},
    {"name": "get_equity_fundamentals", "description": "Get P/E, market cap, revenue, EPS for symbols.",
     "input_schema": {"type": "object", "properties": {"symbols": {"type": "array", "items": {"type": "string"}}}, "required": ["symbols"]}},
    {"name": "get_equity_positions", "description": "Get all current open positions.",
     "input_schema": {"type": "object", "properties": {}, "required": []}},
    {"name": "get_equity_orders", "description": "Get order history by state.",
     "input_schema": {"type": "object", "properties": {"state": {"type": "string", "enum": ["open", "filled", "cancelled", "all"]}}, "required": []}},
    {"name": "get_earnings_calendar", "description": "Get upcoming earnings announcements.",
     "input_schema": {"type": "object", "properties": {}, "required": []}},
    {"name": "get_earnings_results", "description": "Get recent earnings results for a symbol.",
     "input_schema": {"type": "object", "properties": {"symbol": {"type": "string"}}, "required": ["symbol"]}},
    {"name": "get_index_quotes", "description": "Get quotes for market indices (SPY, QQQ, DIA, VIX).",
     "input_schema": {"type": "object", "properties": {"symbols": {"type": "array", "items": {"type": "string"}}}, "required": ["symbols"]}},
    {"name": "place_equity_order",
     "description": "Place a buy or sell order. MUST include confidence (0.0-1.0) and rationale. Orders below threshold are rejected. After a buy, place a stop_loss order.",
     "input_schema": {"type": "object", "properties": {
         "symbol": {"type": "string"}, "side": {"type": "string", "enum": ["buy", "sell"]},
         "quantity": {"type": "number"}, "order_type": {"type": "string", "enum": ["market", "limit", "stop_loss", "stop_limit"]},
         "limit_price": {"type": "number"}, "stop_price": {"type": "number"},
         "time_in_force": {"type": "string", "enum": ["gfd", "gtc", "ioc", "opg"]},
         "confidence": {"type": "number", "minimum": 0.0, "maximum": 1.0},
         "rationale": {"type": "string"}},
         "required": ["symbol", "side", "quantity", "order_type", "confidence", "rationale"]}},
    {"name": "cancel_equity_order", "description": "Cancel an open order by ID.",
     "input_schema": {"type": "object", "properties": {"order_id": {"type": "string"}}, "required": ["order_id"]}},
]


class ClaudeTrader:
    def __init__(self, mcp_client: RobinhoodMCPClient, circuit_breaker: CircuitBreaker,
                 trade_logger: TradeLogger, profile: AggressivenessProfile, config: dict, api_key: str) -> None:
        self._mcp = mcp_client
        self._breaker = circuit_breaker
        self._logger = trade_logger
        self._profile = profile
        self._config = config
        self._dry_run: bool = config.get("dry_run", True)
        self._watchlist_name: str = config.get("watchlist_name", "Default")
        self._anthropic = anthropic.Anthropic(api_key=api_key)
        self._system_prompt = build_system_prompt(profile)

    async def run_cycle(self) -> CycleResult:
        started_at = _now()
        cycle_id = await self._logger.start_cycle(CycleRecord(started_at=started_at, aggressiveness_level=self._profile.level))
        trades_placed = trades_rejected = total_tool_calls = input_tokens = output_tokens = 0
        claude_summary = ""
        error_msg: str | None = None
        snapshot: MarketSnapshot | None = None

        try:
            snapshot = await self._gather_initial_context()
            await self._breaker.check_portfolio_loss(snapshot.portfolio_value)

            if not self._breaker.is_open():
                claude_summary = f"Cycle skipped: circuit breaker tripped - {self._breaker._state.trip_reason}"
                logger.warning(claude_summary)
            else:
                messages: list[dict] = [{"role": "user", "content": self._build_initial_user_message(snapshot)}]
                earnings_calendar: list[dict] = []

                for _ in range(MAX_TOOL_ITERATIONS):
                    response = self._anthropic.messages.create(
                        model=MODEL, max_tokens=MAX_TOKENS, system=self._system_prompt,
                        tools=_TOOL_DEFINITIONS, messages=messages,  # type: ignore[arg-type]
                    )
                    input_tokens += response.usage.input_tokens
                    output_tokens += response.usage.output_tokens
                    messages.append({"role": "assistant", "content": response.content})

                    if response.stop_reason == "end_turn":
                        for block in response.content:
                            if hasattr(block, "text"):
                                claude_summary = block.text
                        break

                    if response.stop_reason == "tool_use":
                        tool_results = []
                        for block in response.content:
                            if block.type != "tool_use":
                                continue
                            total_tool_calls += 1
                            result_str, placed, rejected, ec = await self._dispatch_tool(
                                block.name, block.input, cycle_id, snapshot, earnings_calendar
                            )
                            trades_placed += placed
                            trades_rejected += rejected
                            if ec:
                                earnings_calendar = ec
                            tool_results.append({"type": "tool_result", "tool_use_id": block.id, "content": result_str})
                        messages.append({"role": "user", "content": tool_results})
                    else:
                        break

        except Exception as exc:
            error_msg = str(exc)
            logger.exception("Cycle %d failed", cycle_id)

        finished_at = _now()
        duration = (datetime.datetime.fromisoformat(finished_at) - datetime.datetime.fromisoformat(started_at)).total_seconds()
        await self._logger.finish_cycle(cycle_id, CycleRecord(
            started_at=started_at, finished_at=finished_at, duration_seconds=duration,
            portfolio_value=snapshot.portfolio_value if snapshot else None,
            cash_available=snapshot.cash_available if snapshot else None,
            open_positions=len(snapshot.open_positions) if snapshot else None,
            trades_placed=trades_placed, trades_rejected=trades_rejected,
            claude_input_tokens=input_tokens, claude_output_tokens=output_tokens,
            total_tool_calls=total_tool_calls, cycle_summary=claude_summary,
            error_message=error_msg, aggressiveness_level=self._profile.level,
        ))
        logger.info("Cycle %d complete: placed=%d rejected=%d tokens=%d+%d duration=%.1fs",
                    cycle_id, trades_placed, trades_rejected, input_tokens, output_tokens, duration)
        return CycleResult(cycle_id=cycle_id, started_at=started_at, finished_at=finished_at,
                           trades_placed=trades_placed, trades_rejected=trades_rejected,
                           claude_summary=claude_summary, input_tokens=input_tokens,
                           output_tokens=output_tokens, total_tool_calls=total_tool_calls, error=error_msg)

    async def _gather_initial_context(self) -> MarketSnapshot:
        portfolio, positions, orders, watchlist = await asyncio.gather(
            self._mcp.get_portfolio(), self._mcp.get_equity_positions(),
            self._mcp.get_equity_orders(state="open"),
            self._mcp.get_watchlist_items(self._watchlist_name),
        )
        index_quotes = await self._mcp.get_index_quotes(["SPY", "QQQ", "DIA", "VIX"])
        today_trades = await self._logger.get_today_trades()
        portfolio_value = float(portfolio.get("equity", portfolio.get("portfolio_value", 0)) or 0)
        cash = float(portfolio.get("cash", 0) or 0)
        buying_power = float(portfolio.get("buying_power", cash) or 0)
        symbols: list[str] = []
        items = watchlist if isinstance(watchlist, list) else watchlist.get("results", watchlist.get("items", []))
        for item in items:
            sym = item.get("symbol") or item.get("ticker") or ""
            if sym:
                symbols.append(sym.upper())
        return MarketSnapshot(
            timestamp=_now(), portfolio_value=portfolio_value, cash_available=cash, buying_power=buying_power,
            open_positions=positions if isinstance(positions, list) else positions.get("results", []),
            index_quotes=index_quotes if isinstance(index_quotes, dict) else {},
            watchlist_symbols=symbols[:self._config.get("max_stocks_to_analyze_per_cycle", 10)],
            today_orders=orders if isinstance(orders, list) else orders.get("results", []),
            trades_today_count=len(today_trades),
            daily_pnl=sum(float(t.get("pnl") or 0) for t in today_trades),
        )

    def _build_initial_user_message(self, s: MarketSnapshot) -> str:
        return (
            f"Current time: {s.timestamp}\n"
            f"Portfolio: ${s.portfolio_value:,.2f} | Cash: ${s.cash_available:,.2f} | Buying power: ${s.buying_power:,.2f}\n"
            f"Open positions: {_format_positions(s.open_positions)}\n"
            f"Indices: {_format_indices(s.index_quotes)}\n"
            f"Watchlist ({len(s.watchlist_symbols)}): {', '.join(s.watchlist_symbols) or '(empty)'}\n"
            f"Trades today: {s.trades_today_count}/{self._profile.max_trades_per_day} | Daily P&L: ${s.daily_pnl:+,.2f}\n"
            f"Mode: {'DRY RUN (no real orders)' if self._dry_run else 'LIVE TRADING'}\n\n"
            f"Analyze positions and watchlist, use tools as needed, place trades if appropriate."
        )

    async def _dispatch_tool(self, tool_name: str, tool_input: dict, cycle_id: int,
                              snapshot: MarketSnapshot, earnings_calendar: list[dict]) -> tuple[str, int, int, list[dict]]:
        placed = rejected = 0
        new_ec = earnings_calendar
        try:
            if tool_name == "get_equity_quotes":
                result = await self._mcp.get_equity_quotes(tool_input["symbols"])
            elif tool_name == "get_equity_historicals":
                result = await self._mcp.get_equity_historicals(tool_input["symbol"], tool_input["interval"], tool_input["span"])
            elif tool_name == "get_equity_fundamentals":
                result = await self._mcp.get_equity_fundamentals(tool_input["symbols"])
            elif tool_name == "get_equity_positions":
                result = await self._mcp.get_equity_positions()
            elif tool_name == "get_equity_orders":
                result = await self._mcp.get_equity_orders(tool_input.get("state", "open"))
            elif tool_name == "get_earnings_calendar":
                result = await self._mcp.get_earnings_calendar()
                new_ec = result if isinstance(result, list) else result.get("results", result.get("earnings", []))
            elif tool_name == "get_earnings_results":
                result = await self._mcp.get_earnings_results(tool_input["symbol"])
            elif tool_name == "get_index_quotes":
                result = await self._mcp.get_index_quotes(tool_input["symbols"])
            elif tool_name == "place_equity_order":
                result, placed, rejected = await self._handle_place_order(tool_input, cycle_id, snapshot, earnings_calendar)
            elif tool_name == "cancel_equity_order":
                result = await self._mcp.cancel_equity_order(tool_input["order_id"])
            else:
                result = {"error": f"Unknown tool: {tool_name}"}
        except Exception as exc:
            logger.exception("Tool %s failed", tool_name)
            result = {"error": str(exc)}
        return json.dumps(result, default=str), placed, rejected, new_ec

    async def _handle_place_order(self, tool_input: dict, cycle_id: int,
                                   snapshot: MarketSnapshot, earnings_calendar: list[dict]) -> tuple[Any, int, int]:
        symbol = tool_input.get("symbol", "").upper()
        side = tool_input.get("side", "")
        quantity = int(tool_input.get("quantity", 0))
        order_type = tool_input.get("order_type", "market")
        confidence = float(tool_input.get("confidence", 0.0))
        rationale = tool_input.get("rationale", "")
        limit_price = tool_input.get("limit_price")
        stop_price = tool_input.get("stop_price")

        quote = await self._mcp.get_equity_quotes([symbol])
        current_price = _extract_price(quote, symbol)
        estimated_value = current_price * quantity if current_price else 0.0

        fundamentals: dict = {}
        try:
            fund_data = await self._mcp.get_equity_fundamentals([symbol])
            fundamentals = fund_data[0] if isinstance(fund_data, list) and fund_data else (fund_data if isinstance(fund_data, dict) else {})
        except Exception:
            pass

        is_valid, rejection_reason = validate_trade_request(
            symbol=symbol, side=side, quantity=quantity, confidence=confidence,
            current_price=current_price, fundamentals=fundamentals,
            earnings_calendar=earnings_calendar, profile=self._profile, portfolio_value=snapshot.portfolio_value,
        )
        if is_valid:
            cb_allowed, cb_reason = await self._breaker.check_trade(symbol, side, estimated_value)
            if not cb_allowed:
                is_valid, rejection_reason = False, cb_reason

        trade = TradeRecord(
            timestamp=_now(), cycle_id=cycle_id, symbol=symbol, side=side, quantity=quantity,
            order_type=order_type, estimated_value=estimated_value, confidence=confidence, rationale=rationale,
            limit_price=limit_price, stop_price=stop_price, aggressiveness_level=self._profile.level,
            dry_run=self._dry_run, status="", rejection_reason=None,
        )

        if not is_valid:
            trade.status = "rejected"
            trade.rejection_reason = rejection_reason
            await self._logger.log_trade(trade)
            logger.info("Order REJECTED %s %d %s: %s", side, quantity, symbol, rejection_reason)
            return {"status": "rejected", "reason": rejection_reason}, 0, 1

        if self._dry_run:
            trade.status = "dry_run"
            await self._logger.log_trade(trade)
            await self._breaker.record_trade_placed(symbol, side)
            logger.info("DRY RUN: %s %d %s @ ~$%.2f (confidence=%.0f%%)", side, quantity, symbol, current_price, confidence * 100)
            return {"status": "dry_run", "message": f"DRY RUN: Would {side} {quantity} {symbol} @ ~${current_price:.2f}",
                    "estimated_value": estimated_value, "confidence": confidence}, 1, 0

        try:
            await self._mcp.review_equity_order(symbol=symbol, side=side, quantity=quantity,
                order_type=order_type, limit_price=limit_price, stop_price=stop_price,
                time_in_force=tool_input.get("time_in_force", "gfd"))
            order_result = await self._mcp.place_equity_order(symbol=symbol, side=side, quantity=quantity,
                order_type=order_type, limit_price=limit_price, stop_price=stop_price,
                time_in_force=tool_input.get("time_in_force", "gfd"))
            trade.status = "placed"
            trade.robinhood_order_id = order_result.get("id")
            await self._logger.log_trade(trade)
            await self._breaker.record_trade_placed(symbol, side)
            logger.info("Order PLACED: %s %d %s id=%s", side, quantity, symbol, trade.robinhood_order_id)
            return order_result, 1, 0
        except Exception as exc:
            trade.status = "error"
            trade.rejection_reason = str(exc)
            await self._logger.log_trade(trade)
            return {"status": "error", "message": str(exc)}, 0, 1


def _extract_price(quote_data: Any, symbol: str) -> float:
    if isinstance(quote_data, list):
        for q in quote_data:
            if isinstance(q, dict) and q.get("symbol", q.get("ticker", "")).upper() == symbol.upper():
                return float(q.get("last_trade_price") or q.get("price") or 0)
    elif isinstance(quote_data, dict):
        return float(quote_data.get("last_trade_price") or quote_data.get("price") or 0)
    return 0.0


def _format_positions(positions: list[dict]) -> str:
    if not positions:
        return "none"
    parts = []
    for p in positions[:8]:
        sym = p.get("symbol", p.get("ticker", "?"))
        qty = p.get("quantity", "?")
        pct = p.get("percent_change", p.get("return_rate", ""))
        parts.append(f"{sym}x{qty}" + (f" ({float(pct):.1%})" if pct else ""))
    return ", ".join(parts)


def _format_indices(index_quotes: dict) -> str:
    parts = [f"{s}: {index_quotes[s].get('last_trade_price') or index_quotes[s].get('price') or '?'}" for s in ["SPY", "QQQ", "DIA", "VIX"] if s in index_quotes]
    return " | ".join(parts) if parts else "unavailable"


def _now() -> str:
    return datetime.datetime.now(datetime.timezone.utc).isoformat()
