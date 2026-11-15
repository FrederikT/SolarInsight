#!/usr/bin/env bash
# Restores a decrypted backup folder into a throw-away database container and prints verify.sql output.
# Usage: restore-check.sh <folder with solar.dump and SHA256SUMS>. Never touches the running stack.
set -euo pipefail
src=${1:?folder with solar.dump}
here=$(cd "$(dirname "$0")" && pwd)
(cd "$src" && sha256sum -c SHA256SUMS)
name=solar-restore-check-$$
pw=$(head -c 24 /dev/urandom | base64)
docker run -d --rm --name "$name" --network none -e POSTGRES_USER=solar -e POSTGRES_DB=solar \
  -e POSTGRES_PASSWORD="$pw" -e POSTGRES_INITDB_ARGS="--encoding=UTF8 --locale=C" postgres:17.11-bookworm > /dev/null
trap 'docker stop "$name" > /dev/null' EXIT
until docker exec "$name" pg_isready -U solar -d solar > /dev/null 2>&1; do sleep 1; done
docker exec -i "$name" pg_restore --exit-on-error --no-owner --role=solar -U solar -d solar < "$src/solar.dump"
docker exec -i "$name" psql -X -U solar -d solar -v ON_ERROR_STOP=1 < "$here/verify.sql"
