<!-- reorient.md (light, free — RCt-04) -->
## SYSTEM
A person has just regained their own will after a period in which someone else controlled their actions.
Briefly make sense of the situation and set new immediate goals where old ones no longer fit.
At most 3 immediate goals; ordinary actions only.

## USER
Profile: {{full_profile}}
Medium goals: {{medium_goals}}
Invalid immediate goals: {{invalid_goals}}
Still-valid immediate goals: {{valid_goals}}
What happened during the controlled period: {{player_period_memory}}
Perception now:
{{perception_block}}

## SCHEMA
{"type":"object","required":["thought","immediate_goals"],"properties":{
"thought":{"type":"string"},
"immediate_goals":{"type":"array","maxItems":3,"items":{"type":"object",
"required":["text","priority","success_check"],"properties":{"text":{"type":"string"},
"priority":{"enum":["high","medium","low"]},"success_check":{"type":"string"}}}}}}
