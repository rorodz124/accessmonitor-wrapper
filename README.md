# AccessMonitor Wrapper API

## O que é

API em C# .NET 9 que funciona como wrapper para o [AccessMonitor](https://github.com/amagovpt/accessmonitor-docker), uma ferramenta open source da AMA para avaliar a acessibilidade de páginas web segundo critérios WCAG.

O objetivo é expor uma API simples onde o utilizador envia um URL e recebe o relatório de acessibilidade.

## Como funciona

```text
Cliente → POST /api/validate → Wrapper API → AccessMonitor Docker → Relatório JSON
```

1. O cliente envia um URL.
2. A API valida o URL e converte-o para Base64.
3. A API chama o AccessMonitor (`GET /amp/eval/{urlBase64}`) com o header `Referer`.
4. O AccessMonitor avalia a acessibilidade da página.
5. O relatório JSON é devolvido ao cliente.

## Endpoint

```http
POST /api/validate
Content-Type: application/json
```

```json
{
  "url": "https://example.com/"
}
```

### Respostas

| Código | Significado |
|--------|-------------|
| `200 OK` | Relatório de acessibilidade devolvido com sucesso |
| `400 Bad Request` | URL em falta, vazio, ou inválido |
| `502 Bad Gateway` | Erro de comunicação com o AccessMonitor |
| `504 Gateway Timeout` | O AccessMonitor não respondeu a tempo |

## Estrutura do Projeto

```text
AccessMonitorWrapper/
├── Controllers/
│   └── AccessibilityController.cs    # Endpoint POST /api/validate
├── Services/
│   └── AccessMonitorService.cs       # Comunicação com o AccessMonitor
├── Models/
│   └── ValidateRequest.cs            # Modelo do pedido { url }
├── Program.cs                        # Configuração e DI
├── Dockerfile                        # Build multi-stage da API
├── docker-compose.yml                # Orquestração dos dois serviços
├── appsettings.json                  # Configuração base
└── appsettings.Development.json      # Configuração de desenvolvimento
```

## Pré-requisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker](https://www.docker.com/) (para correr o AccessMonitor)

## Como correr

### 1. Arrancar o AccessMonitor em Docker

```powershell
git clone https://github.com/amagovpt/accessmonitor-docker
cd accessmonitor-docker
Copy-Item .env.example .env
docker build -t accessmonitor-docker .
docker run --env-file .env -p 3000:3000 accessmonitor-docker
```

Verificar que está a correr:

```powershell
Invoke-RestMethod "http://localhost:3000/health"
```

### 2. Arrancar a Wrapper API

```powershell
cd AccessMonitorWrapper
dotnet run
```

A API fica disponível em `http://localhost:8080` (ou a porta indicada no output).

A página de teste simples estará disponível na raiz do serviço, por exemplo `http://localhost:8080/`. Nessa página basta inserir um URL e clicar em Avaliar para obter o relatório.

Em desenvolvimento, a Swagger UI também estará disponível em `http://localhost:8080/swagger` para ver a documentação do endpoint.

### 3. Testar

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/validate" `
  -ContentType "application/json" `
  -Body '{"url":"https://example.com/"}'
```

Ou usar o ficheiro `AccessMonitorWrapper.http` no Visual Studio / VS Code com a extensão REST Client.

### Alternativa: Docker Compose (tudo junto)

> **Nota:** Requer que o repo `accessmonitor-docker` esteja clonado na pasta ao lado deste projeto.

```powershell
docker compose up --build
```

Isto arranca os dois serviços:

- `accessmonitor` na porta `3000`
- `wrapper-api` na porta `8080`

## Configuração

A API lê a configuração do AccessMonitor via `appsettings.json` ou variáveis de ambiente:

| Variável | Default | Descrição |
|----------|---------|-----------|
| `AccessMonitor:BaseUrl` | `http://localhost:3000` | URL base do AccessMonitor |
| `AccessMonitor:Referer` | `http://localhost:3000` | Header Referer exigido pelo AccessMonitor |

Em Docker Compose, estas variáveis são definidas automaticamente:

```text
AccessMonitor__BaseUrl=http://accessmonitor:3000
AccessMonitor__Referer=http://localhost:3000
```

## Notas técnicas

- O AccessMonitor espera o URL codificado em **Base64** no path: `GET /amp/eval/{urlBase64}`.
- O header `Referer` é obrigatório — sem ele, o AccessMonitor responde `403 Forbidden`.
- O timeout do HttpClient está definido para **120 segundos** (a avaliação pode demorar).