import numpy as np
import pandas as pd
import pytest
from hypothesis import given, settings, strategies as st

from backtesting.backtester import CostModel, report, simulate, walk_forward, walk_forward_splits
from ml.drift import drift_report, ks_statistic, psi
from ml.registry import ModelRegistry, make_version, parse_version
from tests.conftest import make_candles


def test_splits_do_not_overlap_and_move_forward():
    splits = list(walk_forward_splits(100, train=30, test=10))
    assert splits
    for (tr, te), (_, te2) in zip(splits, splits[1:]):
        assert tr.stop == te.start and te2.start >= te.stop
    assert splits[-1][1].stop <= 100


def test_costs_reduce_returns():
    c = make_candles(200, drift=0.001)
    pos = pd.Series(1.0, index=c.index)
    free = simulate(c["close"], pos, CostModel(0, 0))["net"].sum()
    paid = simulate(c["close"], pos, CostModel(5, 5))["net"].sum()
    assert paid < free


def test_no_lookahead_position_shifted():
    close = pd.Series([100.0, 110.0, 99.0, 120.0])
    pos = pd.Series([0.0, 1.0, 0.0, 0.0])     # decide to go long at the close of bar 1
    sim = simulate(close, pos, CostModel(0, 0))
    assert sim["gross"].iloc[1] == 0.0        # the +10% bar-1 move is NOT earned by a bar-1 decision
    assert sim["gross"].iloc[2] == pytest.approx(99.0 / 110.0 - 1.0)  # earned on the next bar
    assert sim["gross"].iloc[3] == 0.0        # flat again after the decision to exit


def test_report_has_required_metrics():
    c = make_candles(300, drift=0.0005)
    pos = (c["close"] > c["close"].rolling(10).mean()).astype(float) * 2 - 1
    rep = report(simulate(c["close"], pos, CostModel(4, 2)), 8760)
    for k in ("sharpe", "sortino", "max_drawdown", "hit_rate", "avg_r"):
        assert k in rep and np.isfinite(rep[k])
    assert 0 <= rep["max_drawdown"] <= 1 and 0 <= rep["hit_rate"] <= 1


def test_walk_forward_end_to_end():
    c = make_candles(400, drift=0.0003)
    strat = lambda h: ((h["close"] > h["close"].rolling(20).mean()).astype(float) * 2 - 1)
    out = walk_forward(c, strat, CostModel(4, 2), train=100, test=50, periods_per_year=8760)
    assert len(out["folds"]) >= 3 and "sharpe" in out["aggregate"]


def test_walk_forward_too_short():
    with pytest.raises(ValueError):
        walk_forward(make_candles(50), lambda h: pd.Series(0.0, index=h.index), CostModel(0, 0), 100, 50, 8760)


def test_psi_ks_zero_for_identical_and_large_for_shifted():
    rng = np.random.default_rng(1)
    a = rng.normal(0, 1, 5000)
    assert psi(a, a.copy()) < 1e-6 and ks_statistic(a, a.copy()) == 0
    shifted = rng.normal(3, 1, 5000)
    assert psi(a, shifted) > 0.2 and ks_statistic(a, shifted) > 0.5
    rep = drift_report({"z": a}, {"z": shifted}, psi_warn=0.2, ks_warn=0.2)
    assert rep["z"]["warn"] is True


def test_version_format_and_registry_integrity(tmp_path):
    v = make_version("trend", "1.2.3", b"abc")
    assert parse_version(v)["semver"] == "1.2.3"
    reg = ModelRegistry(tmp_path)
    e = reg.register("trend", "1.0.0", b"fake-onnx", ["z", "iv_hv_ratio"])
    assert reg.get(e.version).feature_names == ["z", "iv_hv_ratio"]
    e.path.write_bytes(b"tampered")
    with pytest.raises(ValueError, match="hash mismatch"):
        reg.get(e.version)
    with pytest.raises(ValueError):
        parse_version("bad-version")


# --- property-based: backtest invariants -------------------------------------------------
@settings(max_examples=60, deadline=None)
@given(st.lists(st.floats(min_value=-0.05, max_value=0.05), min_size=10, max_size=200),
       st.lists(st.sampled_from([-1.0, 0.0, 1.0]), min_size=10, max_size=200))
def test_property_drawdown_bounded_and_costs_never_help(rets, poss):
    n = min(len(rets), len(poss))
    close = pd.Series(100 * np.cumprod(1 + np.array(rets[:n])))
    pos = pd.Series(poss[:n])
    free = simulate(close, pos, CostModel(0, 0))
    paid = simulate(close, pos, CostModel(5, 5))
    assert paid["net"].sum() <= free["net"].sum() + 1e-12
    assert 0.0 <= report(paid, 8760)["max_drawdown"] <= 1.0
