"""Posts signals to the .NET backend over the HMAC-authenticated internal endpoint.

Python never talks to Toobit: its ONLY outbound call is to BACKEND_SIGNAL_URL. The correlation id received from
.NET is forwarded so one id spans .NET -> Python -> .NET. Retries only on connection errors / 5XX with jittered
backoff; ingest is idempotent per (symbol, correlation id) on the .NET side for shadow-mode logging.
"""
from __future__ import annotations

import json
import random
import time
from typing import Callable
from urllib.parse import urlparse

import httpx

from app.security import sign


def build_signal_body(signal: dict, reference_price: float) -> bytes:
    payload = {
        "symbol": signal["symbol"], "action": signal["action"], "confidence": signal["confidence"],
        "referencePrice": reference_price, "modelVersion": signal.get("modelVersion"),
        "features": signal["features"], "explain": signal["explain"],
    }
    return json.dumps(payload, separators=(",", ":")).encode()


def signed_headers(key_id: str, key: bytes, url: str, body: bytes, correlation_id: str, now: float | None = None) -> dict:
    ts = str(int(now if now is not None else time.time()))
    path = urlparse(url).path
    return {"Content-Type": "application/json", "X-Key-Id": key_id, "X-Timestamp": ts,
            "X-Signature": sign(key, "POST", path, ts, body), "X-Correlation-Id": correlation_id}


def publish(signal: dict, reference_price: float, url: str, key_id: str, key: bytes, correlation_id: str,
            client: httpx.Client | None = None, max_attempts: int = 3,
            sleep: Callable[[float], None] = time.sleep, rng: Callable[[], float] = random.random) -> int:
    body = build_signal_body(signal, reference_price)
    own = client is None
    client = client or httpx.Client(timeout=5.0)
    try:
        for attempt in range(1, max_attempts + 1):
            headers = signed_headers(key_id, key, url, body, correlation_id)   # fresh timestamp/signature per attempt
            try:
                r = client.post(url, content=body, headers=headers)
                if r.status_code < 500:
                    return r.status_code
            except httpx.TransportError:
                pass
            if attempt < max_attempts:
                sleep(min(5.0, 0.25 * 2 ** (attempt - 1)) * (0.5 + 0.5 * rng()))
        raise RuntimeError("backend unreachable after retries")
    finally:
        if own:
            client.close()
