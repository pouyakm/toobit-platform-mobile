"""Contract tests: Python's outbound signal body and the shared fixture must satisfy contracts/signal-ingest.schema.json.
The .NET side deserializes the SAME fixture (backend/tests/Toobit.UnitTests/ContractTests.cs)."""
import json
import pathlib

import jsonschema
import pytest

from app.publisher import build_signal_body
from signals.engine import analyze
from tests.conftest import make_candles

CONTRACTS = pathlib.Path(__file__).resolve().parents[2] / "contracts"
SCHEMA = json.loads((CONTRACTS / "signal-ingest.schema.json").read_text())


def test_shared_fixture_is_valid():
    jsonschema.validate(json.loads((CONTRACTS / "examples" / "signal.json").read_text()), SCHEMA)


def test_real_signal_from_engine_matches_contract(cfg):
    sig = analyze("BTC-SWAP-USDT", make_candles(120), cfg).to_dict()
    body = json.loads(build_signal_body(sig, 60000.0))
    jsonschema.validate(body, SCHEMA)


def test_contract_rejects_drift():
    bad = json.loads((CONTRACTS / "examples" / "signal.json").read_text())
    bad["extra"] = 1  # additionalProperties false: renamed/extra fields are caught
    with pytest.raises(jsonschema.ValidationError):
        jsonschema.validate(bad, SCHEMA)
    bad2 = json.loads((CONTRACTS / "examples" / "signal.json").read_text())
    bad2["explain"]["methodology"] = "live_options_feed"
    with pytest.raises(jsonschema.ValidationError):
        jsonschema.validate(bad2, SCHEMA)
