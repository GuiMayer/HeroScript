# API Conventions

**Última atualização:** 2026-05-08

---

## Visão Geral

Este documento define as convenções, padrões e boas práticas para a API REST do HeroScript. Seguir estas convenções garante consistência, previsibilidade e facilidade de uso da API.

## Princípios Gerais

### 1. RESTful Design

A API segue princípios REST quando aplicável:
- Recursos são substantivos (não verbos)
- Métodos HTTP expressam ações (GET, POST, PUT, DELETE)
- URLs são hierárquicos e descritivos
- Respostas usam códigos HTTP apropriados

### 2. Stateless

Cada requisição contém todas as informações necessárias:
- Sem dependência de estado de sessão no servidor
- IDs de recursos são passados explicitamente
- Autenticação via headers (futuro)

### 3. Data-Driven

Configurações e regras são dados (JSON), não código:
- Fórmulas matemáticas em JSON
- Definições de poderes, inimigos, raças em JSON
- Sistema de herança delta para configs

---

## Estrutura de URLs

### Base URL

```
http://localhost:5260/api
```

**Nota:** Versionamento (`/api/v1/`) será adicionado apenas quando tivermos o primeiro MVP em beta.

### Padrões de Nomenclatura

**Recursos (substantivos, plural):**
```
/api/configs
/api/operations
/api/events
/api/powers
/api/companions
```

**Recursos específicos (com ID):**
```
/api/config/{name}
/api/power/{name}
/api/combat/{combatId}
/api/run/{runId}
```

**Sub-recursos:**
```
/api/config/{name}/chain
/api/power/{name}/tree
/api/run/{runId}/state
/api/combat/{combatId}/history
```

**Ações (verbos quando necessário):**
```
POST /api/config/{name}/validate
POST /api/config/{name}/load
POST /api/damage/calculate
POST /api/seed/generate
```

### Convenções de Nomenclatura

- **Recursos:** plural, lowercase, separados por hífen se necessário
  - ✅ `/api/companions`
  - ✅ `/api/card-selection`
  - ❌ `/api/Companion`
  - ❌ `/api/cardSelection`

- **IDs:** kebab-case ou identificadores naturais
  - ✅ `combat-123`
  - ✅ `FIREBALL`
  - ✅ `human`
  - ❌ `Combat_123`

- **Query parameters:** camelCase
  - ✅ `?category=COMBAT&limit=50`
  - ❌ `?Category=COMBAT&Limit=50`

---

## Métodos HTTP

### GET - Consulta

Obtém recursos sem modificar estado.

```http
GET /api/powers
GET /api/power/FIREBALL
GET /api/run/{runId}/state
```

**Características:**
- Idempotente (múltiplas chamadas = mesmo resultado)
- Sem efeitos colaterais
- Pode ser cacheado

### POST - Criação ou Ação

Cria novos recursos ou executa ações.

```http
POST /api/run/start
POST /api/combat/{combatId}/action
POST /api/damage/calculate
```

**Características:**
- Não idempotente (múltiplas chamadas podem criar múltiplos recursos)
- Pode ter efeitos colaterais
- Retorna recurso criado ou resultado da ação

### PUT - Atualização Completa (futuro)

Substitui recurso completamente.

```http
PUT /api/gambit/{gambitId}
```

### PATCH - Atualização Parcial (futuro)

Atualiza campos específicos.

```http
PATCH /api/run/{runId}/state
```

### DELETE - Remoção

Remove recursos.

```http
DELETE /api/save/{saveId}
DELETE /api/events
```

**Características:**
- Idempotente (deletar múltiplas vezes = mesmo resultado)
- Requer confirmação para operações destrutivas

---

## Códigos de Status HTTP

### 2xx - Sucesso

- **200 OK** - Requisição bem-sucedida
- **201 Created** - Recurso criado com sucesso
- **204 No Content** - Sucesso sem corpo de resposta (DELETE)

### 4xx - Erro do Cliente

- **400 Bad Request** - Dados inválidos ou malformados
- **401 Unauthorized** - Autenticação necessária (futuro)
- **403 Forbidden** - Operação não permitida (flag de segurança)
- **404 Not Found** - Recurso não encontrado
- **409 Conflict** - Conflito de estado (ex: combate já finalizado)
- **422 Unprocessable Entity** - Validação de negócio falhou

### 5xx - Erro do Servidor

- **500 Internal Server Error** - Erro interno do servidor
- **501 Not Implemented** - Funcionalidade não implementada
- **503 Service Unavailable** - Serviço temporariamente indisponível

---

## Formato de Respostas

### Sucesso

**Recurso único:**
```json
{
  "name": "FIREBALL",
  "displayName": "Fireball",
  "type": "ROOT",
  "tags": ["fire", "aoe", "magic"],
  "rarity": "COMMON"
}
```

