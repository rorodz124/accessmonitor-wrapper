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
2. A API valida o URL e codifica-o com percent-encoding (`Uri.EscapeDataString`).
3. A API chama o AccessMonitor (`GET /amp/eval/{urlPercentEncoded}`) com o header `Referer`.
4. O AccessMonitor avalia a acessibilidade da página.
5. O relatório JSON completo é devolvido ao cliente (sem o campo `pagecode`, que pode ter vários MB).

### Fluxo — Validar por HTML
1. O cliente envia código HTML em bruto.
2. A API envia esse HTML diretamente ao AccessMonitor (`POST /amp/eval/html`).
3. O AccessMonitor avalia a acessibilidade do HTML.
4. A API devolve a resposta do AccessMonitor tal como está.

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
| `200 OK` | Resposta do AccessMonitor devolvida com sucesso |
| `400 Bad Request` | HTML em falta ou vazio |
| `502 Bad Gateway` | Erro de comunicação com o AccessMonitor |
| `504 Gateway Timeout` | O AccessMonitor não respondeu a tempo |

---

## Frontend
O projeto inclui duas páginas HTML estáticas em `wwwroot/`:

### ValidateUrl.html — Validação por URL

Acessível em `http://localhost:5296/ValidateUrl.html`

- Campo para inserir um URL
- Botão para submeter
- Resultado apresentado em card com resumo:
  - **Score** (pontuação geral de 0 a 10, calculada pelo AccessMonitor)
  - **Aceitáveis** (critérios WCAG que passaram)
  - **Para ver manualmente** (avisos)
  - **Não aceitáveis** (erros críticos)
  - Breakdown por nível WCAG (A / AA / AAA)
- Botão "Mostrar relatório completo" que apresenta o detalhe de cada critério com os elementos afetados

### ValidateHtml.html — Validação por HTML

Acessível em `http://localhost:5296/ValidateHtml.html`

- Campo de texto (suporta HTML, tabelas, código, etc.)
- **Validação automática a cada 30 segundos** desde a última alteração — chama `POST /api/validate/html` automaticamente e atualiza os alertas sem interromper a edição
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
│   └── ValidateRequestHtml.cs        # Modelo do pedido por HTML { html }
├── Program.cs                        # Configuração e DI
├── Dockerfile                        # Build multi-stage da API (porta 5296)
├── docker-compose.yml                # Orquestração dos dois serviços
├── appsettings.json                  # Configuração base
├── appsettings.Development.json      # Configuração de desenvolvimento
└── wwwroot/
    ├── ValidateUrl.html              # Página de validação por URL
    └── ValidateHtml.html             # Página de validação por HTML
```

---

## Pré-requisitos
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/) (para correr o AccessMonitor)
- [Git](https://git-scm.com/)

---

## Como correr — VS Code
### 1. Configurar o AccessMonitor
```powershell
cd C:\Projetos\accessmonitor-docker
docker run --env-file .env -p 3000:3000 accessmonitor-docker
```

```powershell
docker ps
docker update --restart unless-stopped <id-do-contentor>
```

### 2. Arrancar o projeto
```powershell
cd C:\Projetos\AccessMonitorWrapper
dotnet run
```

### 3. Verificar o Swagger
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
- O URL enviado ao AccessMonitor é codificado com **percent-encoding** no path: `GET /amp/eval/{urlPercentEncoded}`.
- O timeout do HttpClient está definido para **180 segundos** (a avaliação pode demorar bastante dependendo da página).
- Na validação por URL, a API remove o campo `pagecode` da resposta — é o HTML cru da página avaliada e pode ter vários MB sem utilidade para o cliente.
- A Wrapper API corre na porta `5296` em desenvolvimento (`dotnet run`) e também na porta `5296` em Docker.
- A auto-validação no editor HTML dispara a cada **30 segundos** desde a última alteração, sem bloquear a edição.