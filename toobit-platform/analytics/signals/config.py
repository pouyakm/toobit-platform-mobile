"""Signal thresholds. Every value comes from the environment (see .env.example).

There are deliberately NO numeric defaults here: a missing threshold is a startup
error (fail-fast), so nothing is silently hardcoded (ADR-0001).
"""
from __future__ import annotations

import os
from dataclasses import dataclass, fields, replace
from typing import Mapping


class ConfigError(RuntimeError):
    pass


@dataclass(frozen=True)
class SignalConfig:
    hv_window: int
    iv_window: int
    iv_scale: float
    tenor_days: float
    rate: float
    z_window: int
    z_entry: float
    z_exit: float
    ratio_min: float
    ratio_max: float
    periods_per_year: float

    def validate(self) -> "SignalConfig":
        if self.hv_window < 2 or self.iv_window < 1 or self.z_window < 2:
            raise ConfigError("windows too small")
        if self.z_exit >= self.z_entry:
            raise ConfigError("z_exit must be < z_entry")
        if self.ratio_min >= self.ratio_max:
            raise ConfigError("ratio_min must be < ratio_max")
        if self.tenor_days <= 0 or self.iv_scale <= 0 or self.periods_per_year <= 0:
            raise ConfigError("tenor_days, iv_scale, periods_per_year must be > 0")
        return self

    @classmethod
    def from_env(cls, env: Mapping[str, str] | None = None) -> "SignalConfig":
        env = os.environ if env is None else env
        values = {}
        for f in fields(cls):
            key = f"SIGNALS_{f.name.upper()}"
            if key not in env:
                raise ConfigError(f"missing required setting {key}")
            raw = env[key]
            try:
                values[f.name] = int(raw) if f.type in ("int", int) else float(raw)
            except ValueError as exc:
                raise ConfigError(f"{key} is not numeric: {raw!r}") from exc
        return cls(**values).validate()

    def with_overrides(self, **kw: float) -> "SignalConfig":
        return replace(self, **kw).validate()
