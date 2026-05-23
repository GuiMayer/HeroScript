# Event Integration

**Última atualização:** 2026-05-08

---

## Visão Geral

Este documento descreve as estratégias de integração entre a API REST e o sistema de EventBus, permitindo que clientes consumam eventos de combate, sistema, e meta-progressão. Duas abordagens principais são apresentadas: **Polling (GET)** e **WebSocket (Push)**.

## Contexto

O EventBus é o sistema central de comunicação entre componentes do HeroScript. Todos os eventos importantes são publicados através dele:

- **Eventos de Combate** - Ataques, dano, morte, status aplicados
- **Eventos de Pipeline** - Processamento de buckets de dano
- **Eventos de Sistema** - Config load, resource reload
- **Eventos de Meta** - Desbloqueios, feats, estatísticas

Clientes da API (frontends, ferramentas de análise, bots) precisam acessar esses eventos para:
- Atualizar UI em tempo real
- Exibir histórico de combate
- Debugar comportamento do jogo
- Analisar estratégias
- Replay de runs

---

## Abordagem 1: Polling (GET)

### Descrição

Cliente consulta periodicamente o endpoint `/api/events` para obter novos eventos.

### Implementação

**Endpoint:**
```http
GET /api/events?since={lastEventId}&limit=50
```

**Fluxo:**
1. Cliente faz requisição inicial: `GET /api/events?limit=50`
2. Servidor retorna eventos mais recentes
3. Cliente armazena ID do último evento recebido
4. Cliente aguarda intervalo (ex: 1 segundo)
5. Cliente faz nova requisição: `GET /api/events?since={lastEventId}`
6. Repete passos 4-5

**Exemplo de Resposta:**
```json
{
  "events": [
    {
      "eventId": "evt-123",
      "timestamp": "2026-05-08T10:30:00Z",
      "category": "COMBAT",
      "type": "DAMAGE_DEALT",
      "payload": {
        "actorId": "player-1",
        "targetId": "goblin-1",
        "damage": 15
      }
    }
  ],
  "hasMore": false,
  "nextCursor": "evt-124"
}
```

### Vantagens

**1. Simplicidade de Implementação**
- Usa HTTP padrão (GET)
- Não requer infraestrutura adicional
- Funciona com qualquer cliente HTTP
- Fácil de testar (curl, Postman, browser)

**2. Compatibilidade Universal**
- Funciona através de proxies e firewalls
- Não requer suporte a WebSocket
- Compatível com HTTP/1.1 e HTTP/2
- Funciona em ambientes restritos (corporate networks)

**3. Stateless**
- Servidor não mantém conexões abertas
- Fácil de escalar horizontalmente
- Não há problema com reconexão
- Cliente controla frequência de polling

**4. Debugging Simples**
- Requisições visíveis em logs HTTP
- Fácil de inspecionar com ferramentas de rede
- Replay manual de requisições
- Não há estado de conexão para debugar

**5. Caching Possível**
- Eventos históricos podem ser cacheados
- CDN pode servir eventos antigos
- Reduz carga no servidor para histórico

### Desvantagens

**1. Latência**
- Delay entre evento ocorrer e cliente receber (intervalo de polling)
- Não é verdadeiro "tempo real"
- Mínimo de 500ms-1s de latência típica
- Eventos podem chegar em lotes, não individualmente

**2. Overhead de Rede**
- Requisições frequentes mesmo sem novos eventos
- Headers HTTP repetidos em cada requisição
- Desperdício de banda se polling muito frequente
- Mais requisições = mais custo de rede

**3. Carga no Servidor**
- Servidor processa requisições mesmo quando não há eventos
- Muitos clientes polling = muitas requisições vazias
- Dificulta rate limiting efetivo
- Pode sobrecarregar servidor em picos

**4. Experiência do Usuário**
- Atualizações "saltadas" (não fluidas)
- Delay perceptível em ações rápidas
- Não ideal para animações sincronizadas
- Pode parecer "travado" entre polls

**5. Complexidade no Cliente**
- Cliente precisa gerenciar loop de polling
- Lógica de retry e backoff
- Gerenciar cursor/lastEventId
- Detectar quando parar de polling

### Quando Usar

