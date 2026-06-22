"""Entry point: event loop, market-hours guard, signal handling."""

from __future__ import annotations

import asyncio
import datetime
import json
import logging
import os
import pathlib
import signal
import sys
import zoneinfo

from dotenv import load_dotenv

from agent.circuit_breaker import CircuitBreaker
from agent.claude_trader import ClaudeTrader
from agent.logger import TradeLogger
from agent.mcp_client import RobinhoodMCPClient
from agent.strategy import load_profile

_ET = zoneinfo.ZoneInfo("America/New_York")
_MARKET_OPEN = datetime.time(9, 30)
_MARKET_CLOSE = datetime.time(16, 0)

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)-8s %(name)s - %(message)s", datefmt="%Y-%m-%d %H:%M:%S")
logger = logging.getLogger(__name__)


def load_config(path: str = "config.json") -> dict:
    data = json.loads(pathlib.Path(path).read_text())
    for key in ["aggressiveness_level", "robinhood_mcp_command", "db_path"]:
        if key not in data:
            raise ValueError(f"config.json missing required key: {key}")
    return data


def is_market_open() -> bool:
    now = datetime.datetime.now(_ET)
    return now.weekday() < 5 and _MARKET_OPEN <= now.time() < _MARKET_CLOSE


def seconds_until_market_open() -> float:
    now = datetime.datetime.now(_ET)
    if now.weekday() >= 5:
        days_ahead = 7 - now.weekday()
        next_open = datetime.datetime.combine(now.date() + datetime.timedelta(days=days_ahead), _MARKET_OPEN, tzinfo=_ET)
    elif now.time() < _MARKET_OPEN:
        next_open = datetime.datetime.combine(now.date(), _MARKET_OPEN, tzinfo=_ET)
    else:
        days_ahead = 1 if now.weekday() < 4 else 3
        next_open = datetime.datetime.combine(now.date() + datetime.timedelta(days=days_ahead), _MARKET_OPEN, tzinfo=_ET)
    return max(0.0, (next_open - now).total_seconds())


async def run_trading_loop(trader: ClaudeTrader, breaker: CircuitBreaker, config: dict,
                            interval_seconds: int, stop_event: asyncio.Event) -> None:
    market_hours_only: bool = config.get("market_hours_only", True)
    while not stop_event.is_set():
        if market_hours_only and not is_market_open():
            wait = seconds_until_market_open()
            logger.info("Market closed. Waiting %.0f minutes until next open.", wait / 60)
            try:
                await asyncio.wait_for(stop_event.wait(), timeout=wait)
            except asyncio.TimeoutError:
                pass
            continue
        logger.info("=== Starting trading cycle ===")
        try:
            result = await asyncio.wait_for(trader.run_cycle(), timeout=config.get("cycle_timeout_seconds", 180))
            logger.info("Cycle %d done - placed=%d rejected=%d | %s",
                        result.cycle_id, result.trades_placed, result.trades_rejected,
                        result.claude_summary[:120] if result.claude_summary else "(no summary)")
        except asyncio.TimeoutError:
            logger.error("Cycle timed out after %ds", config.get("cycle_timeout_seconds", 180))
        except Exception:
            logger.exception("Unexpected error in trading cycle")
        try:
            await asyncio.wait_for(stop_event.wait(), timeout=interval_seconds)
        except asyncio.TimeoutError:
            pass


async def main() -> None:
    load_dotenv()
    config_path = sys.argv[1] if len(sys.argv) > 1 else "config.json"
    config = load_config(config_path)
    level = int(config["aggressiveness_level"])
    profile = load_profile(level)
    logger.info("Profile: Level %d - %s | interval=%dmin dry_run=%s",
                level, profile.name, profile.check_interval_minutes, config.get("dry_run", True))
    api_key = os.environ.get(config.get("anthropic_api_key_env", "ANTHROPIC_API_KEY"), "")
    if not api_key:
        raise SystemExit("ANTHROPIC_API_KEY not set in environment")
    trade_logger = TradeLogger(config["db_path"])
    await trade_logger.initialize()
    mcp_client = RobinhoodMCPClient(config["robinhood_mcp_command"], config.get("robinhood_mcp_env_vars", {}))
    await mcp_client.start()
    portfolio_data = await mcp_client.get_portfolio()
    portfolio_value = float(portfolio_data.get("equity", portfolio_data.get("portfolio_value", 100_000)) or 100_000)
    breaker = CircuitBreaker(profile, trade_logger)
    await breaker.initialize(portfolio_value)
    trader = ClaudeTrader(mcp_client, breaker, trade_logger, profile, config, api_key)
    stop_event = asyncio.Event()

    def _handle_stop(*_): stop_event.set()
    def _handle_emergency(*_): asyncio.get_event_loop().create_task(breaker.emergency_stop())

    signal.signal(signal.SIGTERM, _handle_stop)
    signal.signal(signal.SIGINT, _handle_stop)
    try: signal.signal(signal.SIGUSR1, _handle_emergency)
    except (AttributeError, OSError): pass

    try:
        await run_trading_loop(trader, breaker, config, profile.check_interval_minutes * 60, stop_event)
    finally:
        await mcp_client.stop()
        await trade_logger.close()
        logger.info("Agent shut down cleanly.")


if __name__ == "__main__":
    asyncio.run(main())
