# Sosô

<p align="center">
  <img src="logo.jpg" alt="Logo do Sosô" width="240" />
</p>

<p align="center">
  <img src="https://flagcdn.com/w40/br.png" width="28" alt="Bandeira do Brasil" />
  <span>Made in brazil.</span>
</p>

<p align="center">
  <a href="README.md">🇺🇸 English</a> | <a href="README.pt-br.md">🇧🇷 Português (Brasil)</a> | <a href="README.es-mx.md">🇲🇽 Español (México)</a>
  <br /><br />
  <img src="https://img.shields.io/badge/C%23-512BD4?logo=sharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/ASP.NET_Core-512BD4?logo=dotnet&logoColor=white" alt="ASP.NET Core" />
  <img src="https://img.shields.io/badge/LiteDB-2C3E50?logo=databricks&logoColor=white" alt="LiteDB" />
  <img src="https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white" alt="TypeScript" />
</p>

A lightweight, llm/mcp friendly, software-development kanban workspace built with ASP.NET Core 10 minimal APIs, LiteDB, React, TypeScript, Vite and Mantine.

## Workspace

- Multiple private boards with owner-managed membership and configurable columns.
- Clean draggable cards, keyboard dragging, subtle lift/drop animation, search, assignee and priority filters.
- Built-in Bug, Feature, Design, Docs, Refactor, Test, Chore and Research tags.
- Ticket dialogs with descriptions, subtasks, comments, images, assignees and due dates.
- Done columns, archive/restore actions and separate completed/archived views.
- Collapsible navigation, light/dark themes, English, Brazilian Portuguese and Mexican Spanish, profile photos and private custom preferences.
- Administrator-only account creation, disabling and password resets. No public sign-up.
- Authenticated built-in Streamable HTTP MCP server at `/mcp`.

## Run With Docker (Recommended)

