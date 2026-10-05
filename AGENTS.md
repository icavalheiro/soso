# Repository guidance

- Before considering a task complete, run `make verify` from the repository root. This is the same verification target used by GitHub Actions.
- Do not report a task as fully verified if any check fails or cannot run. Include the failing check and its output, and resolve the failure or clearly report the environment blocker.
- Keep `.github/workflows/ci.yml` invoking `make verify` so local and CI verification stay in sync.
- Preserve unrelated working-tree changes.
