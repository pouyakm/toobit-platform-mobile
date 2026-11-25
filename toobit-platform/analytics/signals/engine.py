"""Signal engine: option-proxy features -> explainable entry/exit signal.

Pure function of candles + config. No I/O, no exchange access (ADR-0001).
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional

import numpy as np
import pandas as pd

from analytics.options_proxy import (
    METHODOLOGY_TAG, bs_call_greeks, garman_klass_volatility, historical_volatility,
)
from signals.config import SignalConfig

REQUIRED_COLUMNS = ("open", "high", "low", "close")


@dataclass
class Signal:
    symbol: str
    action: str            # "enter_long" | "enter_short" | "exit" | "hold"
    confidence: float      # 0..1, heuristic (see calibration_note)
    features: dict
    explain: dict
    model_version: Optional[str] = None
    calibration_note: str = (
        "Confidence is a monotone squash of |z| and is NOT a calibrated probability."
    )

    def to_dict(self) -> dict:
        return {
            "symbol": self.symbol,
            "action": self.action,
            "confidence": self.confidence,
            "features": self.features,
            "explain": self.explain,
            "modelVersion": self.model_version,
            "calibrationNote": self.calibration_note,
        }


def compute_features(candles: pd.DataFrame, cfg: SignalConfig) -> dict:
    missing = [c for c in REQUIRED_COLUMNS if c not in candles.columns]
    if missing:
        raise ValueError(f"candles missing columns: {missing}")
    need = max(cfg.hv_window + 1, cfg.iv_window, cfg.z_window)
    if len(candles) < need:
        raise ValueError(f"need at least {need} candles, got {len(candles)}")

    close = candles["close"].to_numpy(dtype=float)
    hv = historical_volatility(close, cfg.hv_window, cfg.periods_per_year)
    iv = garman_klass_volatility(
        candles["open"].to_numpy(), candles["high"].to_numpy(),
        candles["low"].to_numpy(), close, cfg.iv_window, cfg.periods_per_year,
    ) * cfg.iv_scale
    if iv <= 0 or hv <= 0:
        raise ValueError("degenerate volatility (flat prices)")

    window = close[-cfg.z_window:]
    std = float(np.std(window, ddof=1))
    z = 0.0 if std == 0 else float((close[-1] - window.mean()) / std)
    spot = float(close[-1])
    g = bs_call_greeks(spot, spot, cfg.tenor_days, cfg.rate, iv)
    return {
        "spot": spot, "hv": hv, "iv_proxy": iv, "iv_hv_ratio": iv / hv, "z": z,
        "delta": g.delta, "gamma": g.gamma, "theta": g.theta, "vega": g.vega,
    }


def decide(symbol: str, features: dict, cfg: SignalConfig, in_position: bool = False) -> Signal:
    z, ratio = features["z"], features["iv_hv_ratio"]
    ratio_ok = cfg.ratio_min <= ratio <= cfg.ratio_max
    if in_position and abs(z) <= cfg.z_exit:
        action, reason = "exit", "deviation reverted inside z_exit band"
    elif not in_position and abs(z) >= cfg.z_entry and ratio_ok:
        action = "enter_long" if z > 0 else "enter_short"
        reason = "trend-following entry: |z| >= z_entry and IV/HV in range"
    else:
        action, reason = "hold", "no threshold crossed or IV/HV out of range"

    confidence = float(1.0 - np.exp(-abs(z) / max(cfg.z_entry, 1e-9))) if action != "hold" else 0.0
    contributions = {
        "z": abs(z) / cfg.z_entry,
        "iv_hv_ratio": 1.0 - abs(ratio - (cfg.ratio_min + cfg.ratio_max) / 2)
                           / ((cfg.ratio_max - cfg.ratio_min) / 2) if ratio_ok else 0.0,
        "delta": abs(features["delta"] - 0.5),
    }
    top_k = sorted(contributions.items(), key=lambda kv: kv[1], reverse=True)[:3]
    return Signal(
        symbol=symbol, action=action, confidence=confidence, features=features,
        explain={
            "methodology": METHODOLOGY_TAG,
            "reason": reason,
            "ratio_in_range": ratio_ok,
            "top_features": [{"name": k, "contribution": round(v, 6)} for k, v in top_k],
        },
    )


def analyze(symbol: str, candles: pd.DataFrame, cfg: SignalConfig, in_position: bool = False) -> Signal:
    return decide(symbol, compute_features(candles, cfg), cfg, in_position)
