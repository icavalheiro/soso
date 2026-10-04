# Sosô

A lightweight software-development kanban workspace built with ASP.NET Core 10 minimal APIs, LiteDB, React, TypeScript, Vite and Mantine.

## Workspace

- Multiple private boards with owner-managed membership and configurable columns.
- Clean draggable cards, keyboard dragging, subtle lift/drop animation, search, assignee and priority filters.
- Built-in Bug, Feature, Design, Docs, Refactor, Test, Chore and Research tags.
- Ticket dialogs with descriptions, subtasks, comments, images, assignees and due dates.
- Done columns, archive/restore actions and separate completed/archived views.
- Collapsible navigation, light/dark themes, profile photos and private custom preferences.
- Administrator-only account creation, disabling and password resets. No public sign-up.
- Authenticated built-in Streamable HTTP MCP server at `/mcp`.

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

## Separate Desktop Documentation

The following Tauri/Rust notes describe a separate desktop variant. Its commands and SQLite data paths do not apply to the ASP.NET Core / LiteDB web application documented above.

# Sosô

<img src="logo.jpg" alt="Logo do Sosô" width="240" />

Kanban desktop local com Tauri 2, Rust, React, TypeScript e Mantine em tema escuro. Permite criar e selecionar multiplos boards, criar e editar tickets, arrastar e reordenar, mudar estados, comentar, adicionar e marcar subtarefas, buscar e alternar entre board e lista.

## Executar

