# ADR-0002: Trendiness scoring formula

Status: Accepted

## Context
The scanner ranks all `*-SWAP-USDT` contracts from `GET /quote/v1/contract/ticker/24hr` (public, unsigned). Ranking must be transparent and explainable per metric.

## Decision
Three metrics per symbol, each min-max normalized to [0,1] across the current universe (equal values normalize to 0.5), then combined linearly:

```
score = w_volume * norm(log1p(quoteVolume))
      + w_momentum * norm(|priceChangePercent|)
      + w_spike * norm(volumeSpikeRatio)
```

- `volumeSpikeRatio` = current 24h quote volume / EMA of that symbol's prior snapshots (falls back to 1.0 with no history).
- Default weights `0.4 / 0.4 / 0.2`, required to sum to 1 (validated at load). Weights live in `ScannerConfig` and a feature flag selects an alternate weight set without redeploy.
- Symbols below `min_quote_volume` are excluded before normalization.
- Each `TrendingSymbol` row stores raw values, normalized values, weights and the final score, so the dashboard can show a per-metric breakdown.

## Properties (enforced by property-based tests)
- Score is within [0,1].
- Monotonic: raising a metric while holding others fixed never lowers the score.
- Deterministic and order-independent.

## Consequences
Min-max is sensitive to outliers; log1p on volume dampens this. Weights are a judgment call; revisit with backtest evidence.
