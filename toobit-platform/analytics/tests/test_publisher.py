import json

import httpx
import pytest

from app.publisher import build_signal_body, publish, signed_headers
from app.security import sign

# Cross-language contract: the same vector is asserted by the .NET InternalHmacTests.
DOTNET_VECTOR = "2ccb6406411a2fd8bf7b92a4b8a2cde9780a6deaa35e90cd5475a82e86bdd261"


def test_signature_matches_dotnet_vector():
    assert sign(b"unit-test-key", "POST", "/internal/signals", "1700000000", b'{"a":1}') == DOTNET_VECTOR


SIG = {"symbol": "BTC-SWAP-USDT", "action": "enter_long", "confidence": 0.7, "features": {"z": 2.0}, "explain": {"methodology": "options_proxy_v1"}}


def test_body_is_compact_json_and_signed_over_exact_bytes():
    body = build_signal_body(SIG, 60000.0)
    assert b" " not in body.replace(b"options_proxy_v1", b"")
    h = signed_headers("k1", b"key", "http://api:8080/internal/signals", body, "corr-9", now=1700000000)
    assert h["X-Signature"] == sign(b"key", "POST", "/internal/signals", "1700000000", body)
    assert h["X-Correlation-Id"] == "corr-9"


def test_publish_success_and_correlation_header_forwarded():
    seen = {}

    def handler(req: httpx.Request):
        seen["h"] = dict(req.headers)
        seen["body"] = json.loads(req.content)
        return httpx.Response(202, json={"shadow": True})

    c = httpx.Client(transport=httpx.MockTransport(handler))
    assert publish(SIG, 60000.0, "http://api/internal/signals", "k1", b"key", "corr-1", client=c) == 202
    assert seen["h"]["x-correlation-id"] == "corr-1" and seen["body"]["referencePrice"] == 60000.0


def test_publish_retries_5xx_with_backoff_then_succeeds():
    calls, sleeps = [], []

    def handler(req):
        calls.append(1)
        return httpx.Response(503 if len(calls) < 3 else 202)

    c = httpx.Client(transport=httpx.MockTransport(handler))
    assert publish(SIG, 1.0, "http://api/internal/signals", "k1", b"k", "c", client=c, sleep=sleeps.append, rng=lambda: 0.5) == 202
    assert len(calls) == 3 and sleeps == [0.1875, 0.375]


def test_publish_does_not_retry_4xx_and_gives_up_after_max_attempts():
    c = httpx.Client(transport=httpx.MockTransport(lambda r: httpx.Response(401)))
    assert publish(SIG, 1.0, "http://api/internal/signals", "k1", b"k", "c", client=c) == 401

    def boom(req):
        raise httpx.ConnectError("down")

    c2 = httpx.Client(transport=httpx.MockTransport(boom))
    with pytest.raises(RuntimeError):
        publish(SIG, 1.0, "http://api/internal/signals", "k1", b"k", "c", client=c2, sleep=lambda s: None)
