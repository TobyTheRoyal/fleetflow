#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
if [ "$#" -ne 1 ] || [ "$1" != --confirm ]; then
  printf '%s\n' 'This deletes only the fleetflow development database and recreates fictional demo data.' 'Run: sh scripts/reset-demo.sh --confirm'
  exit 2
fi
sh scripts/setup-env.sh
docker compose -p fleetflow down --volumes
docker compose -p fleetflow up --build -d --wait
docker compose -p fleetflow run --rm --no-deps api --migrate --seed
sh scripts/demo-users.sh
