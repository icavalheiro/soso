# Sosô

<p align="center">
  <img src="logo.jpg" alt="Logo de Sosô" width="240" />
</p>

<p align="center">
  <img src="https://flagcdn.com/w40/br.png" width="28" alt="Bandera de Brasil" />
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

Un espacio kanban ligero para desarrollo de software, compatible con LLM y MCP, creado con APIs minimalistas de ASP.NET Core 10, LiteDB, React, TypeScript, Vite y Mantine.

## Espacio de trabajo

- Varios tableros privados con administración de miembros por parte del propietario y columnas configurables.
- Tarjetas fáciles de arrastrar, movimiento con teclado, animaciones sutiles al mover, búsqueda y filtros por responsable y prioridad.
- Etiquetas integradas: Bug, Feature, Design, Docs, Refactor, Test, Chore y Research.
- Ventanas de tareas con descripciones, subtareas, comentarios, imágenes, responsables y fechas de entrega.
- Columnas de tareas completadas, acciones para archivar/restaurar y vistas separadas para elementos completados y archivados.
- Navegación contraíble, temas claro/oscuro, inglés, portugués de Brasil y español de México, fotos de perfil y preferencias personalizadas privadas.
- Creación de cuentas, desactivación y restablecimiento de contraseñas solo para administradores. No hay registro público.
- Servidor MCP integrado y autenticado mediante HTTP Streamable en `/mcp`.

## Ejecutar con Docker (recomendado)

