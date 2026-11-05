#!/usr/bin/env bash
# Restores Nikah Noor from a folder written by backup.sh. This REPLACES the current data.
#
#   ./restore.sh backups/20261105-031500
#
# On a new server: copy this folder (with the backup), run `cp backups/<ts>/env .env`,
# start the database once with `docker compose up -d db`, then run this script.
set -euo pipefail
cd "$(dirname "$0")"

SRC="${1:?Usage: ./restore.sh backups/<timestamp>}"
[ -f "$SRC/database.dump" ] || { echo "No database.dump in $SRC" >&2; exit 1; }
[ -f .env ] || { echo "Missing .env (copy $SRC/env to .env first)" >&2; exit 1; }

envval() {
  local v
  v=$(grep -E "^$1=" .env | tail -n 1 | cut -d= -f2- | tr -d '"\r' || true)
  echo "${v:-$2}"
}
DB_USER=$(envval POSTGRES_USER muslimmatrimony)
DB_NAME=$(envval POSTGRES_DB muslimmatrimony)

if [ "${CONFIRM:-}" != "yes" ]; then
  read -r -p "This replaces all current Nikah Noor data with $SRC. Type 'yes' to continue: " answer
  [ "$answer" = "yes" ] || { echo "Aborted."; exit 1; }
fi

echo "Stopping the app"
docker compose stop web
docker compose up -d db
for _ in $(seq 1 30); do
  docker compose exec -T db pg_isready -U "$DB_USER" -d "$DB_NAME" > /dev/null 2>&1 && break
  sleep 1
done

echo "Restoring database $DB_NAME"
docker compose exec -T db psql -q -U "$DB_USER" -d "$DB_NAME" -c "DROP SCHEMA public CASCADE; CREATE SCHEMA public;" > /dev/null
docker compose exec -T db pg_restore -U "$DB_USER" -d "$DB_NAME" --no-owner --exit-on-error < "$SRC/database.dump"

if [ -f "$SRC/files.tar.gz" ]; then
  echo "Restoring photos and keys"
  docker compose run --rm --no-deps -T --entrypoint sh web -c 'find /data -mindepth 1 -delete && tar xzf - -C /data' < "$SRC/files.tar.gz"
fi
echo "Starting the app"
docker compose up -d
echo "Restored from $SRC. Run ./deploy.sh (or SKIP_PULL=1 ./deploy.sh) to wait for the health check."
