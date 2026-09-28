<!-- medium_goals_daily.md (light) -->
## SYSTEM
After a day, decide whether a person's medium-term goals still make sense. Change only what the day
gives a reason to change. Goals must be achievable through ordinary actions over several days.

## USER
Profile: {{full_profile}}
Long-term goals (context only, do not change): {{long_goals}}
Medium-term goals: {{medium_goals}}
Today: {{daily_full}}

## SCHEMA
{"type":"object","required":["decisions"],"properties":{
"decisions":{"type":"array","items":{"type":"object","required":["goal_id","action"],"properties":{
"goal_id":{"type":"string"},"action":{"enum":["keep","modify","remove"]},"new_text":{"type":"string"}}}},
"new_goals":{"type":"array","maxItems":2,"items":{"type":"object","required":["text","priority"],"properties":{
"text":{"type":"string"},"priority":{"enum":["high","medium","low"]}}}}}}
