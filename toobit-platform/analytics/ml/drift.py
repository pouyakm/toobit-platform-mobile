"""Feature drift monitoring: PSI and two-sample KS vs. a training snapshot."""
from __future__ import annotations

import numpy as np


def psi(expected: np.ndarray, actual: np.ndarray, bins: int = 10) -> float:
    expected = np.asarray(expected, dtype=float)
    actual = np.asarray(actual, dtype=float)
    edges = np.unique(np.quantile(expected, np.linspace(0, 1, bins + 1)))
    if len(edges) < 3:
        return 0.0
    edges[0], edges[-1] = -np.inf, np.inf
    e = np.histogram(expected, edges)[0] / len(expected)
    a = np.histogram(actual, edges)[0] / len(actual)
    e, a = np.clip(e, 1e-6, None), np.clip(a, 1e-6, None)
    return float(np.sum((a - e) * np.log(a / e)))


def ks_statistic(a: np.ndarray, b: np.ndarray) -> float:
    a, b = np.sort(np.asarray(a, dtype=float)), np.sort(np.asarray(b, dtype=float))
    grid = np.concatenate([a, b])
    cdf_a = np.searchsorted(a, grid, side="right") / len(a)
    cdf_b = np.searchsorted(b, grid, side="right") / len(b)
    return float(np.max(np.abs(cdf_a - cdf_b)))


def drift_report(train: dict[str, np.ndarray], live: dict[str, np.ndarray],
                 psi_warn: float, ks_warn: float) -> dict:
    out = {}
    for name, tr in train.items():
        if name not in live or len(live[name]) == 0:
            continue
        p, k = psi(tr, live[name]), ks_statistic(tr, live[name])
        out[name] = {"psi": p, "ks": k, "warn": bool(p > psi_warn or k > ks_warn)}
    return out
