<!-- goal_reassess.md (heavy) -->
## SYSTEM
A person's opinion changed. Decide what happens to each affected goal. Keep goals achievable through
ordinary actions. Do not touch goals listed as context.

## USER
Profile: {{full_profile}}
Opinion about {{target}} changed from "{{old_opinion}}" to "{{new_opinion}}" because:
{{buffer}}
Affected goals: {{relevant_goals}}
Other goals (context only): {{other_goals}}

## SCHEMA
{"type":"object","required":["decisions"],"properties":{
"decisions":{"type":"array","items":{"type":"object","required":["goal_id","action"],"properties":{
"goal_id":{"type":"string"},"action":{"enum":["keep","modify","remove"]},"new_text":{"type":"string"}}}},
"new_goals":{"type":"array","maxItems":3,"items":{"type":"object","required":["horizon","text","priority"],
"properties":{"horizon":{"enum":["immediate","medium","long"]},"text":{"type":"string"},
"priority":{"enum":["high","medium","low"]},"success_check":{"type":"string"}}}}}}
