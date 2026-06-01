# AccessMonitor Wrapper API

## Contexto

Este projeto consiste numa API em C# .NET Core que funciona como wrapper para o AccessMonitor, uma ferramenta open source da AMA usada para avaliar a acessibilidade de paginas web segundo criterios WCAG.

O objetivo e expor uma API simples para clientes internos, escondendo os detalhes tecnicos do AccessMonitor, que corre separadamente em Docker.

Fluxo geral:

```text
Cliente -> API C# -> AccessMonitor Docker -> Relatorio de acessibilidade
```

## AccessMonitor

O AccessMonitor usado neste projeto vem do repositorio:

```text
https://github.com/amagovpt/accessmonitor-docker
```

Comandos base:

```powershell
git clone https://github.com/amagovpt/accessmonitor-docker
cd accessmonitor-docker
Copy-Item .env.example .env
docker build -t accessmonitor-docker .
docker run --env-file .env -p 3000:3000 accessmonitor-docker
```

Depois de arrancar, fica disponivel em:

```text
http://localhost:3000
```

## Contrato Descoberto

Endpoint correto para validar uma pagina por URL:

```http
GET /amp/eval/{urlEmBase64}
```

Rotas descobertas:

```text
GET  /health
GET  /amp/eval/:url
POST /amp/eval/html
```

Exemplo de pedido final, depois de converter `https://example.com/` para Base64:

```text
http://localhost:3000/amp/eval/aHR0cHM6Ly9leGFtcGxlLmNvbS8=
```

## Header Referer

O AccessMonitor pode exigir o header `Referer`, dependendo da variavel de ambiente `REFERER`.

```http
Referer: http://localhost:3000
```

Sem este header, a API pode responder com `403 Forbidden`.

## Teste Manual

Exemplo de teste no PowerShell:

```powershell
$url = "https://example.com/"
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($url))

Invoke-RestMethod `
  -Uri "http://localhost:3000/amp/eval/$encoded" `
  -Headers @{
    Referer = "http://localhost:3000"
  }
```

Health check:

```powershell
Invoke-RestMethod "http://localhost:3000/health"
```

## API C# a Construir

A nossa API vai expor:

```http
POST /api/validate
Content-Type: application/json
```

```json
{
  "url": "https://example.com/"
}
```

Internamente, a API deve:

1. Receber a URL.
2. Validar se a URL e valida.
3. Converter a URL para Base64.
4. Chamar `GET /amp/eval/{urlBase64}` no AccessMonitor.
5. Enviar o header `Referer`.
6. Validar status code e content type da resposta.
7. Devolver o relatorio JSON ao cliente.
8. Tratar falhas, timeouts e respostas inesperadas.

## Estrutura do Projeto

Estrutura:

```text
AccessMonitorWrapper/
|-- Controllers/
|   |-- AccessibilityController.cs
|-- Services/
|   |-- AccessMonitorService.cs
|-- Models/
|   |-- ValidateRequest.cs
|-- Program.cs
|-- Dockerfile
|-- docker-compose.yml
```

Modelo inicial em `Models/ValidateRequest.cs`:

```csharp
namespace AccessMonitorWrapper.Models;

public class ValidateRequest
{
    public string? Url { get; set; }
}
```

## Docker Compose

O projeto devera subir dois servicos:

- `accessmonitor`: API original do AccessMonitor
- `wrapper-api`: API C# deste projeto

Dentro do Docker Compose, a API C# deve chamar o AccessMonitor pelo nome do servico:

```text
http://accessmonitor:3000
```

Fluxo:

```text
Cliente -> http://localhost:8080/api/validate -> wrapper-api -> http://accessmonitor:3000
```

## Configuracao

Variaveis de ambiente previstas:

```text
AccessMonitor__BaseUrl=http://accessmonitor:3000
AccessMonitor__Referer=http://localhost:3000
```

Em desenvolvimento local fora de Docker Compose:

```text
AccessMonitor__BaseUrl=http://localhost:3000
AccessMonitor__Referer=http://localhost:3000
```

## Estado Atual

Ja foi feito:

- AccessMonitor executado em Docker.
- Rotas reais descobertas atraves dos logs.
- Endpoint correto identificado: `GET /amp/eval/{urlBase64}`.
- Necessidade do header `Referer` identificada.
- Modelo inicial `ValidateRequest` preparado.

Ainda falta:

- Criar `AccessMonitorService`.
- Criar `AccessibilityController`.
- Configurar `Program.cs`.
- Criar `Dockerfile`.
- Criar `docker-compose.yml`.
- Testar o fluxo completo.
- Inicializar Git e organizar commits.