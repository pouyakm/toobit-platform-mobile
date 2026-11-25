"""Enforces ADR-0001: candle-only inputs, correct math, no hardcoded thresholds, no Toobit access."""
import math
import pathlib
import re

import numpy as np
import pytest

from analytics.options_proxy import bs_call_greeks, garman_klass_volatility, historical_volatility

ROOT = pathlib.Path(__file__).resolve().parents[1]


def test_bs_atm_known_values():
    # S=K=100, T=1y (365d), r=0, sigma=0.2 -> price 7.9656, delta 0.5398, vega(per 1pt) 0.3945
    g = bs_call_greeks(100, 100, 365, 0.0, 0.2)
    assert g.price == pytest.approx(7.9656, abs=1e-3)
    assert g.delta == pytest.approx(0.5398, abs=1e-3)
    assert g.vega == pytest.approx(0.3970, abs=2e-3)
    assert g.gamma == pytest.approx(0.01985, abs=1e-4)
    assert g.theta < 0


def test_greek_signs_and_bounds():
    g = bs_call_greeks(50, 50, 7, 0.01, 0.8)
    assert 0 < g.delta < 1 and g.gamma > 0 and g.vega > 0 and g.theta < 0


@pytest.mark.parametrize("kw", [dict(sigma=0), dict(tenor_days=0), dict(spot=-1)])
def test_greeks_reject_bad_input(kw):
    args = dict(spot=100, strike=100, tenor_days=7, rate=0.0, sigma=0.5) | kw
    with pytest.raises(ValueError):
        bs_call_greeks(args["spot"], args["strike"], args["tenor_days"], args["rate"], args["sigma"])


def test_hv_matches_manual():
    close = np.array([100, 101, 99, 102, 103, 101.0])
    rets = np.diff(np.log(close))
    assert historical_volatility(close, 5, 8760) == pytest.approx(np.std(rets, ddof=1) * math.sqrt(8760))


def test_hv_needs_enough_data():
    with pytest.raises(ValueError):
        historical_volatility(np.array([1.0, 2.0]), 5, 8760)


def test_gk_zero_range_flat_is_zero():
    a = np.full(20, 100.0)
    assert garman_klass_volatility(a, a, a, a, 14, 8760) == 0.0


def test_gk_rejects_invalid_ohlc():
    o = np.full(14, 100.0)
    with pytest.raises(ValueError):
        garman_klass_volatility(o, o * 0.9, o, o, 14, 8760)  # high < low


def test_no_exchange_or_options_endpoint_in_analytics_code():
    banned = re.compile(r"toobit|api\.toobit|/option|options_chain|requests\.get|httpx\.", re.I)
    for folder in ("analytics", "signals", "backtesting"):
        for py in (ROOT / folder).rglob("*.py"):
            text = py.read_text()
            # Allow the word "Toobit" only in docstrings/comments describing the constraint.
            code = "\n".join(l for l in text.splitlines() if not l.strip().startswith(("#", '"', "'")))
            hits = [m.group(0) for m in banned.finditer(code) if "toobit" not in m.group(0).lower()]
            assert not hits, f"{py} references forbidden I/O: {hits}"


def test_signals_package_has_no_numeric_threshold_literals():
    """Thresholds must come from SignalConfig (env), not literals in signals/engine.py."""
    src = (ROOT / "signals" / "engine.py").read_text()
    forbidden = re.compile(r"(z_entry|z_exit|ratio_min|ratio_max)\s*=\s*[-+]?\d")
    assert not forbidden.search(src)
