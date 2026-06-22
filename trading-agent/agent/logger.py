"""Async SQLite logger for trades, cycles, decisions, and circuit breaker events."""

from __future__ import annotations

import dataclasses
import datetime
from typing import Any

import aiosqlite


@dataclasses.dataclass
class TradeRecord:
    timestamp: str
    cycle_id: int
    symbol: str
    side: str
    quantity: int
    order_type: str
    estimated_value: float
    confidence: float
    rationale: str
    status: str  # 'placed' | 'rejected' | 'dry_run' | 'error'
    aggressiveness_level: int
    dry_run: bool
    limit_price: float | None = None
    stop_price: float | None = None
    rejection_reason: str | None = None
    robinhood_order_id: str | None = None
    filled_price: float | None = None
    filled_quantity: int | None = None
    filled_at: str | None = None
    pnl: float | None = None


@dataclasses.dataclass
class CycleRecord:
    started_at: str
    aggressiveness_level: int
    finished_at: str | None = None
    duration_seconds: float | None = None
    portfolio_value: float | None = None
    cash_available: float | None = None
    open_positions: int | None = None
    trades_placed: int = 0
    trades_rejected: int = 0
    claude_input_tokens: int | None = None
    claude_output_tokens: int | None = None
    total_tool_calls: int = 0
    cycle_summary: str | None = None
    error_message: str | None = None


@dataclasses.dataclass
class DecisionRecord:
    cycle_id: int
    timestamp: str
    decision_type: str
    symbol: str | None = None
    confidence: float | None = None
    rationale: str | None = None
    outcome: str | None = None


@dataclasses.dataclass
class CircuitBreakerEvent:
    timestamp: str
    event_type: str
    reason: str
    daily_loss_pct: float | None = None
    trades_count: int | None = None


@dataclasses.dataclass
class CircuitBreakerState:
    trades_today: int = 0
    daily_pnl_dollars: float = 0.0
    is_tripped: bool = False
    trip_reason: str = ""
    last_reset_date: str = ""
    portfolio_open_value: float = 0.0


_SCHEMA = """
CREATE TABLE IF NOT EXISTS trades (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp           TEXT    NOT NULL,
    cycle_id            INTEGER NOT NULL,
    symbol              TEXT    NOT NULL,
    side                TEXT    NOT NULL,
    quantity            INTEGER NOT NULL,
    order_type          TEXT    NOT NULL,
    limit_price         REAL,
    stop_price          REAL,
    estimated_value     REAL    NOT NULL,
    confidence          REAL    NOT NULL,
    rationale           TEXT    NOT NULL,
    status              TEXT    NOT NULL,
    rejection_reason    TEXT,
    robinhood_order_id  TEXT,
    filled_price        REAL,
    filled_quantity     INTEGER,
    filled_at           TEXT,
    pnl                 REAL,
    aggressiveness_level INTEGER NOT NULL,
    dry_run             INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS cycles (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at           TEXT    NOT NULL,
    finished_at          TEXT,
    duration_seconds     REAL,
    portfolio_value      REAL,
    cash_available       REAL,
    open_positions       INTEGER,
    trades_placed        INTEGER DEFAULT 0,
    trades_rejected      INTEGER DEFAULT 0,
    claude_input_tokens  INTEGER,
    claude_output_tokens INTEGER,
    total_tool_calls     INTEGER DEFAULT 0,
    cycle_summary        TEXT,
    error_message        TEXT,
    aggressiveness_level INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS decisions (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    cycle_id       INTEGER NOT NULL,
    timestamp      TEXT    NOT NULL,
    symbol         TEXT,
    decision_type  TEXT    NOT NULL,
    confidence     REAL,
    rationale      TEXT,
    outcome        TEXT,
    FOREIGN KEY (cycle_id) REFERENCES cycles(id)
);

CREATE TABLE IF NOT EXISTS circuit_breaker_events (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp      TEXT    NOT NULL,
    event_type     TEXT    NOT NULL,
    reason         TEXT    NOT NULL,
    daily_loss_pct REAL,
    trades_count   INTEGER
);

CREATE TABLE IF NOT EXISTS daily_state (
    state_date           TEXT PRIMARY KEY,
    trades_today         INTEGER DEFAULT 0,
    daily_pnl_dollars    REAL    DEFAULT 0.0,
    portfolio_open_value REAL,
    is_tripped           INTEGER DEFAULT 0,
    trip_reason          TEXT
);

CREATE INDEX IF NOT EXISTS idx_trades_timestamp   ON trades(timestamp);
CREATE INDEX IF NOT EXISTS idx_trades_symbol      ON trades(symbol);
CREATE INDEX IF NOT EXISTS idx_trades_cycle_id    ON trades(cycle_id);
CREATE INDEX IF NOT EXISTS idx_decisions_cycle_id ON decisions(cycle_id);
CREATE INDEX IF NOT EXISTS idx_cycles_started_at  ON cycles(started_at);
"""


