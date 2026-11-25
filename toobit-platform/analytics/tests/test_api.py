import json
import time

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from app.security import HmacVerifier, sign
from tests.conftest import make_candles

KEY = b"unit-test-key"


@pytest.fixture
def client(cfg):
    app = create_app(cfg=cfg, verifier=HmacVerifier({"k1": KEY}, max_skew=60), scorer=None)
    return TestClient(app)


def _post(client, path, payload, key=KEY, ts=None, key_id="k1"):
    body = json.dumps(payload).encode()
    ts = str(ts if ts is not None else time.time())
    headers = {"X-Key-Id": key_id, "X-Timestamp": ts, "X-Signature": sign(key, "POST", path.split("?")[0], ts, body),  # contract: path only, query not signed
               "Content-Type": "application/json", "X-Correlation-Id": "corr-123"}
    return client.post(path, content=body, headers=headers)


def payload(symbol="BTC-SWAP-USDT"):
    c = make_candles(120)
    return {"symbol": symbol, "candles": c[["open", "high", "low", "close", "volume"]].to_dict("records")}


def test_health_needs_no_auth(client):
    assert client.get("/health").json()["status"] == "ok"


def test_analyze_ok_and_correlation_id_propagates(client):
    r = _post(client, "/analyze", payload())
    assert r.status_code == 200
    assert r.json()["correlationId"] == "corr-123"
    assert r.headers["X-Correlation-Id"] == "corr-123"
    assert r.json()["action"] in {"hold", "enter_long", "enter_short", "exit"}


def test_signal_endpoint(client):
    assert _post(client, "/signal/BTC-SWAP-USDT", payload()).status_code == 200
    assert _post(client, "/signal/ETH-SWAP-USDT", payload()).status_code == 422


def test_unsigned_rejected(client):
    assert client.post("/analyze", json=payload()).status_code == 401


def test_bad_signature_rejected(client):
    assert _post(client, "/analyze", payload(), key=b"wrong").status_code == 401


def test_stale_timestamp_rejected(client):
    assert _post(client, "/analyze", payload(), ts=time.time() - 3600).status_code == 401


def test_replay_rejected(client):
    body = json.dumps(payload()).encode()
    ts = str(time.time())
    h = {"X-Key-Id": "k1", "X-Timestamp": ts, "X-Signature": sign(KEY, "POST", "/analyze", ts, body),
         "Content-Type": "application/json"}
    assert client.post("/analyze", content=body, headers=h).status_code == 200
    assert client.post("/analyze", content=body, headers=h).status_code == 401


def test_invalid_symbol_and_candles_validated(client):
    assert _post(client, "/analyze", payload("btcusdt")).status_code == 422
    bad = payload()
    bad["candles"][0]["low"] = -1
    assert _post(client, "/analyze", bad).status_code == 422


def test_too_few_candles_is_422(client):
    p = payload()
    p["candles"] = p["candles"][:5]
    assert _post(client, "/analyze", p).status_code == 422


def test_signal_publish_forwards_to_backend_with_correlation(client, monkeypatch):
    seen = {}

    def fake_publish(sig, price, url, key_id, key, corr, **kw):
        seen.update(url=url, corr=corr, price=price, action=sig["action"])
        return 202

    monkeypatch.setattr("app.main.publish", fake_publish)
    monkeypatch.setenv("BACKEND_SIGNAL_URL", "http://api/internal/signals")
    monkeypatch.setenv("INTERNAL_HMAC_KEY_ID", "k1")
    monkeypatch.setenv("INTERNAL_HMAC_KEY", "secret")
    r = _post(client, "/signal/BTC-SWAP-USDT?publish=true", payload())
    assert r.status_code == 200 and r.json()["published"] == 202
    assert seen["corr"] == "corr-123" and seen["url"].endswith("/internal/signals")
