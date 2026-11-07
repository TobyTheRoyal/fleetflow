#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
# Docker Desktop on macOS may require its active context rather than /var/run/docker.sock.
if [ "$(uname -s)" = Darwin ] && [ -z "${DOCKER_HOST:-}" ]; then
  DOCKER_HOST=$(docker context inspect --format '{{.Endpoints.docker.Host}}')
  export DOCKER_HOST
  export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
fi
dotnet test --solution backend/FleetFlow.slnx -c Release --no-build --no-restore \
  --coverlet --coverlet-include '[FleetFlow.*]*' --coverlet-exclude '[FleetFlow.*Tests]*' \
  --coverlet-exclude-by-file '**/Migrations/*.cs' --coverlet-output-format cobertura \
  --results-directory artifacts/backend --results-directory-layout per-module --report-xunit-trx
