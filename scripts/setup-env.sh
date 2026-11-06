#!/bin/sh
set -eu

cd "$(dirname "$0")/.."
if [ -e .env ]; then
  printf '%s\n' '.env already exists; leaving it unchanged.'
  exit 0
fi

umask 077
set -C
{
  printf 'POSTGRES_PASSWORD='
  openssl rand -hex 24
  printf '%s\n' 'POSTGRES_DB=fleetflow' 'POSTGRES_USER=fleetflow' 'POSTGRES_PORT=5433' 'API_PORT=5080'
} > .env
printf '%s\n' 'Created ignored .env with a generated local database password.'
