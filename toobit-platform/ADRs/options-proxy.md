# ADR-0001: Options-proxy methodology

Status: Accepted (defaults confirmed by owner)

## Context
Toobit USDT-M futures provide no options chain, so real implied volatility and Greeks are unavailable. The strategy still wants option-like risk signals. Hard constraint: no live options data from Toobit; the proxy must be documented here and enforced by tests.

## Decision
Compute a **synthetic ATM option** per symbol from candle data only.

- Timeframe: 1h candles for signals (configurable `signals.timeframe`).
- **HV**: annualized stdev of close-to-close log returns over a 30-period window (`signals.hv_window`), annualization factor `sqrt(24*365)` for 1h bars.
- **IV proxy**: Garman-Klass range estimator over a short window (`signals.iv_window`, default 14), annualized the same way, then scaled by `signals.iv_scale` (default 1.0). It is a realized-range volatility proxy, not a market-implied quantity.
- **Greeks**: Black-Scholes on a synthetic ATM call (S = K = last close), tenor `signals.tenor_days` (default 7), risk-free rate `signals.rate` (default 0), sigma = IV proxy. Delta, gamma, theta (per day), vega (per 1 vol point).
- **Signal logic**: IV/HV ratio and price deviation from a rolling mean (z-score) are combined with delta sign (trend direction). Entry when `|z| >= z_entry` and `iv_hv_ratio` in `[ratio_min, ratio_max]`; exit when `|z| <= z_exit`. Every threshold is read from config.

## Enforcement
- `analytics/tests/test_options_proxy.py` asserts: no Toobit option endpoint is referenced, inputs are candle arrays only, Greeks match closed-form values, and thresholds come from config (no literals in `signals/`).
- Each signal's `explain` records the proxy methodology tag `options_proxy_v1`.

## Consequences
Signals are not tradable option prices; they are a volatility-regime and mean-deviation heuristic. Backtesting is the only validation. Revisit if Toobit publishes options data.
