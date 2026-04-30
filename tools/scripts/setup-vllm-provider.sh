#!/usr/bin/env bash
# =============================================================================
# setup-vllm-provider.sh — register the local vLLM container as an
# OpenClaw provider via the config.patch RPC, and set Seren's default
# model accordingly.
#
# Idempotent: re-running just refreshes the values. Safe to call after
# every fresh `docker compose up -d` or after rebuilding the vllm image.
#
# Prereqs:
#   - vllm container healthy (curl http://vllm:8000/v1/models OK)
#   - openclaw container healthy
#   - OPENCLAW_GATEWAY_TOKEN exported (or sourced from .env)
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

if [[ -f .env ]]; then
  # shellcheck disable=SC1091
  source <(grep -E '^(OPENCLAW_GATEWAY_TOKEN|OPENCLAW_AUTH_TOKEN)=' .env | sed 's/^/export /')
fi

TOKEN="${OPENCLAW_GATEWAY_TOKEN:-${OPENCLAW_AUTH_TOKEN:-}}"
if [[ -z "$TOKEN" ]]; then
  echo "ERROR: set OPENCLAW_GATEWAY_TOKEN in .env or as env var." >&2
  exit 1
fi

OC="${OPENCLAW_URL:-http://localhost:18789}"
SEREN="${SEREN_API_URL:-http://localhost:5080}"
MODEL_ID="${MODEL_ID:-Qwen3.6-27B-AWQ-INT4}"

# ---------------------------------------------------------------------------
# 1. Wait for vLLM to be ready (cold-load can take 2-4 min on a 27B AWQ).
# ---------------------------------------------------------------------------
echo "[1/3] Waiting for vLLM to advertise the model..."
for i in $(seq 1 60); do
  if curl -fsS "${OC}/health" > /dev/null 2>&1 \
     && docker compose exec -T vllm curl -fsS http://localhost:8000/v1/models 2>/dev/null \
        | grep -q "$MODEL_ID"; then
    echo "  vLLM ready (attempt $i)."
    break
  fi
  sleep 5
done

# ---------------------------------------------------------------------------
# 2. Register `vllm` provider in OpenClaw via config.patch RPC.
#    JSON Merge Patch (RFC 7396) — keys present overwrite, null deletes,
#    omitted keys stay untouched. Removing legacy `ollama` provider with
#    null is safe; OpenClaw drops it from its registry on next reload.
# ---------------------------------------------------------------------------
echo "[2/3] Registering vllm provider in OpenClaw config..."
PATCH_BODY=$(cat <<JSON
{
  "name": "config.patch",
  "arguments": {
    "patch": {
      "models": {
        "providers": {
          "vllm": {
            "baseUrl": "http://vllm:8000/v1",
            "api": "openai",
            "models": ["${MODEL_ID}"]
          },
          "ollama": null
        }
      }
    }
  }
}
JSON
)

curl -fsS -X POST "${OC}/tools/invoke" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d "$PATCH_BODY" | head -c 400
echo

# ---------------------------------------------------------------------------
# 3. Set Seren's default model — goes through the same config.patch RPC
#    via Seren's /api/models/apply wrapper.
# ---------------------------------------------------------------------------
echo "[3/3] Pinning Seren default to vllm/${MODEL_ID}..."
curl -fsS -X POST "${SEREN}/api/models/apply" \
  -H "Content-Type: application/json" \
  -d "{\"model\":\"vllm/${MODEL_ID}\"}" -w "\n  HTTP %{http_code}\n"

echo
echo "Done. Test with:"
echo "  curl -fsS http://localhost:18789/v1/chat/completions \\"
echo "    -H 'Authorization: Bearer \${OPENCLAW_GATEWAY_TOKEN}' \\"
echo "    -H 'Content-Type: application/json' \\"
echo "    -d '{\"model\":\"vllm/${MODEL_ID}\",\"messages\":[{\"role\":\"user\",\"content\":\"Salut\"}],\"stream\":false}'"
