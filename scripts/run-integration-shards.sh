#!/usr/bin/env bash
# Runs Content.IntegrationTests in shards. Upstream's PoolManagerTestEventHandler kills the whole
# run after 20 minutes, which a full run exceeds on slower machines (see docs/reports/phase0-baseline.md).
# Progress is written to docs/reports/raw/integration-shards/ and shown by scripts/test-dashboard.py.
# Usage: scripts/run-integration-shards.sh [shard ...]     (default: all shards)
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
OUT="$ROOT/docs/reports/raw/integration-shards"
EVENTS="$OUT/events.jsonl"
mkdir -p "$OUT"
NS=Content.IntegrationTests.Tests

prefixes() { local out="" l; for l in "$@"; do out+="${out:+|}FullyQualifiedName~$NS.$l"; done; echo "$out"; }
NOT_BIG="FullyQualifiedName!~$NS.EntityTest.&FullyQualifiedName!~$NS.GameRules."
# "rest" is split by the first letter of the namespace/class under Content.IntegrationTests.Tests.
declare -A FILTERS=(
  [entitytest]="FullyQualifiedName~$NS.EntityTest."
  [gamerules]="FullyQualifiedName~$NS.GameRules."
  [rest-ag]="($(prefixes A B C D E F G))&$NOT_BIG"
  [rest-hz]="($(prefixes H I J K L M N O P Q R S T U V W X Y Z))&$NOT_BIG"
)
ORDER=(entitytest gamerules rest-ag rest-hz)
SHARDS=("$@"); [[ ${#SHARDS[@]} -eq 0 ]] && SHARDS=("${ORDER[@]}")

event() { printf '{"t":%s,"event":"%s","shard":"%s","extra":"%s"}\n' "$(date +%s)" "$1" "$2" "${3:-}" >>"$EVENTS"; }

: >"$EVENTS"
event run_start "" "$(IFS=,; echo "${SHARDS[*]}")"
python3 "$ROOT/scripts/test-dashboard.py" --watch "$ROOT/TESTES-PROGRESSO.md" &
WATCHER=$!
trap 'sleep 1; kill $WATCHER 2>/dev/null; python3 "$ROOT/scripts/test-dashboard.py" --watch "$ROOT/TESTES-PROGRESSO.md"' EXIT
event build_start build
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c Release -v q -nologo >"$OUT/build.log" 2>&1 \
  || { event build_failed build; echo "build failed, see $OUT/build.log"; exit 1; }
event build_end build

status=0
for name in "${SHARDS[@]}"; do
  filter=${FILTERS[$name]:?unknown shard $name}
  echo "=== shard $name: $filter"
  event shard_start "$name"
  dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-build \
    --filter "$filter" --logger "console;verbosity=normal" --logger "trx;LogFileName=$name.trx" \
    --results-directory "$OUT" >"$OUT/$name.log" 2>&1
  rc=$?
  [[ $rc -ne 0 ]] && status=1
  summary=$(grep -aE '^(Test Run (Successful|Failed|Aborted)|Total tests:|\s+(Passed|Failed|Skipped):)' "$OUT/$name.log" | tr -s ' ' | paste -sd' ' -)
  echo "${summary:-  (no summary line, see $OUT/$name.log)}"
  if grep -aq 'Tests are taking too long' "$OUT/$name.log"; then
    echo "  WARNING: shard hit the 20-minute pool shutdown; split it further"
    event shard_timeout "$name"
  fi
  event shard_end "$name" "rc=$rc"
done
event run_end "" "status=$status"
exit $status
