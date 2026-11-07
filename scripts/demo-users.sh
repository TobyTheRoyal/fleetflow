#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
sh scripts/setup-env.sh
if [ ! -e .env.demo ]; then
  (umask 077; set -C
    for role in ADMIN DISPATCHER VIEWER; do
      printf 'DEMO_%s_PASSWORD=Ff!7' "$role"
      openssl rand -hex 24
    done > .env.demo)
fi
docker compose -p fleetflow --env-file .env --env-file .env.demo run --rm --no-deps api --migrate --seed-users
printf '%s\n' 'Demo accounts provisioned. Read your generated passwords locally in the ignored .env.demo file.' 'Emails: admin@fleetflow.example, dispatcher@fleetflow.example, viewer@fleetflow.example.' 'Existing accounts and passwords were not reset.'
