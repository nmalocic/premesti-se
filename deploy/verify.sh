#!/usr/bin/env bash
set -euo pipefail
# Run from the repository root. The test container joins the real Compose network.
docker compose config --quiet
docker compose up -d --build --wait

sdk() {
  docker run --rm --network premesti-se_default \
    -v "$PWD:/workspace" -v premesti-se-nuget:/root/.nuget/packages -w /workspace \
    -e API_URL=http://api:8080 -e DATABASE_ADDRESS=veloxdb:7568 \
    -e SMTP_HOST=mailpit -e EXPECT_DATABASE_HEALTHY="${1:-true}" \
    mcr.microsoft.com/dotnet/sdk:8.0.422 sh -c \
    'dotnet restore --locked-mode && dotnet build --no-restore && dotnet test --no-build --no-restore'
}

sdk true
api_id=$(docker compose ps -q api)
# Restore the database even when outage assertions fail.
trap 'docker compose start veloxdb' EXIT
docker compose stop veloxdb
sdk false
docker compose start veloxdb
docker compose up -d --wait --no-build
sdk true
test "$api_id" = "$(docker compose ps -q api)"
trap - EXIT

docker run --rm --network premesti-se_default \
  -v "$PWD/web:/work" -w /work -e APP_URL=http://api:8080 \
  mcr.microsoft.com/playwright:v1.56.1-noble \
  sh -c 'npm ci && npm run build && npm run test:e2e'
