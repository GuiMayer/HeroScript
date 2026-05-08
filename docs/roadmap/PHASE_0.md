# Fase 0 - Fundação

**Status:** ✅ Implementado  
**Data de conclusão:** 2026-05-07

---

## Visão Geral

A Fase 0 estabelece a fundação da API REST do HeroScript, expondo os sistemas core de configuração, matemática e recursos. Estes endpoints fornecem a base para todas as fases futuras.

## APIs Implementadas

### 1. Config API

Gerencia o sistema de configuração com herança delta estruturada.

**Endpoints:**
- `GET /api/config` - Lista todas as configurações disponíveis
- `GET /api/config/current` - Obtém a configuração ativa com cadeia de herança
- `POST /api/config/{name}/validate` - Valida uma configuração
- `POST /api/config/{name}/load` - Carrega uma configuração (requer flag)
- `GET /api/config/{name}/chain` - Obtém cadeia de herança
- `GET /api/config/{name}/diff` - Compara configuração com parent
- `GET /api/config/tree` - Obtém árvore completa de hierarquia

**Recursos:**
- Sistema de herança delta com 9 operações (REPLACE, MERGE_DEEP, ARRAY_APPEND, etc.)
- Validação de configurações
- Visualização de diferenças entre configs
- Árvore de hierarquia completa

### 2. Operation API

Fornece metadados sobre operações matemáticas disponíveis.

**Endpoints:**
- `GET /api/operation` - Lista todas as operações (18 operações)
- `GET /api/operation/{name}` - Obtém metadados de operação específica
- `GET /api/operation/categories` - Agrupa operações por categoria

**Categorias:**
- `basic`: ADD, SUBTRACT, MULTIPLY, DIVIDE, NEGATE, ABS, ROUND, FLOOR, CEIL
- `advanced`: DIVIDE_INVERSE, POW, POW_BASE, SQRT, LOG
- `multi-value`: MIN, MAX, CLAMP
- `special`: SET

### 3. Resource API

Gerencia carregamento e cache de recursos.

**Endpoints:**
- `GET /api/resource/origins` - Obtém origem de recursos de fórmulas
- `POST /api/resource/reload` - Recarrega recursos (requer flag)
- `GET /api/resource/stats` - Estatísticas de cache (não implementado)

**Recursos:**
- Rastreamento de origem de fórmulas por config
- Reload de recursos em tempo de desenvolvimento
- Sistema de cache (estatísticas pendentes)

### 4. MathExpression API

Avalia expressões matemáticas com três modos de operação.

**Endpoints:**
- `POST /api/math/evaluate` - Avalia expressão matemática
- `GET /api/math/modes` - Lista modos disponíveis

**Modos:**
- **Implícito (Accumulator):** Operações modificam acumulador
- **Explícito Literal:** Operações com valores literais explícitos
- **Explícito Simbólico:** Operações com referências a parâmetros

### 5. Formula API

Gerencia fórmulas matemáticas definidas em JSON.

**Endpoints:**
- `GET /api/formula` - Lista fórmulas disponíveis
- `GET /api/formula/{name}` - Obtém definição de fórmula
- `POST /api/formula/{name}/evaluate` - Avalia fórmula com parâmetros

**Recursos:**
- Fórmulas data-driven (JSON)
- Herança delta de fórmulas
- Avaliação com parâmetros customizados

### 6. Health API

Endpoint de health check para monitoramento.

**Endpoints:**
- `GET /api/health` - Status da API

---

## Segurança

### Flag: ALLOW_CONFIG_RELOAD

Endpoints que modificam estado requerem esta flag habilitada:
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

---

## Documentação Relacionada

- [API-ENDPOINTS.md](../API-ENDPOINTS.md) - Documentação detalhada dos endpoints
- [CONFIG_SYSTEM.md](../CONFIG_SYSTEM.md) - Sistema de configuração
- [CORE_MATH_SYSTEM.md](../CORE_MATH_SYSTEM.md) - Sistema matemático
- [API_MATH_EXPRESSION_MODES.md](../API_MATH_EXPRESSION_MODES.md) - Modos de expressão

---

## Próximos Passos

Com a fundação estabelecida, a próxima fase implementará:
- **EventBus** - Sistema de eventos pub/sub
- **Combat API** - Sistema de combate básico
- **Damage API** - Pipeline de cálculo de dano

Ver: [PHASE_1.md](PHASE_1.md)
