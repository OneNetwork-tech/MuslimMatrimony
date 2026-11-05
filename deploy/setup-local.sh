#!/usr/bin/env bash
# Creates .env for trying Nikah Noor on this computer, with freshly generated secrets.
#
#   ./setup-local.sh                                  # image tag "latest" (default branch)
#   ./setup-local.sh claude-charming-noether-lhy664   # the newest build of a branch
#
# Then: ./deploy.sh   and open http://localhost:8083
# Needs Docker with Compose and openssl. For a real server, edit .env afterwards
# (see the comments in it): this sets plain-HTTP, test-friendly values.
set -euo pipefail
cd "$(dirname "$0")"

if [ -f .env ]; then
  echo "$(pwd)/.env already exists. Delete it (and run 'docker compose down -v' to wipe the data) to start over." >&2
  exit 1
fi

cp .env.example .env
chmod 600 .env

# Sets NAME=VALUE in .env (replaces an existing line, or appends one). Works with GNU and BSD sed.
setval() {
  if grep -qE "^$1=" .env; then
    sed -i.bak "s|^$1=.*|$1=$2|" .env && rm -f .env.bak
  else
    printf '%s=%s\n' "$1" "$2" >> .env
  fi
}

setval IMAGE_TAG "${1:-latest}"
setval POSTGRES_PASSWORD "$(openssl rand -hex 16)"
setval Security__HttpsOnly false
# No mail server: print emails, including the account confirmation link, in the log
setval Email__LogBodies true

echo "Created $(pwd)/.env (image tag: ${1:-latest})."
echo "Next: ./deploy.sh   then open http://localhost:8083"
echo "After registering, get the confirmation link with: docker compose logs web | grep -A2 \"Confirm your\""
