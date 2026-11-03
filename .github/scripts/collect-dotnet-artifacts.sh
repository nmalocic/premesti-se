#!/usr/bin/env bash
set -euo pipefail

# Run from the repository root after locked restore, build, and checks.
dotnet publish src/PremestiSe.Api/PremestiSe.Api.csproj -c Release --no-restore -o artifacts/api
dotnet publish src/PremestiSe.Importer/PremestiSe.Importer.csproj -c Release --no-restore -o artifacts/importer
dotnet build src/PremestiSe.Db.Api/PremestiSe.Db.Api.csproj -c Release --no-restore -o artifacts/database-build

for project in Api Importer; do
  output="artifacts/${project,,}/PremestiSe.$project"
  for suffix in dll deps.json runtimeconfig.json; do
    test -s "$output.$suffix"
  done
done

# Same build-output pattern as deploy/VeloxDB.Dockerfile, with an explicit manifest
# so a missing deployment dependency cannot silently produce a partial artifact.
mkdir -p artifacts/database
for project in Db.Model Db.Api Db.Contracts Domain; do
  assembly="PremestiSe.$project.dll"
  test -s "artifacts/database-build/$assembly"
  cp "artifacts/database-build/$assembly" "artifacts/database/$assembly"
done
