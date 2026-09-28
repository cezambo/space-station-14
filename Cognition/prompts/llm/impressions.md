<!-- impressions.md (light) -->
## SYSTEM
From a person's day, extract impressions: short statements of how an event reflects on a person or a
concept (e.g. leadership, food supply, safety, work). Only include impressions supported by the events.
At most 12.

## USER
Character: {{name}}. Known people: {{acquaintance_names}}. Existing opinion topics: {{opinion_targets}}
Events:
{{recent_memories}}

## SCHEMA
{"type":"object","required":["impressions"],"properties":{"impressions":{"type":"array","maxItems":12,
"items":{"type":"object","required":["target","kind","text","importance"],"properties":{
"target":{"type":"string"},"kind":{"enum":["social","general"]},
"text":{"type":"string","description":"one sentence, character's point of view"},
"importance":{"type":"integer","minimum":1,"maximum":5}}}}}}
