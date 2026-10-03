# Soso

- ASP.NET Core 10 minimal APIs, LiteDB, React/TypeScript/Vite and Mantine.
- All API routes are authenticated except health, login and CSRF bootstrap. Browser mutations require CSRF tokens.
- Route board and MCP operations through BoardService so access checks and revision checks cannot diverge.
- Do not expose password hashes, security stamps, raw stored tokens or image data in account responses.
- MCP uses the official C# SDK: https://github.com/modelcontextprotocol/csharp-sdk and https://csharp.sdk.modelcontextprotocol.io/.
- Keep MCP stateless and bearer-only; do not enable browser cookie access to MCP.
- Preserve secure cookies and require TLS in production. Never add self-registration.
- Build backend: dotnet build src/Soso.Api/Soso.Api.csproj.
- Build frontend: npm --prefix src/Soso.Web run build.