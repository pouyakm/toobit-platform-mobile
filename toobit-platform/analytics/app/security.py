"""HMAC authentication for the internal .NET <-> Python channel.

Canonical string: METHOD \\n PATH \\n TIMESTAMP \\n SHA256_HEX(body)
Headers: X-Key-Id, X-Timestamp (unix seconds), X-Signature (hex HMAC-SHA256).
Replay protection: timestamp skew window + in-memory nonce (signature) cache
(use Redis for multi-replica deployments).
"""
from __future__ import annotations

import hashlib
import hmac
import time
from collections import OrderedDict


class AuthError(Exception):
    pass


def canonical(method: str, path: str, timestamp: str, body: bytes) -> str:
    return "\n".join([method.upper(), path, timestamp, hashlib.sha256(body).hexdigest()])


def sign(key: bytes, method: str, path: str, timestamp: str, body: bytes) -> str:
    return hmac.new(key, canonical(method, path, timestamp, body).encode(), hashlib.sha256).hexdigest()


class HmacVerifier:
    def __init__(self, keys: dict[str, bytes], max_skew: int, cache_size: int = 10_000):
        self._keys, self._skew = keys, max_skew
        self._seen: OrderedDict[str, float] = OrderedDict()
        self._cache_size = cache_size

    def verify(self, key_id: str, timestamp: str, signature: str, method: str, path: str,
               body: bytes, now: float | None = None) -> None:
        now = time.time() if now is None else now
        key = self._keys.get(key_id)
        if key is None:
            raise AuthError("unknown key id")
        try:
            ts = float(timestamp)
        except ValueError as exc:
            raise AuthError("bad timestamp") from exc
        if abs(now - ts) > self._skew:
            raise AuthError("timestamp outside allowed skew")
        expected = sign(key, method, path, timestamp, body)
        if not hmac.compare_digest(expected, signature):
            raise AuthError("bad signature")
        if signature in self._seen:
            raise AuthError("replayed request")
        self._seen[signature] = now
        while len(self._seen) > self._cache_size:
            self._seen.popitem(last=False)