Clone o repositorio e abra a pasta `soso` no VS Code. Requer Node.js 22.12 ou superior, npm e Rust stable. No Windows, instale a toolchain MSVC, Microsoft C++ Build Tools (carga de trabalho Desktop development with C++) e WebView2. Consulte os [pre-requisitos oficiais do Tauri](https://v2.tauri.app/start/prerequisites/) para cada sistema operacional. O empacotamento atual usa NSIS e tem como alvo Windows.

```cmd
npm ci
npm run dev
```

`npm run dev` abre a janela Tauri e inicia o frontend Vite em `http://127.0.0.1:1420`. O Vite nao possui backend de persistencia. Abrir apenas essa URL no navegador nao fornece o IPC Tauri.

```cmd
npm run build:desktop
```

Compila o executavel desktop, sem instalador. Para gerar o instalador NSIS, use `npm run tauri -- build`. Os artefatos ficam em `target/release/`. Os icones estao incluidos; `npm run icons` regenera os PNG/ICO a partir de `logo.jpg`.

## Dados

- Banco unico: `~/.soso/soso.sqlite3`, ou `%USERPROFILE%\.soso\soso.sqlite3` no Windows.
- Cada board tem ID proprio, colunas, tickets, comentarios, subtarefas, historico e revisao independente.
- SQLite usa uma tabela `boards` com snapshots JSON por board e revisao, em transacoes atomicas. Nao ha arquivos Markdown como fonte de verdade.
- WAL, timeout de bloqueio e revisoes impedem sobrescritas concorrentes entre o app e o MCP. Uma edicao com revisao antiga e recusada; o rascunho fica no editor.
- Escritas pelo MCP embutido notificam a janela para atualizar o board e a lista de boards imediatamente. Consultas a cada cinco segundos e ao receber foco continuam como fallback para alteracoes por outros processos. Rascunhos e suas revisoes originais sao preservados.
- O banco nao e criptografado: sua protecao depende das permissoes da conta do sistema. Nao armazene segredos em tickets.
- Para backup, feche o app e os servidores MCP e copie a pasta `.soso` inteira, incluindo eventuais arquivos `-wal` e `-shm`. Nunca copie somente o arquivo principal durante escritas.

## MCP Integrado ao App

O app inicia um servidor MCP Streamable HTTP em `http://127.0.0.1:17842/mcp`, dentro do processo desktop. Enquanto a janela estiver aberta, clientes compativeis, incluindo o VS Code, podem ler e alterar os boards. Ao sair do app, o servidor e encerrado.

1. Abra o Sosô e selecione o board que deseja acompanhar.
2. No cabecalho, abra **Conexao MCP** pelo icone de plugue. O painel mostra URL, token da sessao e configuracao VS Code.
3. Copie a configuracao para `.vscode/mcp.json` na pasta de trabalho do VS Code. Esse arquivo local nao e versionado neste repositorio.
4. Execute **MCP: List Servers**, inicie `soso` e informe o token no campo protegido quando solicitado. A confianca e as aprovacoes de ferramentas continuam sob controle do cliente.

```json
{
  "servers": {
    "soso": {
      "type": "http",
      "url": "http://127.0.0.1:17842/mcp",
      "headers": {
        "Authorization": "Bearer ${input:soso-mcp-token}"
      }
    }
  },
  "inputs": [
    {
      "type": "promptString",
      "id": "soso-mcp-token",
      "description": "Token MCP da sessao Sosô",
      "password": true
    }
  ]
}
```

O token e aleatorio, fica apenas na memoria do app e muda a cada inicializacao. Se o cliente tiver salvo uma entrada anterior, substitua-a pelo token da nova sessao e reconecte. Nao grave o token em arquivos versionados, tickets ou logs. Outros clientes devem enviar o mesmo cabecalho `Authorization: Bearer <token>` e suportar Streamable HTTP.

O servidor escuta somente em IPv4 loopback, valida `Host`, rejeita requisicoes com `Origin` de navegador e limita o corpo das requisicoes a 256 KiB. Nao e um endpoint publico e nao oferece CORS para aplicativos web. Se a porta 17842 estiver ocupada, o painel informa o erro; o board continua funcionando. Encerre o processo que voce conhece como ocupante e reabra o app, sem interromper processos desconhecidos.

### Ferramentas

Ferramentas:

- `list_boards`: lista IDs e nomes dos boards.
- `read_board`: retorna tickets e a revisao atual.
- `create_board`: cria um board independente.
- `mutate_board`: cria/edita/move tickets, comenta, adiciona ou marca subtarefas. Exige ID do board e revisao atual.

Exemplo de argumentos para `mutate_board`:

```json
{
  "id": "ID retornado por list_boards",
  "revision": "REVISAO retornada por read_board",
  "operation": { "type": "comment", "id": "ID do ticket", "body": "Validacao registrada no VS Code." }
}
```

O MCP e o Tauri compartilham as mesmas ferramentas, nucleo Rust e SQLite. Nao ha SQL livre, execucao de comandos ou leitura arbitraria de arquivos. As ferramentas de escrita podem alterar qualquer board dessa conta local, nao apenas o board visivel.

### Alternativa Stdio Sem Janela

O servidor independente permanece disponivel para clientes que precisam trabalhar com o app fechado:

```cmd
npm run build:mcp
```

Use esta configuracao no lugar da conexao HTTP:

```json
{
  "servers": {
    "soso": {
      "type": "stdio",
      "command": "${workspaceFolder}/target/debug/soso-mcp.exe"
    }
  }
}
```

Nesse modo o cliente inicia o processo MCP; nao ha listener HTTP nem token de sessao. Alteracoes aparecem na janela pelo polling de fallback, caso ela esteja aberta.

Para um binario otimizado: `npm run build:mcp:release` e ajuste a configuracao para `target/release/soso-mcp.exe`. Em Linux/macOS, remova `.exe`. O argumento opcional `--database <caminho>` e exclusivo de inicializacao do servidor, usado nos testes para bancos temporarios.

## Validacao

```cmd
npm test
npx playwright install chromium
npm run test:e2e
npm run lint
npm run build
cargo fmt --all -- --check
```

- Quatro testes Rust do nucleo: persistencia, isolamento, revisoes, fluxo completo, importacao e concorrencia.
- Dois testes Rust do MCP HTTP: autenticacao, Host/Origin, limite de corpo, ferramentas, notificacoes apos commits, conflitos, encerramento e porta ocupada. Usam listeners locais em portas efemeras e bancos temporarios.
- Um teste MCP: handshake, descoberta das ferramentas e duas instancias compartilhando SQLite.
- Cinco testes Playwright: boards independentes, tickets/comentarios/subtarefas, recarga, eventos MCP, conflito de rascunho, configuracao sem token embutido, erro de conexao e janela estreita.
- Playwright usa o MCP stdio com banco temporario e simula o IPC de status/eventos Tauri; nao valida o servidor HTTP nem a WebView nativa. Os testes Rust exercitam o HTTP real. Servidores, navegadores e bancos temporarios sao encerrados/removidos automaticamente.
- O build Vite pode emitir um aviso nao bloqueante de bundle JavaScript acima de 500 kB.
- O GitHub Actions executa lint, build frontend, testes Rust/MCP/Playwright e build desktop no Windows e no Ubuntu 24.04. O job Linux instala as dependencias nativas do Tauri e do Chromium. O build desktop usa `--no-bundle`; o instalador NSIS continua exclusivo do Windows. Uma execucao verde em ambos os sistemas e necessaria para validar o desktop em ambientes sem restricoes locais.

## Estrutura

- `src/` e `shared/`: interface React e contratos TypeScript.
- `crates/soso-core/`: regras de negocio, revisoes e persistencia SQLite.
- `crates/soso-mcp/`: ferramentas MCP compartilhadas, transporte HTTP embutido e alternativa stdio.
- `src-tauri/`: aplicativo desktop e configuracao de empacotamento.
- `tests/` e `e2e/`: integracao MCP e interface com bancos temporarios.

## Solucao de Problemas

Politicas de Controle de Aplicativo do Windows podem bloquear a CLI Tauri ou scripts de build Rust (por exemplo, erro 4551). Solicite a liberacao das ferramentas ao administrador responsavel; nao desative politicas de seguranca. Os testes Playwright nao substituem a validacao da janela nativa e do instalador.

## Contribuir

Veja [CONTRIBUTING.md](CONTRIBUTING.md) para desenvolvimento e pull requests, e [SECURITY.md](SECURITY.md) para orientacoes de seguranca. Bugs e sugestoes podem ser registrados nas Issues do repositorio, sem incluir dados pessoais, segredos ou bancos locais.

## Licenca

Este projeto e distribuido sob a GNU General Public License v3.0, somente esta versao (`GPL-3.0-only`). Consulte [LICENSE](LICENSE). Dependencias e recursos de terceiros mantem suas proprias licencas. Antes de distribuir binarios, revise essas licencas e disponibilize o codigo-fonte correspondente conforme a GPL.