**Lista de recursos:**
```json
[
  {
    "name": "FIREBALL",
    "displayName": "Fireball"
  },
  {
    "name": "ICE_SHARD",
    "displayName": "Ice Shard"
  }
]
```

**Ação executada:**
```json
{
  "message": "Configuration 'alisyum' loaded successfully",
  "timestamp": "2026-05-08T10:30:00Z"
}
```

### Erro

**Formato consistente:**
```json
{
  "error": "Error message",
  "details": "Additional details about the error"
}
```

**Exemplos:**
```json
{
  "error": "Config reload is disabled",
  "details": "Set ALLOW_CONFIG_RELOAD=true in environment or appsettings.json to enable this operation"
}
```

```json
{
  "error": "Power not found",
  "details": "Power 'INVALID_POWER' does not exist in the current configuration"
}
```

```json
{
  "error": "Validation failed",
  "details": "Energy cost cannot be negative"
}
```

---

## Paginação (futuro)

Para listas grandes, usar paginação:

```http
GET /api/events?page=1&limit=50
```

**Resposta:**
```json
{
  "data": [...],
  "pagination": {
    "page": 1,
    "limit": 50,
    "total": 250,
    "totalPages": 5
  }
}
```

---

## Filtros e Query Parameters

### Filtros Comuns

```http
GET /api/events?category=COMBAT&severity=INFO&limit=50
GET /api/powers?tag=fire&rarity=RARE
GET /api/enemies?tier=ELITE
```

### Ordenação (futuro)

```http
GET /api/events?sortBy=timestamp&order=desc
```

### Busca (futuro)

```http
GET /api/powers?search=fire
```

---

## Segurança

### Flags de Segurança

Algumas operações requerem flags habilitadas:

**ALLOW_CONFIG_RELOAD**
- `POST /api/config/{name}/load`
- `POST /api/resource/reload`

**Configuração:**
```bash
# Via environment variable
export ALLOW_CONFIG_RELOAD=true
```

```json
// Via appsettings.json
{
  "AllowConfigReload": true
}
```

**Default:** `false` (desabilitado para segurança)

### Operações Destrutivas

Operações destrutivas devem ser marcadas claramente:
- `DELETE /api/save/{saveId}` - Deleta save
- `DELETE /api/events` - Limpa histórico (dev mode apenas)

### Dev Mode vs Production

Alguns endpoints são apenas para desenvolvimento:
- `DELETE /api/events` - Apenas dev mode
- `GET /api/resource/stats` - Debug info

---

## Versionamento (futuro)

Quando implementarmos versionamento:

```
/api/v1/powers
/api/v2/powers
```

**Política:**
- Versão major para breaking changes
- Versão minor para adições compatíveis
- Manter pelo menos 2 versões ativas
- Deprecation warnings antes de remover versão

---

## Headers

### Request Headers

```http
Content-Type: application/json
Accept: application/json
```

### Response Headers (futuro)

```http
Content-Type: application/json
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 999
X-RateLimit-Reset: 1620000000
```

---

## Rate Limiting (futuro)

Para prevenir abuso:
- 1000 requisições por hora por IP
- Headers indicam limite e reset
- 429 Too Many Requests quando excedido

---

## CORS (futuro)

Para permitir acesso de frontends:
```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, POST, PUT, DELETE
Access-Control-Allow-Headers: Content-Type
```

---

## Documentação

### Swagger/OpenAPI

API documentada via Swagger UI:
```
http://localhost:5260/swagger
```

### Exemplos

Cada endpoint deve ter exemplos de:
- Request body
- Response body
- Códigos de erro possíveis

---

## Testes

### Testando com PowerShell

```powershell
# GET
Invoke-RestMethod -Uri "http://localhost:5260/api/powers" -Method Get

# POST
$body = @{
  raceId = "human"
  difficulty = "normal"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5260/api/run/start" -Method Post -Body $body -ContentType "application/json"
```

### Testando com curl

```bash
# GET
curl http://localhost:5260/api/powers

# POST
curl -X POST http://localhost:5260/api/run/start \
  -H "Content-Type: application/json" \
  -d '{"raceId":"human","difficulty":"normal"}'
```

---

## Boas Práticas

### Para Implementadores

1. **Validação:** Validar inputs antes de processar
2. **Logging:** Logar erros e operações importantes
3. **Documentação:** Manter Swagger atualizado
4. **Testes:** Criar testes para cada endpoint
5. **Consistência:** Seguir padrões estabelecidos

### Para Consumidores

1. **Tratamento de erros:** Sempre verificar códigos de status
2. **Retry logic:** Implementar retry para erros 5xx
3. **Timeouts:** Configurar timeouts apropriados
4. **Validação:** Validar respostas antes de usar
5. **Logging:** Logar requisições para debug

---

## Referências

- [phase-0.md](../phases/phase-0.md) - Endpoints implementados
- [event-integration.md](event-integration.md) - Integração com EventBus
- [endpoints.md](../../api/endpoints.md) - Documentação detalhada
