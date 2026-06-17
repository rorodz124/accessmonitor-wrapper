# AccessMonitor Wrapper API

![.NET](https://img.shields.io/badge/.NET-9.0-blue)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-API-lightgrey)
![Docker](https://img.shields.io/badge/Docker-ready-2496ED)

Wrapper HTTP sobre o [AccessMonitor](https://accessmonitor.acessibilidade.gov.pt/), que expõe uma API REST simples para validar a acessibilidade de páginas web - por URL ou por HTML direto - e devolve o relatório gerado pelo AccessMonitor.

O projeto corre em dois contentores: o **AccessMonitor** (motor de validação) e o **wrapper** (API), ligados em rede interna via Docker Compose.

## Como Iniciar

Clone o repositório:

```bash
git clone https://github.com/rorodz124/accessmonitor-wrapper.git
cd accessmonitor-wrapper
```

Inicia com Docker:

```bash
docker compose up --build -d
```

A aplicação fica disponível em **http://localhost:5297**

A Swagger UI fica disponível em **http://localhost:5297/swagger**

## Configuração

As variáveis de configuração do wrapper são definidas no `docker-compose.yml` ou em `appsettings.json`:

| Variável | Descrição | Valor por defeito |
|----------|-----------|-------------------|
| `AccessMonitor__BaseUrl` | URL base do serviço AccessMonitor | `http://localhost:3000` |
| `AccessMonitor__Referer` | Header Referer enviado nos pedidos ao AccessMonitor | `http://localhost:3000` |
| `ASPNETCORE_ENVIRONMENT` | Ambiente da aplicação | `Production` |

## Stack

- **Backend:** C# + ASP.NET Core 9
- **Documentação:** Swagger / OpenAPI
- **Acessibilidade:** AccessMonitor (serviço externo, corre em contentor próprio)

## API Endpoints

### Validação

| Método | Endpoint | Descrição |
|--------|----------|-----------|
| `POST` | `/api/validate` | Valida a acessibilidade de uma página por URL |
| `POST` | `/api/validate/html` | Valida a acessibilidade de HTML submetido diretamente |

#### `POST /api/validate`

Recebe uma URL absoluta (`http` ou `https`) e devolve o relatório de acessibilidade.

```json
{ "url": "https://exemplo.pt" }
```

#### `POST /api/validate/html`

Recebe HTML em bruto e devolve o relatório de acessibilidade.

```json
{ "html": "<!DOCTYPE html><html>...</html>" }
```

### Respostas

| Código | Descrição |
|--------|-----------|
| `200` | Relatório JSON devolvido com sucesso |
| `400` | Pedido inválido (URL em falta, formato inválido, HTML vazio) |
| `502` | O AccessMonitor devolveu um erro ou resposta inválida |
| `504` | O AccessMonitor não respondeu a tempo (timeout: 180s) |