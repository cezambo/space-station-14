<!-- fortnightly.md (heavy) -->
## SYSTEM
Compress fifteen days of a person's life into long-term memory and update their inner life.
Only propose lasting emotions for events with enduring significance. For existing lasting feelings, decide
whether the period reinforced, relieved or resolved them. Reasons must be timeless and at most 15 words.
Only propose a personality change for truly transformative experiences.

## USER
Profile: {{full_profile}}
Existing lasting feelings: {{modifiers}}
Likes: {{likes}} | Dislikes: {{dislikes}}
Goals — medium: {{medium_goals}}; long: {{long_goals}}
Previous long-term memories: {{fortnightly}}
The fifteen days:
{{daily_full_x15}}

## SCHEMA
{"type":"object","required":["summary","emotion_ops","likes_ops","goal_ops","personality_change"],"properties":{
"summary":{"type":"string","description":"2-3 paragraphs, first person, past tense"},
"emotion_ops":{"type":"array","items":{"type":"object","required":["op","justification"],"properties":{
"op":{"enum":["create","adjust","remove"]},"modifier_id":{"type":"string"},
"emotion":{"enum":["joy","trust","fear","surprise","sadness","disgust","anger","anticipation"]},
"intensity":{"enum":["faint","mild","moderate","strong","overwhelming"]},
"duration":{"enum":["days","about a week","a few weeks","about a month","about two months"]},
"reason":{"type":"string"},"justification":{"type":"string"}}}},
"likes_ops":{"type":"array","items":{"type":"object","required":["op","kind","text"],"properties":{
"op":{"enum":["add","modify","remove"]},"kind":{"enum":["like","dislike"]},"text":{"type":"string"},
"new_text":{"type":"string"},"strength":{"enum":["mild","moderate","strong"]}}}},
"goal_ops":{"type":"array","items":{"type":"object","required":["horizon","op"],"properties":{
"horizon":{"enum":["medium","long"]},"op":{"enum":["keep","modify","remove","add"]},
"goal_id":{"type":"string"},"text":{"type":"string"}}}},
"personality_change":{"type":"object","required":["proposed"],"properties":{
"proposed":{"type":"boolean"},"description":{"type":"string"}}}}}
