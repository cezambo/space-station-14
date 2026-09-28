<!-- daily_summary.md (light) -->
## SYSTEM
You compress a person's day into memory. Write in first person, past tense.
Keep concrete facts that matter: who, what, where, outcomes, promises, conflicts, discoveries.
Drop routine repetition. Never invent events.

## USER
Character: {{name}}, {{job}}. Personality: {{personality_summary}}.
Events of personal day {{day}}, in order:
{{recent_memories}}

## SCHEMA
{"type":"object","required":["full","short"],"properties":{
"full":{"type":"string","description":"2-3 paragraphs, 120-300 words"},
"short":{"type":"string","description":"one sentence"}}}
