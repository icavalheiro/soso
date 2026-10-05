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

Um espaço kanban leve para desenvolvimento de software, preparado para LLMs e MCP, criado com APIs mínimas do ASP.NET Core 10, LiteDB, React, TypeScript, Vite e Mantine.

## Espaço de trabalho

- Vários quadros privados com gerenciamento de membros pelo proprietário e colunas configuráveis.
- Cartões fáceis de arrastar, movimentação por teclado, animações sutis ao mover, busca e filtros por responsável e prioridade.
- Etiquetas integradas: Bug, Feature, Design, Docs, Refactor, Test, Chore e Research.
- Diálogos de tarefas com descrições, subtarefas, comentários, imagens, responsáveis e prazos.
- Colunas de conclusão, ações para arquivar/restaurar e visualizações separadas para itens concluídos/arquivados.
- Navegação recolhível, temas claro/escuro, inglês, português brasileiro e espanhol mexicano, fotos de perfil e preferências personalizadas privadas.
- Criação de contas, desativação e redefinição de senhas apenas por administradores. Não há cadastro público.
- Servidor MCP integrado, autenticado e via HTTP Streamable em `/mcp`.

## Executar com Docker (recomendado)

A forma recomendada de executar o Sosô é com Docker, usando a imagem pré-compilada do [GitHub Container Registry](https://github.com/icavalheiro/soso/pkgs/container/soso). Você não precisa do código-fonte, Docker Compose, SDK do .NET ou Node.js.

Crie um arquivo privado `soso.env` com as variáveis de ambiente abaixo, substituindo o hostname, e-mail e senha pelos seus valores. Não versione esse arquivo; restrinja as permissões no sistema de arquivos.

```dotenv
ASPNETCORE_ENVIRONMENT=Production
Bootstrap__Email=admin@example.com
Bootstrap__Password=replace-with-a-unique-strong-password
Bootstrap__Name=Administrator
AllowedHosts=soso.example.com;127.0.0.1;localhost
CloudflareTunnel=true
Logging__LogLevel__Default=Warning
```

`Bootstrap__Email` e `Bootstrap__Password` são obrigatórios na primeira inicialização; a senha deve ter de 14 a 128 caracteres. `Bootstrap__Name` é opcional. As configurações de bootstrap nunca substituem contas existentes. Defina `AllowedHosts` com seu hostname público, mantendo as entradas de loopback. `CloudflareTunnel=true` é para um Cloudflare Tunnel executado no host; se a detecção do gateway Docker falhar, adicione `TrustedProxy=<IP-exato-do-peer-do-túnel>` ao arquivo. Esses são nomes de variáveis da aplicação, não as substituições `BOOTSTRAP_EMAIL`, `BOOTSTRAP_PASSWORD` e `DOMAIN` usadas pelo Compose.

Baixe e execute a imagem com o Docker CLI:

```cmd
docker pull ghcr.io/icavalheiro/soso:latest
docker run -d --name soso --restart unless-stopped --env-file soso.env -p 127.0.0.1:8060:8080 -v soso-data:/app/data --read-only --tmpfs /tmp:size=64m,mode=1777 --cap-drop ALL --security-opt no-new-privileges:true --memory 512m --cpus 1 ghcr.io/icavalheiro/soso:latest
```

## Desenvolvimento local

Requer o SDK do .NET 10 e Node.js 24. Um certificado HTTPS de desenvolvimento para localhost deve estar disponível e ser confiável pelo navegador. Se necessário, execute `dotnet dev-certs https --trust`.

Defina `Bootstrap__Email` e `Bootstrap__Password` (de 14 a 128 caracteres) no ambiente do terminal. Opcional: `Bootstrap__Name`. O Sosô não inicia se não houver um administrador ativo e credenciais de bootstrap válidas. Contas existentes nunca são substituídas pelas configurações de bootstrap.

```cmd
npm --prefix src/Soso.Web ci
npm --prefix src/Soso.Web run build
xcopy /E /I /Y src\Soso.Web\dist src\Soso.Api\wwwroot
dotnet run --project src/Soso.Api
```

Acesse https://localhost:7240. No Linux/macOS, substitua o comando `xcopy` por `mkdir -p src/Soso.Api/wwwroot && cp -r src/Soso.Web/dist/. src/Soso.Api/wwwroot/`.

Para recarga automática do frontend, execute `npm --prefix src/Soso.Web run dev` junto com a API e acesse a URL local do Vite. O proxy aponta para a API em https://localhost:7240. A verificação do certificado é desativada **somente no proxy de desenvolvimento**. Use a origem HTTPS da API para testar o comportamento de cookies seguros entre navegadores.

## Testes locais com Docker

Defina `BOOTSTRAP_EMAIL` e uma `BOOTSTRAP_PASSWORD` forte, de 14 a 128 caracteres, no `.env`, usando `.env.example` como referência. Esta configuração local não precisa de domínio público, túnel ou certificado TLS.

```cmd
docker compose -f docker-compose.local.yml up -d --build
docker compose -f docker-compose.local.yml ps
```

Acesse http://localhost:8061. O projeto local e o volume de dados são separados da produção, e a porta 8061 evita conflitos com a origem de produção na 8060. Defina `SOSO_LOCAL_PORT` em `.env` para usar outra porta disponível. Use este arquivo sozinho com `-f`, sem combiná-lo como override com `docker-compose.yml`.

Esta configuração habilita `LocalHttp=true` somente em `Development`: os cookies de sessão e CSRF usam nomes locais e aceitam HTTP, enquanto a validação CSRF continua habilitada. A produção ignora esta opção e mantém cookies `__Host-` exclusivos para HTTPS. A porta publicada fica vinculada apenas ao loopback. Não exponha publicamente esta configuração de desenvolvimento. Alterações de bootstrap não atualizam um administrador já criado no volume de dados local.

```cmd
docker compose -f docker-compose.local.yml logs -f soso
docker compose -f docker-compose.local.yml down
```

`down` preserva os dados locais. Adicionar `--volumes` exclui o banco local e as chaves; isso não afeta o volume de produção separado.

## Produção

1. Configure seu hostname público no Cloudflare Tunnel. Mantenha o HTTPS público habilitado na Cloudflare e ative Always Use HTTPS; não são necessárias portas públicas de entrada para a aplicação.
2. Crie `.env` usando `.env.example`, defina `DOMAIN` como o hostname público do túnel e configure uma senha de bootstrap forte e exclusiva. Nunca versione esse arquivo; restrinja suas permissões no sistema de arquivos.
3. Execute `docker compose up -d --build`. O ASP.NET Core serve a aplicação React, a API e o MCP em `http://127.0.0.1:8060`; o container escuta internamente na porta 8080. Não é necessário um proxy reverso separado.
4. Aponte o serviço `cloudflared` executado no host para `http://127.0.0.1:8060`. Entre em `https://<DOMAIN>`, crie contas da equipe em Accounts e adicione membros nas configurações de cada quadro.

O único container da aplicação é publicado somente na porta de loopback **8060** do host. Ele é executado sem root, com sistema de arquivos raiz somente para leitura, capacidades do Linux removidas, `no-new-privileges` e limites de recursos. A imagem inclui a compilação do frontend em `wwwroot`; o ASP.NET Core serve arquivos estáticos e rotas SPA junto com `/api` e `/mcp`. A Cloudflare encerra o TLS público; o trecho local, do túnel até a aplicação, usa HTTP. Não altere o vínculo de loopback para uma interface pública. Mantenha o hostname público inalterado nas solicitações do túnel e não sobrescreva o esquema encaminhado nem o cabeçalho de IP do cliente da Cloudflare. A API rejeita solicitações encaminhadas como HTTP. O Kestrel transmite respostas MCP diretamente, sem um proxy intermediário que armazene respostas em buffer.

O Compose usa a rede bridge padrão, sem IPs fixos de container ou sub-rede personalizada. `CloudflareTunnel=true` aceita `CF-Connecting-IP` e `X-Forwarded-Proto` somente de loopback ou do gateway IPv4 exato do host Docker detectado na inicialização. Ele não confia em cadeias arbitrárias de IP encaminhado nem em redes inteiras. Se não houver um gateway único, a inicialização falha de forma segura: defina `TRUSTED_PROXY` em `.env` com o IP exato de origem visto pelo container. Essa configuração também substitui a detecção do gateway em outros ambientes de rede Docker. Outros processos do host podem acessar esta origem local; mantenha o host confiável e atualizado.

Esta configuração do Compose pressupõe que `cloudflared` seja executado no host. Se o túnel for executado em outro container ou máquina, configure uma origem privada acessível e o `TRUSTED_PROXY` exato; `127.0.0.1` dentro de outro container não se refere a este host. Não confie em intervalos amplos de IP nem exponha a origem publicamente. Fora desta implantação com Compose, mantenha `CloudflareTunnel` desabilitado, a menos que as solicitações realmente venham do Cloudflare Tunnel.

Para um túnel gerenciado localmente, adicione uma regra de ingresso à configuração existente:

```yaml
ingress:
  - hostname: soso.example.com
    service: http://127.0.0.1:8060
  - service: http_status:404
```

Para um túnel gerenciado pelo dashboard, configure nele o mesmo hostname e a origem HTTP. Mantenha as credenciais do túnel fora deste repositório. Não registre cabeçalhos de autorização nem tokens MCP.

O LiteDB, as imagens processadas e as chaves de criptografia de cookies são persistidos em `soso-data`. Execute **uma única réplica da API**: o LiteDB embarcado não é um banco compartilhado/distribuído. Pare a aplicação antes de copiar o banco para obter um backup consistente. Faça backup de todo o volume de dados, incluindo `keys`, e teste as restaurações. Use criptografia de disco no host e backups protegidos; as chaves de proteção de dados são protegidas pelas permissões do volume, não por um cofre de chaves separado.

As configurações de bootstrap são usadas somente quando não há um administrador ativo. Após a configuração inicial, altere a senha em Profile / Security. Não mantenha a senha de exemplo em produção. Alterar uma senha invalida todas as sessões e tokens MCP dessa conta. A desativação do último administrador ativo é rejeitada.

## Clientes MCP / LLM

A aplicação web disponibiliza um guia de integração com LLM em `/llms.txt`, que cobre configuração de clientes, permissões, argumentos de ferramentas, controle de revisão e fluxos seguros para agentes. A origem está em [src/Soso.Web/public/llms.txt](src/Soso.Web/public/llms.txt); o Vite o inclui na compilação do frontend e a aplicação o serve como recurso estático público.

Crie um token em Profile & settings / MCP. Cada token começa sem acesso a quadros. Selecione os quadros em Assigned boards e clique em Save boards para conceder acesso; limpe e salve a seleção para remover o acesso. Tokens existentes sem atribuições também não têm acesso a quadros. O acesso MCP é a interseção dessas atribuições com as permissões atuais do proprietário do token em cada quadro, inclusive para administradores. As alterações passam a valer nas operações MCP seguintes. Os tokens são exibidos uma única vez, armazenados apenas como hashes SHA-256, expiram em 30 dias e podem ser revogados imediatamente. Configure um cliente compatível com **HTTP Streamable e cabeçalhos bearer personalizados**:

```json
{
  "servers": {
    "soso": {
      "type": "http",
      "url": "https://soso.example.com/mcp",
      "headers": { "Authorization": "Bearer <seu-token-pessoal>" }
    }
  }
}
```

O arquivo `.vscode/mcp.json` incluído solicita o token sem gravá-lo no repositório. Altere a URL para sua implantação. Ferramentas disponíveis: `list_boards`, `get_board`, `create_ticket`, `create_tickets`, `move_ticket`, `update_ticket`, `update_tickets`, `add_comment`, `search_tickets`. `update_ticket` aceita etiquetas, subtarefas, estado de arquivamento e outras propriedades editáveis com verificação de revisão. As ferramentas em lote inserem ou atualizam atomicamente de 1 a 100 tarefas em um quadro; qualquer item inválido reverte o lote inteiro. `search_tickets` pesquisa IDs e texto em quadros acessíveis, com paginação e opção para incluir itens arquivados; passe um ID completo ou parcial como `query` para encontrá-lo. As ferramentas respeitam as permissões de quadro do usuário e não podem criar contas. Cookies não são aceitos nos endpoints MCP; tokens bearer não são aceitos nos endpoints da API do navegador. Esta versão não implementa descoberta/registro OAuth, portanto clientes que exigem esse fluxo precisam de um gateway compatível. Trate textos e comentários dos quadros como contexto não confiável; exija aprovação humana para gravações feitas por LLMs.

A criação via MCP exige uma especificação de trabalho não vazia em `description` e de 1 a 8 `tags` de categorias de software, salvas junto com o título e a coluna. Comentários servem para registrar progresso, decisões, bloqueios e resultados de verificação; eles não substituem a especificação. A conclusão é determinada somente por uma coluna com `isDone=true`: preserve a descrição e mova a tarefa para essa coluna em vez de escrever "concluído" enquanto ela permanece em andamento. A inicialização do servidor e as descrições das ferramentas incluem estas instruções.

`update_ticket` e `update_tickets` usam patches parciais: envie a `revision` atual da tarefa e somente os campos que deseja alterar. Campos omitidos ou nulos são preservados; arrays enviados substituem a coleção inteira, `[]` limpa etiquetas/subtarefas e `""` limpa a descrição. Use `clearAssignee=true` ou `clearDueDate=true` para remover explicitamente esses valores anuláveis. Por exemplo, `{"revision":3,"priority":"high"}` altera a prioridade sem redefinir qualquer outro campo. Clientes existentes que limpam valores anuláveis usando `null` devem migrar para as flags de limpeza e atualizar a descoberta das ferramentas MCP após a implantação. O contrato de atualização completa da API do navegador não foi alterado.

## Segurança e validação

As sessões do navegador usam cookies HttpOnly, Secure e SameSite=Strict, expiram em 12 horas e verificam o estado da conta em cada solicitação. Todas as alterações feitas pelo navegador (incluindo o login) exigem um token CSRF. O login tem limitação por IP e bloqueio persistente da conta após cinco falhas. Em produção, solicitações sem criptografia são rejeitadas, exceto verificações de saúde via loopback; um proxy configurado é considerado confiável; entradas são validadas, tamanhos de solicitações/imagens são limitados, metadados de imagens são removidos e anexos são servidos por rotas autenticadas. Atualizações de tarefas e quadros usam revisões e retornam 409 em caso de conflito.

As rotas anônimas se limitam ao login, inicialização CSRF, uma resposta mínima de saúde e recursos estáticos da aplicação. Os recursos estáticos não contêm dados privados dos quadros. Usuários autenticados podem ver o nome e avatar da equipe para compartilhar quadros; e-mails e configurações pessoais não são expostos nessa área.

```cmd
dotnet test tests/Soso.Api.Tests/Soso.Api.Tests.csproj
npm --prefix src/Soso.Web run build
npm --prefix src/Soso.Web run lint
dotnet list src/Soso.Api/Soso.Api.csproj package --vulnerable --include-transitive
npm --prefix src/Soso.Web audit
```

Os testes de interação do navegador usam uma API simulada, separada dos testes de integração com a API real:

```cmd
cd src/Soso.Web
npx playwright install chromium
npm run test:e2e
```

Eles cobrem login, etiquetas de software, filtros por responsável/busca, arrastar entre colunas e animações, listas de verificação/comentários/imagens, arquivamento/restauração, telas de contas e layouts para desktop/celular nos temas claro/escuro. Capturas de tela e traces de falha são armazenados em `src/Soso.Web/test-results`. O CI executa as duas suítes e compila a imagem de produção.

Os testes cobrem acesso anônimo, CSRF, contas exclusivas de administradores, isolamento entre quadros, revisões, etiquetas, arquivamento, validação de imagens, invalidação de sessões, bloqueio, chamadas autenticadas a ferramentas MCP e rejeição de cabeçalhos encaminhados de esquema/IP de cliente vindos de peers não confiáveis. A hospedagem pública ainda exige atualização do host, monitoramento, backups, gerenciamento de segredos, atualização de dependências e análise humana de segurança. Nenhuma aplicação pode prometer segurança incondicional.

## Licença da aplicação web

A licença original GNU GPLv3 do repositório foi mantida em `LICENSE`. LiteDB, Mantine, React e dnd-kit usam MIT; o SDK C# do MCP usa Apache-2.0; Lucide usa ISC. A Split License do Six Labors ImageSharp concede Apache-2.0 para uso em software de código aberto, critério aplicável ao Sosô. Reavalie esses termos antes de adotá-lo em um derivado comercial de código fechado. As licenças e avisos das dependências continuam disponíveis nos pacotes publicados e nos repositórios de origem. Não presuma que o logo tenha direitos independentes de redistribuição além daqueles concedidos por seu proprietário.
