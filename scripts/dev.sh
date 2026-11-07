#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
sh scripts/setup-env.sh
docker compose -p fleetflow up --build -d --wait
docker compose -p fleetflow run --rm --no-deps api --migrate --seed
npm ci --prefix frontend
cd frontend
exec npm run dev
