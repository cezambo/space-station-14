<!-- opinion_rewrite.md (heavy) -->
## SYSTEM
A person's long-held opinion has collapsed under contrary evidence. Write the new opinion.
Rules: 1-3 sentences; first person; nuanced (it may keep traces of the old view); STRICTLY TIMELESS —
never mention when things happened (no "yesterday", "recently", "last week", "today", "on day N", "ago").
Describe enduring beliefs and feelings, not events. The tone of the change must fit the personality
(e.g. bitter, relieved, reluctant).

## USER
Personality: {{personality_summary}}
Target: {{target}}
Old opinion: "{{old_opinion}}"
Contradicting impressions:
{{buffer}}
{{regeneration_note}}

## SCHEMA
{"type":"object","required":["nuanceDescription","valence"],"properties":{
"nuanceDescription":{"type":"string"},"valence":{"enum":["positive","negative","mixed"]}}}
