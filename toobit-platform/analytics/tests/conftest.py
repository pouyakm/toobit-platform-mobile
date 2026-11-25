import numpy as np
import pandas as pd
import pytest

from signals.config import SignalConfig

ENV = {
    "SIGNALS_HV_WINDOW": "30", "SIGNALS_IV_WINDOW": "14", "SIGNALS_IV_SCALE": "1.0",
    "SIGNALS_TENOR_DAYS": "7", "SIGNALS_RATE": "0.0", "SIGNALS_Z_WINDOW": "48",
    "SIGNALS_Z_ENTRY": "1.5", "SIGNALS_Z_EXIT": "0.4", "SIGNALS_RATIO_MIN": "0.3",
    "SIGNALS_RATIO_MAX": "5.0", "SIGNALS_PERIODS_PER_YEAR": "8760",
}


@pytest.fixture
def cfg() -> SignalConfig:
    return SignalConfig.from_env(ENV)


def make_candles(n=120, drift=0.0, vol=0.01, seed=7, start=100.0) -> pd.DataFrame:
    rng = np.random.default_rng(seed)
    rets = drift + vol * rng.standard_normal(n)
    close = start * np.exp(np.cumsum(rets))
    open_ = np.concatenate([[start], close[:-1]])
    spread = np.abs(rng.standard_normal(n)) * vol * close * 0.5
    high = np.maximum(open_, close) + spread
    low = np.minimum(open_, close) - spread
    return pd.DataFrame({"open": open_, "high": high, "low": low, "close": close, "volume": 1.0})


@pytest.fixture
def candles() -> pd.DataFrame:
    return make_candles()
