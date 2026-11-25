"""Walk-forward backtester with transaction-cost + slippage model.

Positions are -1/0/+1 target series computed from information available at bar t and
executed at bar t+1 (no look-ahead). Costs are charged on every position change.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Callable, Iterator

import numpy as np
import pandas as pd


@dataclass(frozen=True)
class CostModel:
    fee_bps: float        # per side, e.g. taker fee
    slippage_bps: float   # per side

    @property
    def per_side(self) -> float:
        return (self.fee_bps + self.slippage_bps) / 1e4


def walk_forward_splits(n: int, train: int, test: int, step: int | None = None) -> Iterator[tuple[slice, slice]]:
    """Yield (train_slice, test_slice) with strictly increasing, non-overlapping test windows."""
    if train < 1 or test < 1:
        raise ValueError("train and test must be >= 1")
    step = step or test
    start = 0
    while start + train + test <= n:
        yield slice(start, start + train), slice(start + train, start + train + test)
        start += step


def simulate(close: pd.Series, target_position: pd.Series, costs: CostModel) -> pd.DataFrame:
    """Net per-bar returns. position[t] decided at close of t, earns return of t+1."""
    ret = close.pct_change().fillna(0.0)
    pos = target_position.reindex(close.index).fillna(0.0).shift(1).fillna(0.0)
    turnover = pos.diff().abs().fillna(pos.abs())
    gross = pos * ret
    net = gross - turnover * costs.per_side
    return pd.DataFrame({"pos": pos, "gross": gross, "net": net, "turnover": turnover})


def _max_drawdown(equity: np.ndarray) -> float:
    peak = np.maximum.accumulate(equity)
    return float(np.max((peak - equity) / peak)) if len(equity) else 0.0


def report(sim: pd.DataFrame, periods_per_year: float) -> dict:
    net = sim["net"].to_numpy()
    if len(net) == 0:
        return {k: 0.0 for k in ("sharpe", "sortino", "max_drawdown", "hit_rate", "avg_r", "trades", "total_return")}
    mean, std = net.mean(), net.std(ddof=1) if len(net) > 1 else 0.0
    downside = net[net < 0]
    dstd = math.sqrt(np.mean(downside**2)) if len(downside) else 0.0
    ann = math.sqrt(periods_per_year)
    equity = np.cumprod(1.0 + net)

    # Trades = contiguous same-sign position runs; R multiple = trade return / mean |loss|.
    pos = sim["pos"].to_numpy()
    trade_returns, run_ret, cur = [], 0.0, 0.0
    for p, r in zip(pos, net):
        if p != cur:
            if cur != 0.0:
                trade_returns.append(run_ret)
            run_ret, cur = 0.0, p
        if cur != 0.0:
            run_ret += r
    if cur != 0.0:
        trade_returns.append(run_ret)
    tr = np.array(trade_returns, dtype=float)
    losses = tr[tr < 0]
    risk_unit = abs(losses.mean()) if len(losses) else (abs(tr).mean() if len(tr) else 0.0)
    return {
        "sharpe": float(mean / std * ann) if std > 0 else 0.0,
        "sortino": float(mean / dstd * ann) if dstd > 0 else 0.0,
        "max_drawdown": _max_drawdown(equity),
        "hit_rate": float((tr > 0).mean()) if len(tr) else 0.0,
        "avg_r": float(tr.mean() / risk_unit) if risk_unit > 0 else 0.0,
        "trades": int(len(tr)),
        "total_return": float(equity[-1] - 1.0),
    }


def walk_forward(
    candles: pd.DataFrame,
    strategy: Callable[[pd.DataFrame], pd.Series],
    costs: CostModel,
    train: int, test: int, periods_per_year: float,
) -> dict:
    """`strategy(history)` returns a position series for history; only the test slice is scored.
    The test slice is computed on train+test history so indicators are warmed up, but the
    strategy never sees bars after the position's decision time."""
    folds = []
    for tr, te in walk_forward_splits(len(candles), train, test):
        hist = candles.iloc[: te.stop]
        pos = strategy(hist)
        sim = simulate(hist["close"], pos, costs).iloc[te]
        folds.append({"test_start": int(te.start), "test_end": int(te.stop), **report(sim, periods_per_year)})
    if not folds:
        raise ValueError("series too short for the requested train/test sizes")
    agg = {k: float(np.mean([f[k] for f in folds])) for k in
           ("sharpe", "sortino", "max_drawdown", "hit_rate", "avg_r", "total_return")}
    agg["trades"] = int(sum(f["trades"] for f in folds))
    return {"folds": folds, "aggregate": agg}