The preferred way to run Sosô is Docker using the prebuilt image from [GitHub Container Registry](https://github.com/icavalheiro/soso/pkgs/container/soso). You do not need the source code, Docker Compose, the .NET SDK or Node.js.

Create a private `soso.env` file with the following environment variables, replacing the hostname, email and password with your own values. Do not commit this file; restrict its filesystem permissions.

```dotenv
ASPNETCORE_ENVIRONMENT=Production
Bootstrap__Email=admin@example.com
Bootstrap__Password=replace-with-a-unique-strong-password
Bootstrap__Name=Administrator
AllowedHosts=soso.example.com;127.0.0.1;localhost
CloudflareTunnel=true
Logging__LogLevel__Default=Warning
```

`Bootstrap__Email` and `Bootstrap__Password` are required on first startup; the password must be 14-128 characters. `Bootstrap__Name` is optional. Bootstrap settings never overwrite existing accounts. Set `AllowedHosts` to your public hostname, keeping the loopback entries. `CloudflareTunnel=true` is for a host-based Cloudflare Tunnel; if Docker gateway detection fails, add `TrustedProxy=<exact-tunnel-peer-IP>` to the file. These are application variable names, not the `BOOTSTRAP_EMAIL`, `BOOTSTRAP_PASSWORD` and `DOMAIN` substitutions used by Compose.

Download and run the image with Docker CLI:

```cmd
docker pull ghcr.io/icavalheiro/soso:latest
docker run -d --name soso --restart unless-stopped --env-file soso.env -p 127.0.0.1:8060:8080 -v soso-data:/app/data --read-only --tmpfs /tmp:size=64m,mode=1777 --cap-drop ALL --security-opt no-new-privileges:true --memory 512m --cpus 1 ghcr.io/icavalheiro/soso:latest
```

## Local Development

Requires .NET 10 SDK and Node.js 24. A localhost development HTTPS certificate must be available and trusted by your browser. If needed, run `dotnet dev-certs https --trust` yourself.

Set `Bootstrap__Email` and `Bootstrap__Password` (14-128 characters) in your terminal environment. Optional: `Bootstrap__Name`. Sosô fails startup if no active administrator exists and valid bootstrap credentials are missing. Existing accounts are never overwritten by bootstrap settings.

```cmd
npm --prefix src/Soso.Web ci
npm --prefix src/Soso.Web run build
xcopy /E /I /Y src\Soso.Web\dist src\Soso.Api\wwwroot
dotnet run --project src/Soso.Api
```

Open https://localhost:7240. On Linux/macOS replace the `xcopy` command with `mkdir -p src/Soso.Api/wwwroot && cp -r src/Soso.Web/dist/. src/Soso.Api/wwwroot/`.

For frontend hot reload, run `npm --prefix src/Soso.Web run dev` alongside the API and open the Vite localhost URL. Its proxy targets the API at https://localhost:7240. Certificate verification is disabled **only in the development proxy**. Use the HTTPS API origin for testing secure-cookie behavior across browsers.

## Local Docker Testing

Set `BOOTSTRAP_EMAIL` and a strong `BOOTSTRAP_PASSWORD` of 14-128 characters in `.env`, using `.env.example` as a reference. No public domain, tunnel or TLS certificate is needed for this local-only configuration.

```cmd
docker compose -f docker-compose.local.yml up -d --build
docker compose -f docker-compose.local.yml ps
```

Open http://localhost:8061. The local project and data volume are separate from production, and port 8061 avoids conflicting with the production origin on 8060. Set `SOSO_LOCAL_PORT` in `.env` to use another free port. Use this file by itself with `-f`, not as an override merged with `docker-compose.yml`.

This configuration enables `LocalHttp=true` only in `Development`: session and CSRF cookies use local names and accept HTTP, while CSRF validation remains enabled. Production ignores this option and retains HTTPS-only `__Host-` cookies. The published port is bound exclusively to loopback. Do not expose this development configuration publicly. Bootstrap changes do not update an administrator already created in the local data volume.

```cmd
docker compose -f docker-compose.local.yml logs -f soso
docker compose -f docker-compose.local.yml down
```

`down` preserves local data. Adding `--volumes` deletes the local database and keys; it does not affect the separate production volume.

## Production

1. Configure your public hostname in Cloudflare Tunnel. Keep public HTTPS enabled at Cloudflare and enable Always Use HTTPS; no inbound public application ports are needed.
2. Create `.env` using `.env.example`, set `DOMAIN` to the tunnel's public hostname and set a unique strong bootstrap password. Never commit this file; restrict its filesystem permissions.
3. Run `docker compose up -d --build`. ASP.NET Core serves the React application, API and MCP at `http://127.0.0.1:8060`; the container listens internally on port 8080. No separate reverse proxy is needed.
4. Point your host-based `cloudflared` service to `http://127.0.0.1:8060`. Sign in at `https://<DOMAIN>`, create team accounts from Accounts, and add members in each board's settings.

The single application container is published only on host loopback port **8060**. It runs without root, with a read-only root filesystem, dropped Linux capabilities, no-new-privileges and resource limits. The image includes the frontend build in `wwwroot`; ASP.NET Core serves static files and SPA routes alongside `/api` and `/mcp`. Cloudflare terminates public TLS; the host-local tunnel-to-application hop uses HTTP. Do not change the loopback binding to a public interface. Keep the public hostname unchanged in tunnel requests and do not override the forwarded scheme or Cloudflare client-IP header. The API rejects requests forwarded as HTTP. Kestrel streams MCP responses directly without an intermediate buffering proxy.

Compose uses its standard bridge network, without fixed container IPs or a custom subnet. `CloudflareTunnel=true` accepts `CF-Connecting-IP` and `X-Forwarded-Proto` only from loopback or the exact Docker host IPv4 gateway detected at startup. It does not trust arbitrary forwarded-IP chains or whole networks. If there is no unique gateway, startup fails closed: set `TRUSTED_PROXY` in `.env` to the exact source IP seen by the container. That setting also overrides gateway detection for a different Docker networking setup. Other host processes can reach this local origin, so keep the host trusted and patched.

This Compose configuration assumes `cloudflared` runs on the host. If the tunnel runs in another container or on another machine, configure a private reachable origin and its exact `TRUSTED_PROXY`; `127.0.0.1` inside a different container does not refer to this host. Do not trust broad IP ranges or expose the origin publicly. Outside this Compose deployment, leave `CloudflareTunnel` disabled unless the requests actually come through Cloudflare Tunnel.

For a locally managed tunnel, add an ingress rule to its existing configuration:

```yaml
ingress:
  - hostname: soso.example.com
    service: http://127.0.0.1:8060
  - service: http_status:404
```

For a dashboard-managed tunnel, configure the same hostname and HTTP origin there. Keep tunnel credentials outside this repository. Do not log authorization headers or MCP tokens.

LiteDB, processed images and cookie-encryption keys are persisted in `soso-data`. Run **one API replica**: embedded LiteDB is not a shared/distributed database. Stop the app before copying its database for a consistent backup. Back up the full data volume, including `keys`, and test restores. Use host-disk encryption and protected backups; data-protection keys are protected by volume permissions, not a separate key vault.

Bootstrap settings are only used when no active administrator exists. Rotate the initial password from Profile / Security after provisioning. Do not leave the sample password in production. Changing a password invalidates all sessions and MCP tokens for that account. Disabling the last active administrator is rejected.

## MCP / LLM Clients

The web application serves an LLM integration guide at `/llms.txt`, covering client configuration, permissions, tool arguments, revision handling and safe agent workflows. Its source is [src/Soso.Web/public/llms.txt](src/Soso.Web/public/llms.txt); Vite includes it in the frontend build and the application serves it as a public static asset.

Create a token in Profile & settings / MCP. Each token starts with access to no boards. Select its Assigned boards and click Save boards to grant access; clear and save the selection to remove access. Existing tokens without assignments also have no board access. MCP access is the intersection of these assignments and the token owner's current board permissions, including for administrators. Changes take effect on subsequent MCP operations. Tokens are shown once, stored only as SHA-256 hashes, expire in 30 days and can be revoked immediately. Configure a client that supports **Streamable HTTP with custom bearer headers**:

```json
{
  "servers": {
    "soso": {
      "type": "http",
      "url": "https://soso.example.com/mcp",
      "headers": { "Authorization": "Bearer <your-personal-token>" }
    }
  }
}
```

The included `.vscode/mcp.json` prompts for the token without writing it into the repository. Change its URL for your deployment. Available tools: `list_boards`, `get_board`, `create_ticket`, `create_tickets`, `move_ticket`, `update_ticket`, `update_tickets`, `add_comment`, `search_tickets`. `update_ticket` supports tags, subtasks, archive state and other editable properties with revision checks. Batch tools atomically insert or update 1 to 100 tickets in one board; a failed item rolls back the entire batch. `search_tickets` searches ticket IDs and text in accessible boards with pagination and optional archived results; pass a full or partial ticket ID as `query` to find it by ID. Tools use the caller's board permissions and cannot create accounts. Cookies are not accepted at MCP endpoints; bearer tokens are not accepted by browser API endpoints. This version does not implement OAuth discovery/registration, so clients requiring that flow need a compatible gateway. Treat board text and comments as untrusted context; require human approval for LLM writes.

MCP creation requires a nonblank work specification in `description` and 1 to 8 software-work `tags`, saved with the title and column. Comments are for progress, decisions, blockers and verification results, not a substitute for the specification. Completion is determined only by a column with `isDone=true`: preserve the description and move the ticket there instead of writing "completed" while leaving it in progress. Server initialization and tool descriptions include these instructions.

`update_ticket` and `update_tickets` now use partial patches: send the current ticket `revision` and only fields to change. Omitted or null fields are preserved; supplied arrays replace that collection, `[]` clears tags/subtasks, and `""` clears description. Use `clearAssignee=true` or `clearDueDate=true` to explicitly remove those nullable values. For example, `{"revision":3,"priority":"high"}` changes priority without resetting anything else. Existing clients that clear nullable fields with `null` must switch to the clear flags and refresh MCP tool discovery after deployment. The browser API's full-update contract is unchanged.

## Security And Validation

Browser sessions use HttpOnly, Secure, SameSite=Strict cookies, 12-hour expiry and account-state checks on every request. All browser mutations (including login) require a CSRF token. Login has per-IP throttling and persistent account lockout after five failures. Production rejects cleartext requests except loopback health checks, trusts a configured proxy, validates input, restricts request/image sizes, strips image metadata and serves attachments through authenticated routes. Ticket and board updates use revisions and return 409 on conflicting changes.

Anonymous routes are limited to login, CSRF bootstrap, a minimal health response and static application assets. Static assets contain no private board data. Logged-in users can see the team name/avatar directory for board sharing; email and personal settings are not exposed there.

```cmd
dotnet test tests/Soso.Api.Tests/Soso.Api.Tests.csproj
npm --prefix src/Soso.Web run build
npm --prefix src/Soso.Web run lint
dotnet list src/Soso.Api/Soso.Api.csproj package --vulnerable --include-transitive
npm --prefix src/Soso.Web audit
```

Browser interaction tests use a simulated API, separate from the real API integration tests:

```cmd
cd src/Soso.Web
npx playwright install chromium
npm run test:e2e
```

They cover login, software tags, assignee/search filters, cross-column dragging and animation, checklist/comments/images, archive/restore, account screens and desktop/mobile light/dark layouts. Screenshots and failure traces are stored under `src/Soso.Web/test-results`. CI runs both suites and builds the production image.

Tests cover anonymous access, CSRF, administrator-only accounts, cross-board isolation, revisions, tags, archive, image validation, session invalidation, lockout, authenticated MCP tool calls and rejection of forwarded scheme/client-IP headers from untrusted peers. Public hosting still requires host patching, monitoring, backups, secret management, dependency updates and human security review. No application can promise unconditional security.

## Web Application License

The repository's original GNU GPLv3 license is preserved in `LICENSE`. LiteDB, Mantine, React and dnd-kit use MIT; the MCP C# SDK uses Apache-2.0; Lucide uses ISC. ImageSharp's Six Labors Split License grants Apache-2.0 for use in open-source software, the applicable criterion for Sosô. Reassess those terms before adopting it in a closed-source commercial derivative. Dependency licenses and notices remain available in their published packages and source repositories. Do not assume the logo has independent redistribution rights beyond those supplied by its owner.



