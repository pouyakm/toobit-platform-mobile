# syntax=docker/dockerfile:1.7
# Pin to a digest in CI (see cd.yml "verify digests"): python:3.12-slim@sha256:<digest>
ARG PY_IMAGE=python:3.12-slim
FROM ${PY_IMAGE} AS build
WORKDIR /w
COPY requirements.txt .
RUN pip install --no-cache-dir --prefix=/install -r requirements.txt

FROM ${PY_IMAGE} AS runtime
RUN useradd --system --uid 10001 --no-create-home app
WORKDIR /srv
COPY --from=build /install /usr/local
COPY app app
COPY analytics analytics
COPY signals signals
COPY ml ml
COPY backtesting backtesting
USER 10001
ENV PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1
EXPOSE 8000
CMD ["uvicorn", "app.main:app_factory", "--factory", "--host", "0.0.0.0", "--port", "8000"]
