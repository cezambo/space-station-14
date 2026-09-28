# CI on the fork

Only the `Cognition` workflow (`.github/workflows/cognition.yml`, RDev-04) runs on `cezambo/space-station-14`.

The 23 upstream SS14 workflows were **disabled in GitHub Actions settings** on 2026-09-28 (owner request) to
save Actions minutes. Their files under `.github/workflows/` are unchanged, so upstream merges do not conflict.

- List: `gh workflow list --repo cezambo/space-station-14 --all`
- Re-enable one: `gh workflow enable "<name>" --repo cezambo/space-station-14`
- Re-enable all: `gh workflow list --repo cezambo/space-station-14 --all --json id -q '.[].id' | xargs -n1 gh workflow enable --repo cezambo/space-station-14`

A workflow added later by an upstream merge starts **enabled**; disable it the same way if it is not needed.
SS14 build and tests are run locally instead (`docs/reports/phase0-baseline.md`).
