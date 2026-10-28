# Hierarquias de eventos e motivos

Contrato interno inicial de `Core.GameplayEvents`. Ainda não habilita respostas novas em JSON de cartas/modos; a publicação só deve aceitar esses componentes depois de conectado o dispatcher canônico.

Identificadores usam segmentos ASCII minúsculos iniciados por letra, separados por `.`. Os demais caracteres permitidos em um segmento são números, `_` e `-`. Comparação ordinal; maiúsculas, escapes, regex e normalização silenciosa são rejeitados. Limites estruturais: 256 caracteres e 16 segmentos. IDs de recursos/conteúdo não passam a obedecer essa gramática.

| Padrão | Correspondência |
|---|---|
| `resource.change.resolved` | Exatamente esse identificador. |
| `resource.*` | Um nível; casa com `resource.change`, não com `resource.change.resolved`. |
| `resource.*.resolved` | Um segmento variável intermediário. |
| `resource.**` | Um ou mais níveis abaixo de `resource`; não inclui `resource`. |
| `**` | Qualquer identificador válido não vazio. |

`**` é terminal e aparece no máximo uma vez. `dam*`, `!topic`, segmentos vazios e caminhos de filesystem não são padrões válidos.

Uma seleção contém pelo menos um `include`, opcionalmente `exclude`. Inclusão é OR, exclusão vence, e padrões repetidos/sobrepostos não duplicam uma assinatura. Para incluir o nó e descendentes, usar `include: ["resource", "resource.**"]`.

Listas são copiadas e compiladas em ordem ordinal; até 64 padrões por lista. O índice imutável tem até 4096 identidades únicas e 65536 nós. O limite de nós é verificado durante a construção. Os IDs retornados são canônicos, não uma ordem de gameplay: o scheduler deve aplicar a prioridade e os desempates publicados separadamente.

O índice não interpreta palavras: `combat.damage.*` só compara segmentos. Não transforma subtração em dano, aumento em cura ou um recurso chamado health em condição de derrota.
