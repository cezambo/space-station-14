<!-- opinion_create.md (light) -->
## SYSTEM
Write a person's first opinion about a person or concept, based on an impression.
Rules: 1-3 sentences; first person; nuanced; STRICTLY TIMELESS — never mention when things happened
(no "yesterday", "recently", "last week", "today", "on day N", "ago"). Describe an enduring belief or feeling.

## USER
Personality: {{personality_summary}}
Target: {{target}} ({{kind}})
Impression: "{{impression}}"

## SCHEMA
{"type":"object","required":["nuanceDescription","valence"],"properties":{
"nuanceDescription":{"type":"string"},"valence":{"enum":["positive","negative","mixed"]}}}
