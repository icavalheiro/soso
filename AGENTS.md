# Repository guidance

- This repository contains Sosô, an open-source software-development kanban workspace built with ASP.NET Core 10 minimal APIs, LiteDB, React, TypeScript, Vite and Mantine. The web application, API and stateless bearer-authenticated MCP server are served together.
- Keep public READMEs concise and user-facing: describe what the project is, how it works at a high level, and provide a brief Docker CLI run example. Do not put detailed product rules, API/MCP contracts, security implementation notes, development/testing instructions or agent guidance in public READMEs; maintain agent-specific guidance here.
- Keep board and MCP operations routed through `BoardService` so access checks and revision checks cannot diverge.
- Preserve secure cookies and require TLS in production. Browser mutations require CSRF protection. Do not add public self-registration or browser cookie access to MCP.
- Keep MCP stateless and bearer-only; use the official C# SDK.
- Build backend: `dotnet build src/Soso.Api/Soso.Api.csproj`.
- Build frontend: `npm --prefix src/Soso.Web run build`.
- Keep agent and contributor guidance in this file, not in public READMEs. Copilot instructions point back to this file.
- Before considering a task complete, run `make verify` from the repository root. This is the same verification target used by GitHub Actions.
- Do not report a task as fully verified if any check fails or cannot run. Include the failing check and its output, and resolve the failure or clearly report the environment blocker.
- Keep `.github/workflows/ci.yml` invoking `make verify` so local and CI verification stay in sync.
- Preserve unrelated working-tree changes.