- **MVP e prototipagem** - Implementação rápida
- **Ambientes restritos** - Firewalls bloqueiam WebSocket
- **Baixa frequência de eventos** - Eventos espaçados (>5s)
- **Clientes simples** - Scripts, ferramentas CLI
- **Histórico e análise** - Consulta de eventos passados

---

## Abordagem 2: WebSocket (Push)

### Descrição

Servidor mantém conexão persistente com cliente e envia eventos assim que ocorrem.

### Implementação

**Endpoint:**
```
ws://localhost:5260/api/events/stream
```

**Fluxo:**
1. Cliente abre conexão WebSocket
2. Cliente envia mensagem de subscribe (opcional: filtros)
3. Servidor envia eventos em tempo real conforme ocorrem
4. Cliente processa eventos conforme chegam
5. Conexão permanece aberta até cliente desconectar

**Exemplo de Mensagem (Cliente → Servidor):**
```json
{
  "action": "subscribe",
  "filters": {
    "categories": ["COMBAT", "PIPELINE"],
    "severity": ["INFO", "WARN"]
  }
}
```

**Exemplo de Mensagem (Servidor → Cliente):**
```json
{
  "type": "event",
  "data": {
    "eventId": "evt-123",
    "timestamp": "2026-05-08T10:30:00.123Z",
    "category": "COMBAT",
    "type": "DAMAGE_DEALT",
    "payload": {
      "actorId": "player-1",
      "targetId": "goblin-1",
      "damage": 15
    }
  }
}
```

### Vantagens

**1. Latência Mínima**
- Eventos chegam em <50ms após ocorrerem
- Verdadeiro tempo real
- Ideal para animações e feedback imediato
- Experiência fluida e responsiva

**2. Eficiência de Rede**
- Sem overhead de headers HTTP repetidos
- Apenas dados de eventos são transmitidos
- Conexão persistente = menos handshakes
- Menor uso de banda total

**3. Eficiência do Servidor**
- Servidor não processa requisições vazias
- Push apenas quando há eventos
- Melhor uso de recursos
- Escala melhor com muitos clientes

**4. Experiência do Usuário**
- Atualizações instantâneas e fluidas
- Ideal para UI reativa
- Animações sincronizadas
- Feedback imediato de ações

**5. Bidirecional**
- Cliente pode enviar comandos pelo mesmo canal
- Servidor pode enviar notificações proativas
- Suporte a subscriptions dinâmicas
- Protocolo mais rico

### Desvantagens

**1. Complexidade de Implementação**
- Requer biblioteca WebSocket no servidor
- Gerenciamento de conexões persistentes
- Lógica de reconexão automática
- Mais código para manter

**2. Infraestrutura Adicional**
- Requer suporte a WebSocket no servidor
- Proxies/load balancers precisam suportar WebSocket
- Pode não funcionar em alguns ambientes corporativos
- Requer configuração adicional (CORS, etc.)

**3. Stateful**
- Servidor mantém estado de conexões
- Dificulta escala horizontal (sticky sessions)
- Reconexão pode perder eventos
- Mais complexo para debugar

**4. Compatibilidade**
- Alguns firewalls bloqueiam WebSocket
- Proxies antigos podem não suportar
- Fallback para polling pode ser necessário
- Não funciona em todos os ambientes

**5. Debugging Mais Difícil**
- Mensagens não aparecem em logs HTTP padrão
- Ferramentas de rede podem não capturar bem
- Estado de conexão adiciona complexidade
- Replay manual é mais difícil

### Quando Usar

- **UI em tempo real** - Jogos, dashboards, animações
- **Alta frequência de eventos** - Muitos eventos por segundo
- **Experiência premium** - Feedback instantâneo crítico
- **Ambientes controlados** - Infraestrutura própria
- **Aplicações modernas** - Frontends React/Vue/Angular

---

## Comparação Lado a Lado

| Aspecto | Polling (GET) | WebSocket (Push) |
|---------|---------------|------------------|
| **Latência** | 500ms - 2s | <50ms |
| **Overhead de Rede** | Alto (headers repetidos) | Baixo (apenas dados) |
| **Carga no Servidor** | Alta (requisições vazias) | Baixa (push apenas com eventos) |
| **Complexidade** | Simples | Moderada |
| **Compatibilidade** | Universal | Boa (mas não universal) |
| **Stateful/Stateless** | Stateless | Stateful |
| **Debugging** | Fácil | Moderado |
| **Escala Horizontal** | Fácil | Requer sticky sessions |
| **Experiência do Usuário** | Boa | Excelente |
| **Ideal para** | MVP, análise, histórico | UI real-time, jogos |

