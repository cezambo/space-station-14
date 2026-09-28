# Jev wire format (verified)

**Requirements:** RM-02, RM-05, RM-07, RNF-05, RJ-16, RJ-19 · **Task:** T1.03
**Verified:** 2026-09-28 against the official docs at <https://docs.typesafe.ai> (index: `/llms.txt`) and two
live calls to `jev-1.13.0` (`dotnet run --project Cognition/src/Cognition.Eval -- jev-ping --live --max-cost-usd 0.05`).
**Implementation:** `Cognition/src/Cognition.Core/Providers/` (`JevWireFormat`, `JevHttpClient`, `JevRequestValidator`).

Sources used: `/api`, `/models`, `/primitives/*`, `/sdk/python/api/retries`, `/sdk/javascript/api/interfaces/Usage`,
`/cookbooks/parallel_questions`. Third-party sites that mirror or proxy the API (e.g. `jevtypesafeai.com`,
`thejevai.com`) were **not** used; one of them adds a `cost_usd` usage field that the official API does not return.

Legend: **[doc]** stated in the official docs · **[live]** observed on the live API · **[ours]** our choice.

## Endpoint

```http
POST https://api.typesafe.ai/v1/systemone
Authorization: Bearer <TYPESAFE_API_KEY>
Content-Type: application/json
```
[doc] `base_url` in `cognition.toml` is `https://api.typesafe.ai/v1`; the client appends `/systemone`.

## Request

```json
{
  "model": "jev-1.13.0",
  "state": "…text…",
  "questions": {
    "<id>": { "type": "choice", "instructions": "…", "criteria": { "<option>": "description or null", … } },
    "<id>": { "type": "score",  "instructions": "…", "criteria": ["lowest level", …, "highest level"] },
    "<id>": { "type": "noul",   "instructions": "…", "criteria": { "true": "…", "false": "…" } }
  }
}
```

| Field | Rule | Source |
|---|---|---|
| `model` | Required. We always send the pinned version, never an alias (`jev-latest`/`jev-preview` move). Versioned ids work even when `GET /v1/models` does not list them. | [doc] RM-02 |
| `state` | Required. String, object or array; we send a string. | [doc] |
| `questions` | Map id → question. **The id is not sent to the model** (RJ-16). Answers come back under the same ids. | [doc] |
| `instructions` | Required. String, object or array; we send a string. | [doc] |
| Choice `criteria` | Map option → description; `null` allowed for a bare label. Max **255** options. We send `null` for empty descriptions and require ≥2 options. | [doc] / [ours] min 2 |
| Score `criteria` | Ordered array, low → high, **2–10** levels. | [doc] |
| Noul `criteria` | Optional `{ "true": …, "false": … }`. We send both or neither. | [doc] / [ours] pairing |
| Size | 64k tokens per request (state + all questions); 32k for state + the single longest question. Checked locally with chars/`chars_per_token_initial`. | [doc] |

Local validation also rejects: empty model/state/instructions, zero questions, question ids or option keys that
collide case-insensitively. Invalid requests are never sent.

## Response

```json
{
  "model": "jev-1.13.0",
  "answers": {
    "<choice id>": { "type": "choice", "choice": "help", "confidence": 0.99,
                     "probabilities": { "nothing": 0.0, "help": 1.0, "sleep": 0.0, "eat": 0.0 } },
    "<score id>":  { "type": "score", "score": 2.29, "confidence": 0.7,
                     "legend": { "0": "…", "1": "…", "2": "…", "3": "…" },
                     "probabilities": { "0": 0.0, "1": 0.0, "2": 0.71, "3": 0.29 } },
    "<noul id>":   { "type": "noul", "noul": 0.94 }
  },
  "usage": { "input_tokens": 546, "output_tokens": 80 }
}
```
(Real response from the ping fixture, `Cognition/fixtures/jev/ping.json`.)

| Field | Meaning | Mapping in `Cognition.Core` | Source |
|---|---|---|---|
| `model` | Version that answered. | `JevResponse.Model` | [doc] [live] |
| Choice `choice` | Highest-probability option key. | `ChoiceAnswer.Choice`; must be one of the options sent | [doc] |
| Choice `probabilities` | Every option → probability, sums to 1. Key order is **not** the request order. | `ChoiceAnswer.Probabilities` | [doc] [live] |
| `confidence` | Choice and Score only, 0–1, derived from the distribution. Noul has **no** confidence. | `…Answer.Confidence` | [doc] |
| Score `score` | Probability-weighted, can fall between levels. Ordinal use only (RJ-19). | `ScoreAnswer.Score` | [doc] |
| Score `legend` | Level index (string, **0-based**) → description. | `ScoreAnswer.Legend` = description of the most probable level | [doc] [live] / [ours] |
| Score `probabilities` | Level index (string) → probability. | `ScoreAnswer.Probabilities[level]`, missing levels = 0 | [doc] |
| Noul `noul` | P(yes), 0–1. ~0.5 means uncertain, not "medium". | `NoulAnswer.PYes` | [doc] |
| `usage.input_tokens` / `output_tokens` | Token counts. **No USD field.** | `UsageInfo`; USD computed in code | [doc] [live] |

A 2xx body that does not answer every question asked, uses the wrong type, names an unknown option or level,
or lacks `usage` raises `JevProtocolException`.

## Headers observed on the live API

| Header | Use |
|---|---|
| `x-typesafe-request-id: req_…` | Request id → `JevResponse.RequestId` (not in the docs; falls back to `local-<guid>` if absent). [live] |
| `x-envoy-upstream-service-time` | Server-side time in ms (99 ms on the ping vs ~450–480 ms end to end from this machine). Informational. [live] |
| `CF-RAY`, `Server: cloudflare`, `Set-Cookie` | Cloudflare front. `Set-Cookie` is dropped from diagnostics. [live] |

## Pricing and limits (jev-1.13.0)

| Item | Value | Source |
|---|---|---|
| Price | **$0.042 per million input tokens; output tokens free.** Stored as `input_price_usd_per_mtok` / `output_price_usd_per_mtok`. | [doc] `/models` |
| Rate limits | 250,000 tokens/s and 1,200 requests/min per account; "adjusting dynamically", may change without notice. Over either → 429. | [doc] |
| Parallel questions | "Jev ingests the `state` once and evaluates every question against it in parallel." Measured in T1.03b. | [doc] |

## Errors and retries

| Status | Meaning | Client behaviour |
|---|---|---|
| 401 | Missing/invalid key | Fail, no retry |
| 422 | Body failed validation; body names the field | Fail, no retry |
| 400, other 4xx | Client error | Fail, no retry |
| 408 | Request timeout | Retry |
| 429 | Rate limit | Retry; honor `retry-after-ms`, then `Retry-After` (seconds or HTTP date), plus up to `backoff_jitter` on top |
| 529 | Overloaded | Retry (same as 429) |
| other 5xx | Server error | Retry with exponential backoff |

- Retry set and headers match the official SDK `RetryPolicy` defaults: statuses `{408, 429, 500–599}`,
  `respect_retry_after` for `Retry-After` and `retry-after-ms`, backoff 0.5 s doubling to 5 s, jitter 25%
  subtracted. [doc]
- Ours: at most `max_retries = 3` retries (RM-07; the SDK default is 2). Per-attempt timeout `timeout_ms`,
  timeouts and connection failures are retried. A `retry-after` above 30 s fails fast instead of stalling
  (RNF-05). Caller cancellation is never retried.
- Live runs go through `CostGuardedJevClient`, which refuses any call whose estimate (chars/4 × 1.5) would
  push the run over `--max-cost-usd` (RDev-04).
