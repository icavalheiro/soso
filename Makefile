.PHONY: verify

PLAYWRIGHT_INSTALL_FLAGS ?=

DOMAIN ?= soso.example.test
BOOTSTRAP_EMAIL ?= admin@example.test
BOOTSTRAP_PASSWORD ?= configuration-validation-only
export DOMAIN BOOTSTRAP_EMAIL BOOTSTRAP_PASSWORD

verify:
	dotnet test tests/Soso.Api.Tests/Soso.Api.Tests.csproj --configuration Release
	dotnet list src/Soso.Api/Soso.Api.csproj package --vulnerable --include-transitive
	npm ci --prefix src/Soso.Web
	npm run build --prefix src/Soso.Web
	npm run lint --prefix src/Soso.Web
	npm audit --prefix src/Soso.Web --audit-level=moderate
	npm exec --prefix src/Soso.Web -- playwright install $(PLAYWRIGHT_INSTALL_FLAGS) chromium
	npm run test:e2e --prefix src/Soso.Web
	docker build -t soso:ci .
	docker compose config --quiet
