# Robinhood Agentic Trading Partner

A 24/7 autonomous stock trading agent powered by Claude AI + Robinhood MCP. Adjustable aggressiveness levels 1-5 (Ultra Cautious to Very Aggressive).

## Aggressiveness Levels

| Level | Name | Interval | Max Position | Confidence | Stop Loss | Take Profit |
|-------|------|----------|-------------|------------|-----------|-------------|
| 1 | Ultra Cautious | 60 min | 5% | 90% | 3% | 10% |
| 2 | Cautious | 30 min | 8% | 80% | 5% | 15% |
| 3 | Moderate | 15 min | 12% | 70% | 7% | 20% |
| 4 | Aggressive | 10 min | 18% | 60% | 10% | 30% |
| 5 | Very Aggressive | 5 min | 25% | 50% | 15% | 50% |

## Setup

```bash
pip install -r requirements.txt
cp .env.example .env
# Edit .env with your API keys
python -m agent.main
```

## Safety First

Config defaults to `dry_run: true`. The agent logs all decisions without placing real orders until you explicitly set `dry_run: false`.

Change aggressiveness by editing `config.json`:
```json
{"aggressiveness_level": 2}
```

## Emergency Stop
```bash
kill -SIGTERM <pid>   # graceful shutdown
kill -SIGUSR1 <pid>   # trips circuit breaker immediately
```

## Disclaimer
Experimental software. Automated trading carries significant risk. Start with dry-run mode and never risk money you cannot afford to lose.
