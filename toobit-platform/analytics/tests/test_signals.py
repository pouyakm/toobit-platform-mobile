import numpy as np
import pandas as pd
import pytest

from signals.config import ConfigError, SignalConfig
from signals.engine import analyze, compute_features, decide
from tests.conftest import ENV, make_candles


def test_config_requires_every_threshold():
    env = dict(ENV)
    env.pop("SIGNALS_Z_ENTRY")
    with pytest.raises(ConfigError, match="SIGNALS_Z_ENTRY"):
        SignalConfig.from_env(env)


def test_config_rejects_inverted_bands():
    with pytest.raises(ConfigError):
        SignalConfig.from_env(ENV | {"SIGNALS_Z_EXIT": "2.0"})


def test_features_present_and_finite(cfg, candles):
    f = compute_features(candles, cfg)
    for k in ("hv", "iv_proxy", "iv_hv_ratio", "z", "delta", "gamma", "theta", "vega"):
        assert np.isfinite(f[k])


def test_insufficient_candles(cfg):
    with pytest.raises(ValueError):
        compute_features(make_candles(10), cfg)


def test_missing_columns(cfg):
    with pytest.raises(ValueError):
        compute_features(pd.DataFrame({"close": [1.0] * 100}), cfg)


def test_entry_long_on_positive_deviation(cfg):
    f = {"z": 2.0, "iv_hv_ratio": 1.0, "delta": 0.52, "spot": 1, "hv": 1, "iv_proxy": 1}
    s = decide("BTC-SWAP-USDT", f, cfg)
    assert s.action == "enter_long" and 0 < s.confidence < 1


def test_entry_short_on_negative_deviation(cfg):
    f = {"z": -2.0, "iv_hv_ratio": 1.0, "delta": 0.48}
    assert decide("BTC-SWAP-USDT", f, cfg).action == "enter_short"


def test_hold_inside_band(cfg):
    assert decide("X-SWAP-USDT", {"z": 0.5, "iv_hv_ratio": 1.0, "delta": 0.5}, cfg).action == "hold"


def test_hold_when_ratio_out_of_range(cfg):
    assert decide("X-SWAP-USDT", {"z": 3.0, "iv_hv_ratio": 99.0, "delta": 0.5}, cfg).action == "hold"


def test_exit_only_when_in_position_and_reverted(cfg):
    f = {"z": 0.1, "iv_hv_ratio": 1.0, "delta": 0.5}
    assert decide("X-SWAP-USDT", f, cfg, in_position=True).action == "exit"
    assert decide("X-SWAP-USDT", f, cfg, in_position=False).action == "hold"


def test_thresholds_are_configurable(cfg):
    f = {"z": 1.0, "iv_hv_ratio": 1.0, "delta": 0.5}
    assert decide("X-SWAP-USDT", f, cfg).action == "hold"
    loose = cfg.with_overrides(z_entry=0.9)
    assert decide("X-SWAP-USDT", f, loose).action == "enter_long"


def test_explainability_contract(cfg, candles):
    s = analyze("ETH-SWAP-USDT", candles, cfg)
    assert s.explain["methodology"] == "options_proxy_v1"
    assert 1 <= len(s.explain["top_features"]) <= 3
    assert "NOT a calibrated" in s.calibration_note
