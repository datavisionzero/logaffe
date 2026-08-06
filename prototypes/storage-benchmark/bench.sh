#!/usr/bin/env bash
# PROTOTYPE — throwaway. One command per measurement stage.
set -euo pipefail
cd "$(dirname "$0")"

STAGE="${1:-smoke}"
INDEXES="${2:-full}"

if ! docker compose ps --status running --quiet db >/dev/null 2>&1; then
  echo "starting the prototype database ..."
  docker compose up -d --wait
fi

exec dotnet run --project Bench --configuration Release -- stage --stage "$STAGE" --indexes "$INDEXES"
