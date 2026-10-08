# Sosô

<p align="center">
  <img src="logo.png" alt="Logo de Sosô" width="240" />
</p>

<p align="center">
  <img src="https://flagcdn.com/w40/br.png" width="28" alt="Bandera de Brasil" />
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

Sosô es un espacio kanban de código abierto para organizar el desarrollo de software. Reúne proyectos, tareas y colaboración en una aplicación web, con una API y un servidor MCP integrados para conectar herramientas y modelos de lenguaje.

La aplicación combina una interfaz de React y TypeScript con una API de ASP.NET Core. Los tableros y las tareas se almacenan en una base de datos LiteDB integrada; la aplicación web y la API se sirven juntas desde un solo contenedor.

## Ejecutar con Docker CLI

Para uso local, crea un volumen de Docker para conservar los datos e inicia el contenedor. Define un correo de administrador y una contraseña única para la configuración inicial.

```sh
docker volume create soso-data
docker run -d --name soso --restart unless-stopped \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e LocalHttp=true \
  -e Bootstrap__Email=admin@example.com \
  -e Bootstrap__Password='reemplaza-por-una-contrasena-unica' \
  -p 127.0.0.1:8060:8080 \
  -v soso-data:/app/data \
  ghcr.io/icavalheiro/soso:latest
```

Abre `http://localhost:8060` en el navegador. El contenedor escucha en el puerto `8080`; el ejemplo lo publica en el puerto local `8060`. El volumen con nombre conserva los datos entre reinicios del contenedor.

```sh
docker logs -f soso
docker stop soso
docker rm soso
```