class TradeLogger:
    def __init__(self, db_path: str) -> None:
        self._db_path = db_path
        self._db: aiosqlite.Connection | None = None

    async def initialize(self) -> None:
        self._db = await aiosqlite.connect(self._db_path)
        self._db.row_factory = aiosqlite.Row
        await self._db.executescript(_SCHEMA)
        await self._db.commit()

    async def close(self) -> None:
        if self._db:
            await self._db.close()

    async def start_cycle(self, record: CycleRecord) -> int:
        assert self._db
        cursor = await self._db.execute(
            """INSERT INTO cycles (started_at, aggressiveness_level) VALUES (?, ?)""",
            (record.started_at, record.aggressiveness_level),
        )
        await self._db.commit()
        return cursor.lastrowid  # type: ignore[return-value]

    async def finish_cycle(self, cycle_id: int, record: CycleRecord) -> None:
        assert self._db
        await self._db.execute(
            """UPDATE cycles SET finished_at=?, duration_seconds=?, portfolio_value=?,
               cash_available=?, open_positions=?, trades_placed=?, trades_rejected=?,
               claude_input_tokens=?, claude_output_tokens=?, total_tool_calls=?,
               cycle_summary=?, error_message=? WHERE id=?""",
            (record.finished_at, record.duration_seconds, record.portfolio_value,
             record.cash_available, record.open_positions, record.trades_placed,
             record.trades_rejected, record.claude_input_tokens, record.claude_output_tokens,
             record.total_tool_calls, record.cycle_summary, record.error_message, cycle_id),
        )
        await self._db.commit()

    async def log_trade(self, trade: TradeRecord) -> int:
        assert self._db
        cursor = await self._db.execute(
            """INSERT INTO trades
               (timestamp, cycle_id, symbol, side, quantity, order_type,
                limit_price, stop_price, estimated_value, confidence, rationale,
                status, rejection_reason, robinhood_order_id,
                filled_price, filled_quantity, filled_at, pnl, aggressiveness_level, dry_run)
               VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
            (trade.timestamp, trade.cycle_id, trade.symbol, trade.side, trade.quantity,
             trade.order_type, trade.limit_price, trade.stop_price, trade.estimated_value,
             trade.confidence, trade.rationale, trade.status, trade.rejection_reason,
             trade.robinhood_order_id, trade.filled_price, trade.filled_quantity,
             trade.filled_at, trade.pnl, trade.aggressiveness_level, int(trade.dry_run)),
        )
        await self._db.commit()
        return cursor.lastrowid  # type: ignore[return-value]

    async def log_decision(self, decision: DecisionRecord) -> None:
        assert self._db
        await self._db.execute(
            """INSERT INTO decisions (cycle_id, timestamp, symbol, decision_type, confidence, rationale, outcome)
               VALUES (?,?,?,?,?,?,?)""",
            (decision.cycle_id, decision.timestamp, decision.symbol, decision.decision_type,
             decision.confidence, decision.rationale, decision.outcome),
        )
        await self._db.commit()

    async def log_circuit_breaker_event(self, event: CircuitBreakerEvent) -> None:
        assert self._db
        await self._db.execute(
            """INSERT INTO circuit_breaker_events (timestamp, event_type, reason, daily_loss_pct, trades_count)
               VALUES (?,?,?,?,?)""",
            (event.timestamp, event.event_type, event.reason, event.daily_loss_pct, event.trades_count),
        )
        await self._db.commit()

    async def get_circuit_breaker_state(self) -> CircuitBreakerState | None:
        assert self._db
        today = datetime.date.today().isoformat()
        async with self._db.execute("SELECT * FROM daily_state WHERE state_date=?", (today,)) as cursor:
            row = await cursor.fetchone()
        if not row:
            return None
        return CircuitBreakerState(
            trades_today=row["trades_today"],
            daily_pnl_dollars=row["daily_pnl_dollars"],
            is_tripped=bool(row["is_tripped"]),
            trip_reason=row["trip_reason"] or "",
            last_reset_date=row["state_date"],
            portfolio_open_value=row["portfolio_open_value"] or 0.0,
        )

    async def set_circuit_breaker_state(self, state: CircuitBreakerState) -> None:
        assert self._db
        await self._db.execute(
            """INSERT INTO daily_state
               (state_date, trades_today, daily_pnl_dollars, portfolio_open_value, is_tripped, trip_reason)
               VALUES (?,?,?,?,?,?)
               ON CONFLICT(state_date) DO UPDATE SET
               trades_today=excluded.trades_today, daily_pnl_dollars=excluded.daily_pnl_dollars,
               portfolio_open_value=excluded.portfolio_open_value,
               is_tripped=excluded.is_tripped, trip_reason=excluded.trip_reason""",
            (state.last_reset_date, state.trades_today, state.daily_pnl_dollars,
             state.portfolio_open_value, int(state.is_tripped), state.trip_reason),
        )
        await self._db.commit()

    async def get_today_trades(self) -> list[dict[str, Any]]:
        assert self._db
        today = datetime.date.today().isoformat()
        async with self._db.execute(
            "SELECT * FROM trades WHERE timestamp LIKE ? ORDER BY timestamp", (f"{today}%",)
        ) as cursor:
            rows = await cursor.fetchall()
        return [dict(r) for r in rows]
