"""Safety circuit breaker - daily loss limit, trade count cap, crash-safe via SQLite."""

from __future__ import annotations

import datetime
import logging

from agent.logger import CircuitBreakerEvent, CircuitBreakerState, TradeLogger
from agent.strategy import AggressivenessProfile

logger = logging.getLogger(__name__)


class CircuitBreaker:
    def __init__(self, profile: AggressivenessProfile, trade_logger: TradeLogger) -> None:
        self._profile = profile
        self._logger = trade_logger
        self._state = CircuitBreakerState()
        self._portfolio_value: float = 0.0

    async def initialize(self, current_portfolio_value: float) -> None:
        self._portfolio_value = current_portfolio_value
        today = datetime.date.today().isoformat()
        existing = await self._logger.get_circuit_breaker_state()
        if existing and existing.last_reset_date == today:
            self._state = existing
            logger.info("Circuit breaker restored: trades_today=%d tripped=%s",
                        self._state.trades_today, self._state.is_tripped)
        else:
            self._state = CircuitBreakerState(last_reset_date=today, portfolio_open_value=current_portfolio_value)
            await self._persist()
            await self._logger.log_circuit_breaker_event(
                CircuitBreakerEvent(timestamp=_now(), event_type="reset", reason=f"New trading day {today}")
            )
            logger.info("Circuit breaker reset. Portfolio open value: $%.2f", current_portfolio_value)

    async def reset_daily(self, current_portfolio_value: float) -> None:
        if self._state.last_reset_date != datetime.date.today().isoformat():
            await self.initialize(current_portfolio_value)

    async def check_trade(self, symbol: str, side: str, estimated_value: float) -> tuple[bool, str]:
        if self._state.is_tripped:
            reason = f"Circuit breaker TRIPPED: {self._state.trip_reason}"
            await self._log_block(reason)
            return False, reason
        if self._state.trades_today >= self._profile.max_trades_per_day:
            reason = f"Daily trade limit reached: {self._state.trades_today}/{self._profile.max_trades_per_day}"
            await self._log_block(reason)
            return False, reason
        return True, ""

    async def record_trade_placed(self, symbol: str, side: str) -> None:
        self._state.trades_today += 1
        await self._persist()

    async def record_pnl(self, pnl_dollars: float) -> None:
        self._state.daily_pnl_dollars += pnl_dollars
        await self._persist()
        await self._check_loss_limit()

    async def check_portfolio_loss(self, current_portfolio_value: float) -> None:
        self._portfolio_value = current_portfolio_value
        if self._state.portfolio_open_value <= 0:
            return
        unrealized_loss = self._state.portfolio_open_value - current_portfolio_value
        total_loss = unrealized_loss - self._state.daily_pnl_dollars
        loss_pct = total_loss / self._state.portfolio_open_value
        if loss_pct >= self._profile.max_daily_loss_pct:
            await self._trip(f"Daily loss limit: {loss_pct:.1%} >= {self._profile.max_daily_loss_pct:.1%} max")

    def is_open(self) -> bool:
        return not self._state.is_tripped

    async def emergency_stop(self) -> None:
        await self._trip("Emergency stop requested (SIGUSR1)")

    async def _check_loss_limit(self) -> None:
        if self._state.portfolio_open_value <= 0:
            return
        loss_pct = -self._state.daily_pnl_dollars / self._state.portfolio_open_value
        if loss_pct >= self._profile.max_daily_loss_pct:
            await self._trip(f"Realized daily loss limit: {loss_pct:.1%} >= {self._profile.max_daily_loss_pct:.1%} max")

    async def _trip(self, reason: str) -> None:
        if self._state.is_tripped:
            return
        self._state.is_tripped = True
        self._state.trip_reason = reason
        await self._persist()
        await self._logger.log_circuit_breaker_event(
            CircuitBreakerEvent(
                timestamp=_now(), event_type="tripped", reason=reason,
                daily_loss_pct=abs(self._state.daily_pnl_dollars) / max(self._state.portfolio_open_value, 1),
                trades_count=self._state.trades_today,
            )
        )
        logger.warning("CIRCUIT BREAKER TRIPPED: %s", reason)

    async def _log_block(self, reason: str) -> None:
        await self._logger.log_circuit_breaker_event(
            CircuitBreakerEvent(timestamp=_now(), event_type="trade_blocked", reason=reason,
                                trades_count=self._state.trades_today)
        )

    async def _persist(self) -> None:
        await self._logger.set_circuit_breaker_state(self._state)


def _now() -> str:
    return datetime.datetime.now(datetime.timezone.utc).isoformat()
