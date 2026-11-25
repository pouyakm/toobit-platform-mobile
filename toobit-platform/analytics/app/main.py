"""FastAPI analytics service. Never calls Toobit; receives candles from the .NET backend."""
from __future__ import annotations

import asyncio
import logging
import os
import uuid
from contextvars import ContextVar
from typing import Optional

import pandas as pd
from fastapi import Depends, FastAPI, HTTPException, Query, Request
from pydantic import BaseModel, Field, field_validator

from app.publisher import publish
from app.security import AuthError, HmacVerifier
from ml.registry import OnnxScorer, load_active
from signals.config import ConfigError, SignalConfig
from signals.engine import analyze

correlation_id: ContextVar[str] = ContextVar("correlation_id", default="-")
log = logging.getLogger("analytics")


class CandleIn(BaseModel):
    open: float
    high: float
    low: float
    close: float
    volume: float = 0.0
    ts: Optional[int] = None


class AnalyzeRequest(BaseModel):
    symbol: str = Field(pattern=r"^[A-Z0-9]+-SWAP-USDT$")
    candles: list[CandleIn] = Field(min_length=2, max_length=5000)
    in_position: bool = False

    @field_validator("candles")
    @classmethod
    def _sane(cls, v: list[CandleIn]) -> list[CandleIn]:
        for c in v:
            if c.low <= 0 or c.high < c.low:
                raise ValueError("invalid candle: require 0 < low <= high")
        return v


def create_app(cfg: SignalConfig | None = None, verifier: HmacVerifier | None = None,
               scorer: OnnxScorer | None = None) -> FastAPI:
    app = FastAPI(title="Toobit Analytics", version="1.0.0")
    cfg = cfg or SignalConfig.from_env()  # fail-fast on missing thresholds
    if verifier is None:
        key_id = os.environ.get("INTERNAL_HMAC_KEY_ID", "")
        key = os.environ.get("INTERNAL_HMAC_KEY", "")
        if not key_id or not key:
            raise ConfigError("INTERNAL_HMAC_KEY_ID and INTERNAL_HMAC_KEY are required")
        verifier = HmacVerifier({key_id: key.encode()}, int(os.environ.get("INTERNAL_HMAC_MAX_SKEW_SECONDS", "60")))
    if scorer is None:
        scorer = load_active(os.environ.get("MODEL_REGISTRY_PATH", "/models"), os.environ.get("ACTIVE_MODEL") or None)
    app.state.cfg, app.state.scorer = cfg, scorer

    @app.middleware("http")
    async def _correlation(request: Request, call_next):
        cid = request.headers.get("X-Correlation-Id") or str(uuid.uuid4())
        token = correlation_id.set(cid)
        try:
            response = await call_next(request)
        finally:
            correlation_id.reset(token)
        response.headers["X-Correlation-Id"] = cid
        return response

    async def require_auth(request: Request) -> None:
        body = await request.body()
        try:
            verifier.verify(
                request.headers.get("X-Key-Id", ""), request.headers.get("X-Timestamp", ""),
                request.headers.get("X-Signature", ""), request.method, request.url.path, body,
            )
        except AuthError as exc:
            # Message is generic on purpose; never echo key material.
            log.warning("internal auth rejected: %s", exc)
            raise HTTPException(status_code=401, detail="unauthorized") from exc

    def _run(req: AnalyzeRequest) -> dict:
        df = pd.DataFrame([c.model_dump() for c in req.candles])
        try:
            sig = analyze(req.symbol, df, cfg, req.in_position)
        except ValueError as exc:
            raise HTTPException(status_code=422, detail=str(exc)) from exc
        if scorer is not None:
            sig.model_version = scorer.entry.version
            sig.explain["model_score"] = scorer.score(sig.features)
        out = sig.to_dict()
        out["correlationId"] = correlation_id.get()
        return out

    @app.get("/health")
    async def health() -> dict:
        return {"status": "ok", "model": scorer.entry.version if scorer else None}

    @app.post("/analyze", dependencies=[Depends(require_auth)])
    async def analyze_ep(req: AnalyzeRequest) -> dict:
        return _run(req)

    @app.post("/signal/{symbol}", dependencies=[Depends(require_auth)])
    async def signal_ep(symbol: str, req: AnalyzeRequest, publish_to_backend: bool = Query(False, alias="publish")) -> dict:
        if req.symbol != symbol:
            raise HTTPException(status_code=422, detail="symbol mismatch")
        out = _run(req)
        if publish_to_backend:
            # Python -> .NET over the HMAC-guarded internal endpoint (the ONLY outbound call this service makes).
            url = os.environ.get("BACKEND_SIGNAL_URL", "")
            if not url:
                raise HTTPException(status_code=503, detail="BACKEND_SIGNAL_URL not configured")
            try:
                status = await asyncio.to_thread(
                    publish, out, req.candles[-1].close, url, os.environ["INTERNAL_HMAC_KEY_ID"],
                    os.environ["INTERNAL_HMAC_KEY"].encode(), out["correlationId"])
            except RuntimeError as exc:
                raise HTTPException(status_code=502, detail="backend unreachable") from exc
            out["published"] = status
        return out

    return app


def app_factory() -> FastAPI:  # uvicorn --factory app.main:app_factory
    return create_app()