---

## Recomendação de Implementação

### Fase 1 (MVP) - Polling Apenas

Implementar apenas polling para validar conceito rapidamente:
- `GET /api/events` com filtros e paginação
- `GET /api/events/history` para histórico completo
- `DELETE /api/events` para limpar (dev mode)

**Vantagens:**
- Implementação rápida (1-2 dias)
- Testa arquitetura de eventos sem complexidade adicional
- Suficiente para validar gameplay e balanceamento

### Fase 2 (Produção) - WebSocket + Fallback

Adicionar WebSocket mantendo polling como fallback:
- `ws://localhost:5260/api/events/stream` para tempo real
- Cliente tenta WebSocket primeiro
- Se falhar, fallback automático para polling
- Melhor dos dois mundos

**Vantagens:**
- Experiência premium quando possível
- Compatibilidade universal via fallback
- Permite otimização progressiva

---

## Exemplo de Cliente Híbrido

### JavaScript (Browser)

```javascript
class EventClient {
  constructor(baseUrl) {
    this.baseUrl = baseUrl;
    this.useWebSocket = true;
    this.lastEventId = null;
    this.onEvent = null;
  }

  connect() {
    if (this.useWebSocket) {
      this.connectWebSocket();
    } else {
      this.startPolling();
    }
  }

  connectWebSocket() {
    const ws = new WebSocket(`ws://${this.baseUrl}/api/events/stream`);
    
    ws.onopen = () => {
      console.log('WebSocket connected');
      ws.send(JSON.stringify({
        action: 'subscribe',
        filters: { categories: ['COMBAT'] }
      }));
    };
    
    ws.onmessage = (event) => {
      const data = JSON.parse(event.data);
      if (this.onEvent) this.onEvent(data.data);
    };
    
    ws.onerror = () => {
      console.log('WebSocket failed, falling back to polling');
      this.useWebSocket = false;
      this.startPolling();
    };
  }

  startPolling() {
    setInterval(async () => {
      const url = this.lastEventId 
        ? `${this.baseUrl}/api/events?since=${this.lastEventId}`
        : `${this.baseUrl}/api/events?limit=50`;
      
      const response = await fetch(url);
      const data = await response.json();
      
      data.events.forEach(event => {
        if (this.onEvent) this.onEvent(event);
        this.lastEventId = event.eventId;
      });
    }, 1000);
  }
}

// Uso
const client = new EventClient('localhost:5260');
client.onEvent = (event) => {
  console.log('Event received:', event);
  updateUI(event);
};
client.connect();
```

---

## Considerações de Segurança

### Polling

- Rate limiting por IP
- Autenticação via headers (futuro)
- Validação de filtros
- Limite de eventos por requisição

### WebSocket

- Autenticação no handshake
- Validação de origem (CORS)
- Limite de conexões por IP
- Timeout de conexões inativas
- Validação de mensagens do cliente

---

## Métricas e Monitoramento

### Polling

- Taxa de requisições vazias (sem eventos)
- Latência média de entrega de eventos
- Banda consumida por cliente
- Taxa de erro 429 (rate limit)

### WebSocket

- Número de conexões ativas
- Taxa de reconexão
- Latência de entrega de eventos
- Eventos perdidos em reconexão
- Memória usada por conexões

---

## Próximos Passos

1. **Fase 1 (Atual)** - Implementar EventBus Core
2. **Fase 1.1** - Implementar Events API com Polling
3. **Fase 1.2** - Testar com frontend simples
4. **Fase 2** - Adicionar WebSocket quando necessário
5. **Fase 3** - Otimizar baseado em métricas reais

---

## Referências

- [phase-1.md](../phases/phase-1.md) - EventBus e Combat API
- [eventbus-implementation.md](../../systems/events/eventbus-implementation.md) - Plano detalhado do EventBus
- [api-conventions.md](api-conventions.md) - Convenções gerais da API
