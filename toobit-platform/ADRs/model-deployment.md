# ADR-0004: Model deployment approach

Status: Accepted (default: inference in Python service)

## Context
Options considered: ONNX in-process in .NET, or inference in the Python service (FastAPI or gRPC sidecar).

## Decision
- MVP: inference runs inside the Python FastAPI service. It keeps signal logic isolated from execution and lets analytics iterate without redeploying the backend.
- Artifacts are exported to **ONNX** and loaded with ONNX Runtime, so in-process .NET hosting is a drop-in later.
- Versioning: `model_name@semver+sha256[:8]`; stored in a local-FS registry (`MODEL_REGISTRY_PATH`) for MVP, S3-compatible in prod. Each version has a `ModelRegistryEntry` row in Postgres. The active version is chosen by a feature flag.
- Fixed inference contract: `features: float[N]` ordered by the model's `feature_names`, output `score` in [0,1] plus top-k contributing features.
- Drift: PSI/KS of live feature distributions vs. the training snapshot, warn above configured thresholds.
- Shadow mode: signals are generated, stored and shown even when automated trading is off.

## Consequences
One extra network hop per signal, covered by the latency SLO (p95 < 2s). mTLS/HMAC protects the hop. Move to gRPC or in-process ONNX if latency becomes binding.
