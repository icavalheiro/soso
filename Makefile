.PHONY: test verify

TESTS_IMAGE ?= soso-tests:local

DOMAIN ?= soso.example.test
BOOTSTRAP_EMAIL ?= admin@example.test
BOOTSTRAP_PASSWORD ?= configuration-validation-only
export DOMAIN BOOTSTRAP_EMAIL BOOTSTRAP_PASSWORD

test:
	docker build --pull -f tests.Dockerfile -t $(TESTS_IMAGE) .
	docker run --rm $(TESTS_IMAGE)

verify: test
	docker build -t soso:ci .
	docker compose config --quiet