La forma recomendada de ejecutar Sosô es con Docker, usando la imagen precompilada de [GitHub Container Registry](https://github.com/icavalheiro/soso/pkgs/container/soso). No necesitas el código fuente, Docker Compose, el SDK de .NET ni Node.js.

Crea un archivo privado `soso.env` con las siguientes variables de entorno y reemplaza el host, el correo y la contraseña con tus propios valores. No subas este archivo al repositorio; limita sus permisos en el sistema de archivos.

```dotenv
ASPNETCORE_ENVIRONMENT=Production
Bootstrap__Email=admin@example.com
Bootstrap__Password=replace-with-a-unique-strong-password
Bootstrap__Name=Administrator
AllowedHosts=soso.example.com;127.0.0.1;localhost
CloudflareTunnel=true
Logging__LogLevel__Default=Warning
```

`Bootstrap__Email` y `Bootstrap__Password` son obligatorios durante el primer inicio; la contraseña debe tener entre 14 y 128 caracteres. `Bootstrap__Name` es opcional. La configuración de bootstrap nunca reemplaza cuentas existentes. Define `AllowedHosts` con tu host público y conserva las entradas de loopback. `CloudflareTunnel=true` es para un Cloudflare Tunnel ejecutado en el host; si falla la detección del gateway de Docker, agrega `TrustedProxy=<IP-exacta-del-peer-del-túnel>` al archivo. Estos son nombres de variables de la aplicación, no las sustituciones `BOOTSTRAP_EMAIL`, `BOOTSTRAP_PASSWORD` y `DOMAIN` que usa Compose.

Descarga y ejecuta la imagen con Docker CLI:

```cmd
docker pull ghcr.io/icavalheiro/soso:latest
docker run -d --name soso --restart unless-stopped --env-file soso.env -p 127.0.0.1:8060:8080 -v soso-data:/app/data --read-only --tmpfs /tmp:size=64m,mode=1777 --cap-drop ALL --security-opt no-new-privileges:true --memory 512m --cpus 1 ghcr.io/icavalheiro/soso:latest
```

## Desarrollo local

Requiere .NET 10 SDK y Node.js 24. El navegador debe tener disponible y confiar en un certificado HTTPS de desarrollo para localhost. Si hace falta, ejecuta `dotnet dev-certs https --trust` por tu cuenta.

Define `Bootstrap__Email` y `Bootstrap__Password` (de 14 a 128 caracteres) en el entorno de tu terminal. `Bootstrap__Name` es opcional. Sosô no inicia si no hay un administrador activo y faltan credenciales de bootstrap válidas. La configuración de bootstrap nunca reemplaza cuentas existentes.

```cmd
npm --prefix src/Soso.Web ci
npm --prefix src/Soso.Web run build
xcopy /E /I /Y src\Soso.Web\dist src\Soso.Api\wwwroot
dotnet run --project src/Soso.Api
```

Abre https://localhost:7240. En Linux/macOS, reemplaza el comando `xcopy` por `mkdir -p src/Soso.Api/wwwroot && cp -r src/Soso.Web/dist/. src/Soso.Api/wwwroot/`.

Para recarga en caliente del frontend, ejecuta `npm --prefix src/Soso.Web run dev` junto con la API y abre la URL local de Vite. Su proxy apunta a la API en https://localhost:7240. La verificación del certificado se desactiva **solo en el proxy de desarrollo**. Usa el origen HTTPS de la API para probar el comportamiento de cookies seguras en distintos navegadores.

## Pruebas locales con Docker

Define `BOOTSTRAP_EMAIL` y un valor seguro para `BOOTSTRAP_PASSWORD` (de 14 a 128 caracteres) en `.env`, usando `.env.example` como referencia. Para esta configuración local no necesitas un dominio público, un túnel ni un certificado TLS.

```cmd
docker compose -f docker-compose.local.yml up -d --build
docker compose -f docker-compose.local.yml ps
```

Abre http://localhost:8061. El proyecto local y su volumen de datos son independientes de producción, y el puerto 8061 evita conflictos con el origen de producción en 8060. Define `SOSO_LOCAL_PORT` en `.env` para usar otro puerto disponible. Usa este archivo por separado con `-f`, no como una sobreescritura combinada con `docker-compose.yml`.

Esta configuración habilita `LocalHttp=true` solo en `Development`: las cookies de sesión y CSRF usan nombres locales y aceptan HTTP, mientras que la validación CSRF sigue habilitada. Producción ignora esta opción y conserva cookies `__Host-` exclusivas de HTTPS. El puerto publicado está enlazado únicamente a loopback. No expongas públicamente esta configuración de desarrollo. Los cambios de bootstrap no actualizan a un administrador ya creado en el volumen de datos local.

```cmd
docker compose -f docker-compose.local.yml logs -f soso
docker compose -f docker-compose.local.yml down
```

`down` conserva los datos locales. Agregar `--volumes` elimina la base de datos y las claves locales; no afecta el volumen de producción independiente.

## Producción

1. Configura tu host público en Cloudflare Tunnel. Mantén HTTPS público habilitado en Cloudflare y activa Always Use HTTPS; no se necesitan puertos públicos de entrada para la aplicación.
2. Crea `.env` usando `.env.example`, define `DOMAIN` con el host público del túnel y configura una contraseña de bootstrap segura y única. Nunca subas este archivo al repositorio; limita sus permisos en el sistema de archivos.
3. Ejecuta `docker compose up -d --build`. ASP.NET Core sirve la aplicación React, la API y MCP en `http://127.0.0.1:8060`; el contenedor escucha internamente en el puerto 8080. No se necesita un proxy inverso separado.
4. Configura tu servicio `cloudflared` en el host para usar `http://127.0.0.1:8060`. Inicia sesión en `https://<DOMAIN>`, crea cuentas de equipo desde Cuentas y agrega miembros en la configuración de cada tablero.

El único contenedor de la aplicación se publica exclusivamente en el puerto loopback del host **8060**. Se ejecuta sin privilegios de root, con sistema de archivos raíz de solo lectura, capacidades de Linux eliminadas, no-new-privileges y límites de recursos. La imagen incluye el frontend compilado en `wwwroot`; ASP.NET Core sirve archivos estáticos y rutas SPA junto con `/api` y `/mcp`. Cloudflare termina el TLS público; el tramo local entre el host y la aplicación usa HTTP. No cambies el enlace de loopback a una interfaz pública. Conserva el host público original en las solicitudes del túnel y no sobrescribas el esquema reenviado ni el encabezado de IP del cliente de Cloudflare. La API rechaza solicitudes reenviadas como HTTP. Kestrel transmite las respuestas MCP directamente, sin un proxy intermedio que las almacene.

Compose usa su red bridge estándar, sin IP fijas para contenedores ni subred personalizada. `CloudflareTunnel=true` acepta `CF-Connecting-IP` y `X-Forwarded-Proto` únicamente desde loopback o desde la gateway IPv4 exacta de Docker detectada al inicio. No confía en cadenas arbitrarias de IP reenviadas ni en redes completas. Si no hay una gateway única, el inicio falla de forma segura: define `TRUSTED_PROXY` en `.env` con la IP de origen exacta que ve el contenedor. Esa opción también reemplaza la detección de gateway para otras configuraciones de red de Docker. Otros procesos del host pueden acceder a este origen local, así que mantén el host confiable y actualizado.

Esta configuración de Compose supone que `cloudflared` se ejecuta en el host. Si el túnel corre en otro contenedor o en otra máquina, configura un origen privado accesible y su `TRUSTED_PROXY` exacto; `127.0.0.1` dentro de otro contenedor no se refiere a este host. No confíes en rangos amplios de IP ni expongas el origen públicamente. Fuera de esta implementación con Compose, deja `CloudflareTunnel` desactivado salvo que las solicitudes realmente provengan de Cloudflare Tunnel.

Para un túnel administrado localmente, agrega una regla de ingreso a su configuración existente:

```yaml
ingress:
  - hostname: soso.example.com
    service: http://127.0.0.1:8060
  - service: http_status:404
```

Para un túnel administrado desde el panel, configura allí el mismo host y origen HTTP. Mantén las credenciales del túnel fuera de este repositorio. No registres encabezados de autorización ni tokens de MCP.

LiteDB, las imágenes procesadas y las claves de cifrado de cookies se guardan en `soso-data`. Ejecuta **una sola réplica de la API**: LiteDB integrada no es una base de datos compartida o distribuida. Detén la aplicación antes de copiar su base de datos para obtener un respaldo consistente. Respalda el volumen completo, incluida la carpeta `keys`, y prueba las restauraciones. Usa cifrado en el disco del host y respaldos protegidos; las claves de protección de datos están protegidas por permisos del volumen, no por un almacén de claves independiente.

La configuración de bootstrap solo se usa cuando no hay un administrador activo. Cambia la contraseña inicial desde Perfil / Seguridad después de la instalación. No dejes la contraseña de ejemplo en producción. Cambiar una contraseña invalida todas las sesiones y tokens de MCP de esa cuenta. No se permite desactivar al último administrador activo.

## MCP / clientes LLM

La aplicación web sirve una guía de integración para LLM en `/llms.txt`, con configuración de clientes, permisos, argumentos de herramientas, manejo de revisiones y flujos de trabajo seguros para agentes. Su código fuente está en [src/Soso.Web/public/llms.txt](src/Soso.Web/public/llms.txt); Vite la incluye en la compilación del frontend y la aplicación la sirve como recurso estático público.

Crea un token en Perfil y configuración / MCP. Cada token comienza sin acceso a tableros. Selecciona sus Tableros asignados y haz clic en Guardar tableros para otorgar acceso; borra y guarda la selección para quitarlo. Los tokens existentes sin asignaciones tampoco tienen acceso a tableros. El acceso MCP es la intersección de estas asignaciones y los permisos actuales del propietario del token, incluso para administradores. Los cambios se aplican en las siguientes operaciones MCP. Los tokens se muestran una sola vez, se almacenan únicamente como hashes SHA-256, vencen en 30 días y se pueden revocar de inmediato. Configura un cliente compatible con **HTTP Streamable y encabezados bearer personalizados**:

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

El archivo `.vscode/mcp.json` incluido solicita el token sin guardarlo en el repositorio. Cambia la URL para tu instalación. Herramientas disponibles: `list_boards`, `get_board`, `create_ticket`, `create_tickets`, `move_ticket`, `update_ticket`, `update_tickets`, `add_comment`, `search_tickets`. `update_ticket` admite etiquetas, subtareas, estado de archivo y otras propiedades editables con verificación de revisiones. Las herramientas por lote insertan o actualizan atómicamente de 1 a 100 tareas en un tablero; si falla un elemento, se revierte todo el lote. `search_tickets` busca IDs y texto en tableros accesibles, con paginación y resultados archivados opcionales; pasa un ID completo o parcial como `query` para buscarlo. Las herramientas usan los permisos del tablero de quien las invoca y no pueden crear cuentas. Los endpoints MCP no aceptan cookies; los endpoints de la API del navegador no aceptan tokens bearer. Esta versión no implementa descubrimiento ni registro OAuth, por lo que los clientes que requieren ese flujo necesitan una puerta de enlace compatible. Trata el texto de tableros y comentarios como contexto no confiable; requiere aprobación humana para las escrituras de LLM.

La creación de elementos en MCP requiere una especificación de trabajo no vacía en `description` y de 1 a 8 `tags` de categorías de software, guardadas junto con el título y la columna. Los comentarios sirven para registrar avances, decisiones, bloqueos y resultados de verificación; no reemplazan la especificación. La finalización la determina únicamente una columna con `isDone=true`: conserva la descripción y mueve la tarea allí en vez de escribir «completada» mientras permanece en curso. La inicialización del servidor y las descripciones de herramientas incluyen estas instrucciones.

`update_ticket` y `update_tickets` usan parches parciales: envía la `revision` actual de la tarea y solo los campos que quieras cambiar. Los campos omitidos o nulos se conservan; los arreglos enviados reemplazan la colección correspondiente, `[]` limpia etiquetas/subtareas y `""` limpia la descripción. Usa `clearAssignee=true` o `clearDueDate=true` para quitar explícitamente esos valores anulables. Por ejemplo, `{"revision":3,"priority":"high"}` cambia la prioridad sin restablecer nada más. Los clientes existentes que limpian valores anulables con `null` deben cambiar a las opciones `clear` y actualizar el descubrimiento de herramientas MCP después del despliegue. El contrato de actualización completa de la API del navegador no cambia.

## Seguridad y validación

Las sesiones del navegador usan cookies HttpOnly, Secure y SameSite=Strict, vencen en 12 horas y verifican el estado de la cuenta en cada solicitud. Todas las mutaciones del navegador (incluido el inicio de sesión) requieren un token CSRF. El inicio de sesión limita solicitudes por IP y bloquea persistentemente una cuenta después de cinco intentos fallidos. En producción se rechazan solicitudes de texto claro, salvo comprobaciones de estado en loopback; se confía en un proxy configurado, se validan entradas, se limitan tamaños de solicitudes e imágenes, se eliminan metadatos de imágenes y los archivos adjuntos se sirven mediante rutas autenticadas. Las actualizaciones de tareas y tableros usan revisiones y devuelven 409 si hay conflictos.

Las rutas anónimas se limitan al inicio de sesión, la inicialización de CSRF, una respuesta mínima de estado y los recursos estáticos de la aplicación. Los recursos estáticos no contienen datos privados de tableros. Las personas con sesión iniciada pueden ver el directorio de nombres y avatares del equipo para compartir tableros; los correos y la configuración personal no se exponen allí.

```cmd
dotnet test tests/Soso.Api.Tests/Soso.Api.Tests.csproj
npm --prefix src/Soso.Web run build
npm --prefix src/Soso.Web run lint
dotnet list src/Soso.Api/Soso.Api.csproj package --vulnerable --include-transitive
npm --prefix src/Soso.Web audit
```

Las pruebas de interacción del navegador usan una API simulada, independiente de las pruebas de integración de la API real:

```cmd
cd src/Soso.Web
npx playwright install chromium
npm run test:e2e
```

Cubren el inicio de sesión, las etiquetas, los filtros por responsable y búsqueda, el arrastre entre columnas y sus animaciones, listas de tareas, comentarios e imágenes, archivar/restaurar, pantallas de cuentas y diseños de escritorio/móvil en temas claro/oscuro. Las capturas y trazas de fallos se guardan en `src/Soso.Web/test-results`. CI ejecuta ambas suites y compila la imagen de producción.

Las pruebas cubren el acceso anónimo, CSRF, cuentas exclusivas para administradores, aislamiento entre tableros, revisiones, etiquetas, archivo, validación de imágenes, invalidación de sesiones, bloqueo, llamadas autenticadas a herramientas MCP y rechazo de encabezados de esquema/IP de cliente reenviados desde pares no confiables. El alojamiento público requiere además actualizaciones del host, monitoreo, respaldos, gestión de secretos, actualizaciones de dependencias y revisión de seguridad humana. Ninguna aplicación puede garantizar seguridad incondicional.

## Licencia de la aplicación web

Se conserva la licencia GNU GPLv3 original del repositorio en `LICENSE`. LiteDB, Mantine, React y dnd-kit usan MIT; el SDK de C# para MCP usa Apache-2.0; Lucide usa ISC. La Licencia Dividida de Six Labors para ImageSharp concede Apache-2.0 para software de código abierto, el criterio aplicable a Sosô. Reevalúa esos términos antes de adoptarlo en un derivado comercial de código cerrado. Las licencias y avisos de dependencias están disponibles en sus paquetes publicados y repositorios de origen. No asumas que el logotipo tiene derechos de redistribución independientes de los otorgados por su propietario.
