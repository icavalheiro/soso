# Sosô

<p align="center">
  <img src="logo.png" alt="Sosô logo" width="240" />
</p>

<p align="center">
  <img src="https://flagcdn.com/w40/br.png" width="28" alt="Brazil flag" />
  <span>Made in Brazil.</span>
</p>

<p align="center">
  <a href="README.md">🇺🇸 English</a> | <a href="README.pt-br.md">🇧🇷 Português (Brasil)</a> | <a href="README.es-mx.md">🇲🇽 Español (México)</a>
  <br /><br />
  <img src="https://img.shields.io/badge/C%23-512BD4?logo=sharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/ASP.NET_Core-512BD4?logo=dotnet&logoColor=white" alt="ASP.NET Core" />
  <img src="https://img.shields.io/badge/LiteDB-2C3E50?logo=databricks&logoColor=white" alt="LiteDB" />
  <img src="https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white" alt="TypeScript" />
</p>

Sosô is an open-source kanban workspace for organizing software development. It brings projects, tasks and collaboration together in a web application, with an integrated API and MCP server for connecting tools and language models.

The application combines a React and TypeScript interface with an ASP.NET Core API. Boards and tasks are stored in an embedded LiteDB database, while the web application and API are served together by one container.

## Run with Docker CLI

For local use, create a Docker volume for persistent application data and start the container. Set an administrator email and a unique password for the initial setup.

```sh
docker volume create soso-data
docker run -d --name soso --restart unless-stopped \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e LocalHttp=true \
  -e Bootstrap__Email=admin@example.com \
  -e Bootstrap__Password='replace-with-a-unique-password' \
  -p 127.0.0.1:8060:8080 \
  -v soso-data:/app/data \
  ghcr.io/icavalheiro/soso:latest
```

Open `http://localhost:8060` in your browser. The container listens on port `8080`; the example publishes it on local port `8060`. The named volume preserves application data between container restarts.

```sh
docker logs -f soso
docker stop soso
docker rm soso
```
