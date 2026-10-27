#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")"

git pull --rebase
docker compose pull
docker compose build --pull
docker compose up -d