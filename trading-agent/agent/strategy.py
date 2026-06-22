"""Profile loading, position sizing, and pre-trade validation."""

from __future__ import annotations

import dataclasses
import datetime
import json
import pathlib


@dataclasses.dataclass
class AggressivenessProfile:
    level: int
    name: str
    check_interval_minutes: int
    max_position_pct: float
    confidence_threshold: float
    stop_loss_pct: float
    take_profit_pct: float
    max_daily_loss_pct: float
    max_trades_per_day: int
    max_open_positions: int
    stock_universe: str
    min_market_cap_billions: float
    min_avg_daily_volume: int
    allow_earnings_week_trades: bool
    prompt_tone: str
    min_price: float
    max_price: float


def load_profile(level: int, profiles_dir: str = "profiles") -> AggressivenessProfile:
    if not 1 <= level <= 5:
        raise ValueError(f"aggressiveness_level must be 1-5, got {level}")
    path = next(pathlib.Path(profiles_dir).glob(f"{level}_*.json"))
    data = json.loads(path.read_text())
    return AggressivenessProfile(**data)


def calculate_position_size(
    portfolio_value: float,
    profile: AggressivenessProfile,
    current_price: float,
    confidence: float,
) -> int:
    if current_price <= 0:
        return 0
    base_dollars = portfolio_value * profile.max_position_pct
    confidence_scalar = min(1.0, confidence / max(profile.confidence_threshold, 0.01))
    target_dollars = base_dollars * confidence_scalar
    shares = int(target_dollars / current_price)
    return max(1, shares)


def validate_trade_request(
    symbol: str,
    side: str,
    quantity: int,
    confidence: float,
    current_price: float,
    fundamentals: dict,
    earnings_calendar: list[dict],
    profile: AggressivenessProfile,
    portfolio_value: float,
) -> tuple[bool, str]:
    if confidence < profile.confidence_threshold:
        return False, (
            f"Confidence {confidence:.0%} below threshold {profile.confidence_threshold:.0%} "
            f"for level {profile.level} ({profile.name})"
        )
    position_value = quantity * current_price
    max_allowed = portfolio_value * profile.max_position_pct
    if position_value > max_allowed:
        return False, (
            f"Position size ${position_value:,.0f} exceeds max ${max_allowed:,.0f} "
            f"({profile.max_position_pct:.0%} of portfolio)"
        )
    if not (profile.min_price <= current_price <= profile.max_price):
        return False, (
            f"Price ${current_price:.2f} outside allowed range "
            f"[${profile.min_price:.2f}, ${profile.max_price:.2f}]"
        )
    market_cap = fundamentals.get("market_cap") or fundamentals.get("market_capitalization")
    if market_cap and profile.min_market_cap_billions > 0:
        market_cap_b = float(market_cap) / 1e9
        if market_cap_b < profile.min_market_cap_billions:
            return False, f"Market cap ${market_cap_b:.1f}B below minimum ${profile.min_market_cap_billions}B"
    volume = fundamentals.get("average_volume") or fundamentals.get("volume")
    if volume and int(volume) < profile.min_avg_daily_volume:
        return False, f"Volume {int(volume):,} below minimum {profile.min_avg_daily_volume:,}"
    if not profile.allow_earnings_week_trades and side == "buy":
        if _is_earnings_week(symbol, earnings_calendar):
            return False, f"{symbol} has earnings within 5 days - earnings trades disabled at this profile level"
    return True, ""


def _is_earnings_week(symbol: str, earnings_calendar: list[dict]) -> bool:
    today = datetime.date.today()
    cutoff = today + datetime.timedelta(days=5)
    for entry in earnings_calendar:
        ticker = entry.get("symbol") or entry.get("ticker") or ""
        if ticker.upper() != symbol.upper():
            continue
        date_str = entry.get("report_date") or entry.get("date") or ""
        try:
            report_date = datetime.date.fromisoformat(date_str[:10])
            if today <= report_date <= cutoff:
                return True
        except (ValueError, TypeError):
            continue
    return False
