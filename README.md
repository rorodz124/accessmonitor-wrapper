# AccessMonitor Wrapper API

## O que é
API em C# .NET 9 que funciona como wrapper para o [AccessMonitor](https://github.com/amagovpt/accessmonitor-docker), uma ferramenta open source da AMA para avaliar a acessibilidade de páginas web segundo critérios WCAG.

O projeto expõe dois modos de validação:
- **Validar por URL** — para páginas já publicadas na internet
- **Validar por HTML** — para páginas ainda não publicadas, validando o código HTML diretamente

## Como funciona
```text
Cliente → POST /api/validate       → Wrapper API → AccessMonitor Docker → Relatório JSON
Cliente → POST /api/validate/html  → Wrapper API → AccessMonitor Docker → Erros e Avisos JSON
```

### Fluxo — Validar por URL
1. O cliente envia um URL.
2. A API valida o URL e converte-o para Base64.
3. A API chama o AccessMonitor (`GET /amp/eval/{urlBase64}`) com o header `Referer`.
4. O AccessMonitor avalia a acessibilidade da página.
5. O relatório JSON completo é devolvido ao cliente.

### Fluxo — Validar por HTML
1. O cliente envia código HTML em bruto.
2. A API envia esse HTML diretamente ao AccessMonitor (`POST /amp/eval/html`).
3. O AccessMonitor avalia a acessibilidade do HTML.
4. A API filtra a resposta e devolve **apenas erros e avisos** (o que passou não é incluído).

---

## Endpoints
### POST /api/validate — Validar por URL

```http
POST /api/validate
Content-Type: application/json
```

```json
{
  "url": "https://example.com/"
}
```

#### Respostas
| Código | Significado |
|--------|-------------|
| `200 OK` | Relatório de acessibilidade devolvido com sucesso |
| `400 Bad Request` | URL em falta, vazio, ou inválido |
| `502 Bad Gateway` | Erro de comunicação com o AccessMonitor |
| `504 Gateway Timeout` | O AccessMonitor não respondeu a tempo |

---

### POST /api/validate/html — Validar por HTML
```http
POST /api/validate/html
Content-Type: application/json
```

```json
{
  "html": "<html><body><p>Conteúdo da página</p></body></html>"
}
```

#### Respostas
| Código | Significado |
|--------|-------------|
| `200 OK` | Lista de erros e avisos devolvida com sucesso (vazia se não houver problemas) |
| `400 Bad Request` | HTML em falta ou vazio |
| `502 Bad Gateway` | Erro de comunicação com o AccessMonitor |
| `504 Gateway Timeout` | O AccessMonitor não respondeu a tempo |

---

## Frontend
O projeto inclui duas páginas HTML estáticas em `wwwroot/`:

### ValidateUrl.html — Validação por URL

Acessível em `http://localhost:5296/ValidateUrl.html`

O caminho antigo `http://localhost:5296/validate-url.html` continua disponível por compatibilidade.

- Campo para inserir um URL
- Botão para submeter
- Resultado apresentado em card com resumo:
  - **Score** (pontuação geral)
  - **Passed** (critérios que passaram)
  - **Warnings** (avisos)
  - **Failed** (erros críticos)
- Botão "Mostrar relatório completo" que apresenta erros, warnings, acertos e o JSON técnico

### ValidateHtml.html — Validação por HTML

Acessível em `http://localhost:5296/ValidateHtml.html`

- Campo de texto rico (suporta HTML, tabelas, código, etc.)
- **Validação automática a cada 30 segundos** enquanto a pessoa edita — chama `POST /api/validate/html` automaticamente e atualiza os alertas sem interromper a edição
- Apresenta **apenas erros e avisos** — o que passou corretamente não é mostrado
- Botão para validar manualmente a qualquer momento

---

## Estrutura do Projeto
```text
AccessMonitorWrapper/
├── Controllers/
│   └── AccessibilityController.cs    # Endpoints POST /api/validate e POST /api/validate/html
├── Services/
│   └── AccessMonitorService.cs       # Comunicação com o AccessMonitor
├── Models/
│   ├── ValidateRequest.cs            # Modelo do pedido por URL { url }
│   └── ValidateHtmlRequest.cs        # Modelo do pedido por HTML { html }
├── Program.cs                        # Configuração e DI
├── Dockerfile                        # Build multi-stage da API (porta 3000)
├── docker-compose.yml                # Orquestração dos dois serviços
├── appsettings.json                  # Configuração base
├── appsettings.Development.json      # Configuração de desenvolvimento
└── wwwroot/
    ├── ValidateUrl.html              # Página de validação por URL
    ├── validate-url.html             # Alias de compatibilidade
    └── ValidateHtml.html             # Página de validação por HTML
```

## O que falta implementar
- [ ] Remover `wwwroot/index.html`
---

## Pré-requisitos
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/) (para correr o AccessMonitor)
- [Git](https://git-scm.com/)
---

## Como correr — VS Code
### 1. Configurar access monitor
```powershell
cd C:\Projetos\accessmonitor-docker
docker run --env-file .env -p 3000:3000 accessmonitor-docker
```

```powershell
docker ps
docker update --restart unless-stopped <id-do-contentor>
```

### 2.arrancar o projeto
```powershell
cd C:\Projetos\AccessMonitorWrapper
dotnet run
```

Em desenvolvimento, o Swagger UI está em `http://localhost:5296/swagger`.

### 4. Abrir as páginas no browser

- Validação por URL: `http://localhost:5296/ValidateUrl.html`
- Validação por HTML: `http://localhost:5296/ValidateHtml.html`

---

## Configuração da Wrapper API

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
- O AccessMonitor expõe a porta `3000` e requer sempre o header `Referer` — sem ele responde `403 Forbidden`.
- O URL enviado ao AccessMonitor é codificado em **Base64** no path: `GET /amp/eval/{urlBase64}`.
- O timeout do HttpClient está definido para **120 segundos** (a avaliação pode demorar).
- Na validação por HTML, a API filtra a resposta e devolve apenas os itens com erros ou avisos — os critérios que passaram são descartados.
- A Wrapper API corre na porta `5296` em desenvolvimento (`dotnet run`) e na porta `3000` em Docker.
- A auto-validação no editor HTML (quando implementada) dispara a cada **30 segundos** desde a última alteração, sem bloquear a edição.