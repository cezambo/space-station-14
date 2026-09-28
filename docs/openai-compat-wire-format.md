# OpenAI-compatible LLM wire format (T1.04)

Client: `Cognition/src/Cognition.Core/Providers/OpenAiCompatClient.cs` (RM-03, RM-04).
Sources: **[doc]** official OpenRouter docs (`openrouter.ai/docs/llms.txt` index, pages fetched 2026-09-28 into
`docs/reports/raw/openrouter-docs/`, gitignored); **[live]** `llm-ping` on 2026-09-28 (4 calls, $0.0057 total,
dumps in `docs/reports/raw/llm-ping/`); **[ours]** a choice of this client.

## Request: `POST {base_url}/chat/completions`

| Field | Value | Source |
|---|---|---|
| `Authorization` | `Bearer <key>`; omitted when `api_key_env = ""` (local servers) | [doc] / [ours] |
| `model` | role's pinned `model` from `cognition.toml` | [doc] |
| `messages` | `system` (if non-empty) then `user`; the repair round appends `assistant` (bad reply) + `user` (errors) | [doc] / [ours] |
| `max_completion_tokens` | template output limit + `reasoning_allowance_tokens` (reasoning counts as output) | [doc] |
| `reasoning_effort` | role's value; omitted when empty. Enum: `max xhigh high medium low minimal none` | [doc] |
| `response_format` | schema requests only: `{"type":"json_schema","json_schema":{"name","strict":false,"schema"}}`, or `{"type":"json_object"}`, or nothing, per `structured_output` | [doc] / [ours] |
| `provider.require_parameters` | `true` when configured: route only to providers that support every sent parameter | [doc] |
| `stream` | `false` | [doc] |

`reasoning_effort` is the documented shorthand for `reasoning.effort`; it is also the OpenAI field name, so
the same body works with non-OpenRouter servers. `strict` is `false` because the spec schemas have optional
properties; local validation (`JsonSchemaLite`) is the authority in every mode. `json_schema.name` is the
purpose tag reduced to `[a-zA-Z0-9_-]{1,64}`. Serialization is deterministic (replay key, T1.05).

Model facts from `GET /api/v1/models` [live]: `z-ai/glm-5.3-flash` $0.15 / $0.50 per Mtok in/out,
`z-ai/glm-5.3` $1.40 / $4.40; both: `supported_efforts = [max, high, low]`, `mandatory = true` (reasoning
cannot be disabled), `default_effort = max`, `structured_outputs` and `response_format` supported.

## Response (200)

| Field | Use | Source |
|---|---|---|
| `id` | request id (`gen-…`); header `X-Generation-Id` carries the same | [doc] [live] |
| `model` | model that answered | [doc] |
| `provider` | upstream provider name (varies per call, see findings) | [live] |
| `choices[0].message.content` | reply text; also `reasoning`, `reasoning_details`, `refusal` present | [doc] [live] |
| `choices[0].finish_reason` | `stop`, `length`, `tool_calls`, `content_filter`, `error`, null | [doc] |
| `usage.prompt_tokens` / `completion_tokens` | tokens; completion includes reasoning | [doc] [live] |
| `usage.completion_tokens_details.reasoning_tokens` | reasoning share | [doc] [live] |
| `usage.cost` | USD (credits) for the call, returned without opting in; used as is | [doc] [live] |

When `usage.cost` is absent (local servers), USD = tokens × configured prices, flagged `CostReported = false`.
`finish_reason = length` → `LlmProtocolException` (not retried; raise the limit instead). Empty content →
`LlmProtocolException`. Both still report the billed `Usage` to the cost guard.

## Errors and retries

Error body: `{"error":{"code":<int>,"message":"…","metadata":{…}}}` [doc]. HTTP status equals `error.code`
unless the model already started, in which case the status is 200 with the error in the body [doc].

| Case | Behaviour | Source |
|---|---|---|
| 408, 429, 5xx (status or 200-body `error.code`) | retry up to `max_retries`, exponential backoff with jitter | [doc] / [ours] |
| `Retry-After` / `retry-after-ms` | honored (+ jitter); a wait over 60 s fails fast | [doc] / [ours] |
| `finish_reason = error` | retried like a 502 | [ours] |
| 400, 401, 402, 403 | fail at once. 402 = out of credits: the owner tops up manually | [doc] |
| timeout (`timeout_ms` per attempt), connection failure | retried, then `LlmTimeoutException` / `LlmTransportException` | [ours] |

## Schema validation and repair

Templates carry a JSON Schema (`## SCHEMA`). `JsonSchemaLite` supports `type properties required
additionalProperties(bool) items minItems maxItems enum minimum maximum minLength maxLength` (+ `description
title $schema` as annotations); any other keyword is rejected when the schema is loaded. One surrounding
Markdown code fence is stripped. On a validation failure, one repair round sends the errors back; its text
comes from a prompt template (P7), so the client takes it as `RepairMessage`. Second failure →
`LlmSchemaException`. Both rounds are billed and summed.

## Findings from the live run (2026-09-28)

1. **Schema descriptions do not reliably reach the model.** The light schema call had fewer input tokens
   than the plain call, and the reply was structurally valid but empty of meaning (`"full": "Maya's day"`).
   The schema acts as a grammar constraint only. **Templates must describe every field in the prompt text**
   (length, person, tense); the schema only enforces shape. Applies to T1.07 template reconstruction.
2. **Provider routing varies per call** (Wafer, DigitalOcean, Crusoe for the same model). Quality and
   reasoning behaviour may differ between providers; one light call reported 0 reasoning tokens despite
   `mandatory = true`. Pinning `provider.order` is a candidate knob for E-xx if quality varies.
3. Latency: light 1.5–4.5 s; heavy (`max` effort) 2.5–4.4 s with up to 970 reasoning tokens on a short task.
   Heavy cost is dominated by reasoning output ($4.40/Mtok).
