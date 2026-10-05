# Sosô

<p align="center">
  <img src="logo.jpg" alt="Logo do Sosô" width="240" />
</p>

<p align="center">
  <img src="https://flagcdn.com/w40/br.png" width="28" alt="Bandeira do Brasil" />
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

Sosô é um espaço kanban de código aberto para organizar o desenvolvimento de software. Projetos, tarefas e colaboração ficam reunidos em uma aplicação web, com API e servidor MCP integrados para conectar ferramentas e modelos de linguagem.

A aplicação combina uma interface React e TypeScript com uma API ASP.NET Core. Quadros e tarefas são armazenados em um banco LiteDB embutido; a aplicação web e a API são servidas juntas por um único container.

## Executar com Docker CLI

Para uso local, crie um volume Docker para persistir os dados e inicie o container. Informe um e-mail de administrador e uma senha exclusiva para a configuração inicial.

```sh
docker volume create soso-data
docker run -d --name soso --restart unless-stopped \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e LocalHttp=true \
  -e Bootstrap__Email=admin@example.com \
  -e Bootstrap__Password='substitua-por-uma-senha-exclusiva' \
  -p 127.0.0.1:8060:8080 \
  -v soso-data:/app/data \
  ghcr.io/icavalheiro/soso:latest
```

Abra `http://localhost:8060` no navegador. O container escuta na porta `8080`; o exemplo a publica na porta local `8060`. O volume nomeado preserva os dados entre reinicializações do container.

```sh
docker logs -f soso
docker stop soso
docker rm soso
```
