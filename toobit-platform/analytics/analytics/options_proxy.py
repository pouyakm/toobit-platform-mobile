"""Option-proxy math. See ADRs/options-proxy.md (ADR-0001).

IMPORTANT: Toobit provides no options data. Everything here is derived from
candle arrays only. "IV" is a Garman-Klass realized-range volatility proxy and
Greeks are Black-Scholes values for a *synthetic* ATM call. Nothing in this
module performs I/O or references any exchange endpoint.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np

METHODOLOGY_TAG = "options_proxy_v1"


def _norm_cdf(x: float) -> float:
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


def _norm_pdf(x: float) -> float:
    return math.exp(-0.5 * x * x) / math.sqrt(2.0 * math.pi)


def historical_volatility(close: np.ndarray, window: int, periods_per_year: float) -> float:
    """Annualized stdev of close-to-close log returns over the last `window` returns."""
    close = np.asarray(close, dtype=float)
    if len(close) < window + 1:
        raise ValueError(f"need at least {window + 1} closes, got {len(close)}")
    if np.any(close <= 0):
        raise ValueError("closes must be positive")
    rets = np.diff(np.log(close))[-window:]
    return float(np.std(rets, ddof=1) * math.sqrt(periods_per_year))


def garman_klass_volatility(
    open_: np.ndarray, high: np.ndarray, low: np.ndarray, close: np.ndarray,
    window: int, periods_per_year: float,
) -> float:
    """Annualized Garman-Klass estimator over the last `window` bars (the IV proxy base)."""
    o, h, l, c = (np.asarray(a, dtype=float)[-window:] for a in (open_, high, low, close))
    if len(c) < window:
        raise ValueError(f"need at least {window} bars, got {len(c)}")
    if np.any(l <= 0) or np.any(h < l):
        raise ValueError("invalid OHLC: require 0 < low <= high")
    hl = np.log(h / l) ** 2
    co = np.log(c / o) ** 2
    var = np.mean(0.5 * hl - (2.0 * math.log(2.0) - 1.0) * co)
    var = max(float(var), 0.0)
    return math.sqrt(var * periods_per_year)


@dataclass(frozen=True)
class Greeks:
    delta: float
    gamma: float
    theta: float  # per calendar day
    vega: float   # per 1 volatility point (0.01)
    price: float


def bs_call_greeks(spot: float, strike: float, tenor_days: float, rate: float, sigma: float) -> Greeks:
    """Black-Scholes call Greeks for the synthetic option."""
    if spot <= 0 or strike <= 0:
        raise ValueError("spot and strike must be positive")
    if tenor_days <= 0:
        raise ValueError("tenor_days must be positive")
    if sigma <= 0:
        raise ValueError("sigma must be positive")
    t = tenor_days / 365.0
    sqrt_t = math.sqrt(t)
    d1 = (math.log(spot / strike) + (rate + 0.5 * sigma**2) * t) / (sigma * sqrt_t)
    d2 = d1 - sigma * sqrt_t
    disc = math.exp(-rate * t)
    price = spot * _norm_cdf(d1) - strike * disc * _norm_cdf(d2)
    delta = _norm_cdf(d1)
    gamma = _norm_pdf(d1) / (spot * sigma * sqrt_t)
    theta_year = -(spot * _norm_pdf(d1) * sigma) / (2.0 * sqrt_t) - rate * strike * disc * _norm_cdf(d2)
    vega = spot * _norm_pdf(d1) * sqrt_t * 0.01
    return Greeks(delta=delta, gamma=gamma, theta=theta_year / 365.0, vega=vega, price=price)
