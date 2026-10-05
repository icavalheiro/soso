#!/usr/bin/env bash
set -euo pipefail

dotnet test tests/Soso.Api.Tests/Soso.Api.Tests.csproj --configuration Release --no-restore
dotnet list src/Soso.Api/Soso.Api.csproj package --vulnerable --include-transitive
npm run build --prefix src/Soso.Web
npm run lint --prefix src/Soso.Web
npm audit --prefix src/Soso.Web --audit-level=moderate
npm run test:e2e --prefix src/Soso.Web -- --workers=2
