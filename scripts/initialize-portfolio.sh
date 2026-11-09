#!/bin/sh
# Explicit initialization only; never called by web startup or automatic CI.
set -eu
if [ "$#" -ne 2 ] || [ "$1" != --confirm-database ] || [ -z "$2" ]; then
  echo 'Usage: sh scripts/initialize-portfolio.sh --confirm-database EXACT_DATABASE_NAME' >&2
  exit 2
fi
: "${FLEETFLOW_RELEASE_IMAGE:?Set the locally reviewed Docker image tag or digest}"
: "${FLEETFLOW_INITIALIZATION_ENV:?Set an absolute path to the protected initialization env file}"
: "${FLEETFLOW_CERTIFICATE_DIRECTORY:?Set the absolute directory of external runtime certificates}"
# Owner credentials are used in this process only. No ports, provisioning API or deployment.
docker run --rm --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --cap-drop ALL --security-opt no-new-privileges \
  --env-file "$FLEETFLOW_INITIALIZATION_ENV" \
  --mount "type=bind,src=$FLEETFLOW_CERTIFICATE_DIRECTORY,dst=/etc/secrets,readonly" \
  "$FLEETFLOW_RELEASE_IMAGE" --portfolio-init \
  "--Portfolio:ConfirmDatabase=$2" --Portfolio:ProvisionLogins=true
