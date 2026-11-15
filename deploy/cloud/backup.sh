#!/usr/bin/env bash
# Nightly backup of the cloud instance: database dump plus state and key volumes, encrypted with age.
# The age private key never lives on the VM. Optional offsite copy via rclone (RCLONE_REMOTE, e.g. "oci:solar-backups").
set -euo pipefail
cd "$(dirname "$0")/../.."
BACKUP_DIR=${BACKUP_DIR:-/opt/solar/backups}
RECIPIENTS=${AGE_RECIPIENTS:-/opt/solar/backup/age-recipients.txt}
KEEP_DAYS=${KEEP_DAYS:-7}
files=(-f compose.yaml -f compose.owner.yaml -f compose.sungrow.yaml -f deploy/cloud/compose.cloud.yaml)
compose() { docker compose --env-file deploy/cloud/cloud.env "${files[@]}" "$@"; }

[ -s "$RECIPIENTS" ] || { echo "age recipients file missing: $RECIPIENTS" >&2; exit 1; }
stamp=$(date -u +%Y%m%dT%H%M%SZ)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
umask 077

# exec (not run): the app holds a fixed IP, so a second app container cannot start while it runs.
compose exec -T db pg_dump -U solar -d solar -Fc > "$work/solar.dump"
compose exec -T db pg_restore -l < "$work/solar.dump" | grep -q 'TABLE DATA public Aggregates' \
  || { echo "dump inventory lacks Aggregates" >&2; exit 1; }
compose exec -T app tar -czf - -C /state . > "$work/state.tar.gz"
compose exec -T app tar -czf - -C /keys . > "$work/keys.tar.gz"
git rev-parse HEAD > "$work/commit.txt"
(cd "$work" && sha256sum solar.dump state.tar.gz keys.tar.gz commit.txt > SHA256SUMS)

mkdir -p "$BACKUP_DIR"
target="$BACKUP_DIR/solar-$stamp.tar.age"
tar -cf - -C "$work" . | age -R "$RECIPIENTS" -o "$target.part"
mv "$target.part" "$target"
find "$BACKUP_DIR" -name 'solar-*.tar.age' -mtime +"$KEEP_DAYS" -delete

if [ -n "${RCLONE_REMOTE:-}" ]; then
  rclone copy "$target" "$RCLONE_REMOTE" --immutable
fi
echo "backup ok: $target ($(stat -c %s "$target") bytes)"
