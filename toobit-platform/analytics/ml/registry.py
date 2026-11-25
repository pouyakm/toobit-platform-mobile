"""Model registry (local FS for MVP) and ONNX loading path. See ADR-0004.

Version string: ``model_name@semver+hash8`` where hash8 = sha256(artifact)[:8].
"""
from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

import numpy as np

_VERSION_RE = re.compile(r"^(?P<name>[a-z0-9_\-]+)@(?P<semver>\d+\.\d+\.\d+)\+(?P<hash>[0-9a-f]{8})$")


def make_version(name: str, semver: str, artifact_bytes: bytes) -> str:
    return f"{name}@{semver}+{hashlib.sha256(artifact_bytes).hexdigest()[:8]}"


def parse_version(version: str) -> dict:
    m = _VERSION_RE.match(version)
    if not m:
        raise ValueError(f"invalid model version: {version!r}")
    return m.groupdict()


@dataclass
class ModelEntry:
    version: str
    path: Path
    feature_names: list[str]


class ModelRegistry:
    def __init__(self, root: str | Path):
        self.root = Path(root)

    def register(self, name: str, semver: str, onnx_bytes: bytes, feature_names: list[str]) -> ModelEntry:
        version = make_version(name, semver, onnx_bytes)
        d = self.root / version
        d.mkdir(parents=True, exist_ok=True)
        (d / "model.onnx").write_bytes(onnx_bytes)
        (d / "meta.json").write_text(json.dumps({"version": version, "feature_names": feature_names}))
        return ModelEntry(version, d / "model.onnx", feature_names)

    def get(self, version: str) -> ModelEntry:
        parse_version(version)
        d = self.root / version
        model = d / "model.onnx"
        if not model.is_file():
            raise FileNotFoundError(f"model {version} not in registry")
        meta = json.loads((d / "meta.json").read_text())
        if make_version(parse_version(version)["name"], parse_version(version)["semver"], model.read_bytes()) != version:
            raise ValueError(f"artifact hash mismatch for {version}")
        return ModelEntry(version, model, meta["feature_names"])


class OnnxScorer:
    """Fixed inference contract: float32[1, N] ordered by feature_names -> score in [0,1]."""

    def __init__(self, entry: ModelEntry):
        import onnxruntime as ort  # imported lazily so the service runs without a model
        self.entry = entry
        self._sess = ort.InferenceSession(str(entry.path), providers=["CPUExecutionProvider"])
        self._input = self._sess.get_inputs()[0].name

    def score(self, features: dict) -> float:
        missing = [n for n in self.entry.feature_names if n not in features]
        if missing:
            raise ValueError(f"missing features: {missing}")
        x = np.array([[features[n] for n in self.entry.feature_names]], dtype=np.float32)
        out = self._sess.run(None, {self._input: x})[0]
        return float(np.clip(np.ravel(out)[0], 0.0, 1.0))


def load_active(root: str | Path, version: Optional[str]) -> Optional[OnnxScorer]:
    """Return a scorer for `version`, or None (signals still work without a model)."""
    if not version:
        return None
    return OnnxScorer(ModelRegistry(root).get(version))
