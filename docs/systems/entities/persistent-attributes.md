# Atributos persistentes do personagem

Contrato da etapa 10. Engine version 19.

`runs[].playerDefinitionId` captura uma `EntityState` em `RunState.playerEntity` no início da run, com ID, definição, revisão e componentes stats imutáveis. O campo é opt-in: runs sem personagem persistente não recebem valores inventados. Resources continuam em suas autoridades existentes; a nova entidade não duplica moedas nem pools de combate. Inventário/abilities também não ganham persistência implícita nesta mudança.

## Autoridade e transições

- Atributos RunBase pertencem à run. São copiados para o ator no início de cada encontro, antes dos efeitos de inicialização. Não voltam a ser lidos da definição para substituir o investimento.
- Atributos Encounter pertencem ao candidato daquele combate. Não são copiados de volta para a run quando o encontro acaba.
- Status e modifiers temporários oferecem influências na pipeline; sua expiração não remove investimento permanente.
- Identidade/definição/revisão do ator precisam corresponder ao personagem capturado. Mismatch falha; não escolhe silenciosamente outro personagem.
- Hot reload/rebase valida componentes, value IDs e bounds na nova revisão. Valores investidos são preservados; mudanças incompatíveis são rejeitadas. Ativação é o comando explícito já existente, não alteração do histórico.

## Operações autorizadas

Um componente stats declara `values` e `valueRules`. Sem regra, seu valor pode participar de cálculos, mas não pode ser alterado por MODIFY_ATTRIBUTE.

```json
{
  "type": "stats",
  "componentId": "attributes",
  "values": { "power": 2 },
  "valueRules": {
    "power": { "allowedOperations": ["Add", "Multiply", "Set"], "minimum": 0, "maximum": 100 }
  }
}
```

```json
{
  "type": "MODIFY_ATTRIBUTE",
  "target": "SELF",
  "attributeMutation": {
    "componentId": "attributes", "valueId": "power",
    "operation": "Add", "lifetime": "RunBase"
  },
  "parameters": [{
    "parameter": "Amount", "flatValue": 1,
    "channel": "attribute_delta", "pipelineId": "attribute_delta", "unitId": "attribute_points"
  }]
}
```

Amount usa o resolver/pipeline comum; o reducer aplica a operação tipada e rejeita valores não finitos ou fora dos bounds. Não há clamp implícito. Add/Multiply/Set e IDs precisam ser autorizados pela regra capturada. Unidade e cálculo são conteúdo: multiplier e delta podem usar perfis diferentes.

RunBase somente altera o jogador fora de um encontro, no adaptador de atividade comum. Encounter altera apenas o alvo presente naquele combate. Alteração permanente durante combate é rejeitada, não parcialmente aplicada.

## Preparações e inspeção

`preparations[].options[].effects` captura efeitos publicados em `PreparationOptionState`. APPLY_PREPARATION_OPTION continua recebendo IDs, resolve custos/grants e executa os efeitos no mesmo candidato antes do commit. Falha posterior descarta tudo. Não existe rota que aceite um patch arbitrário do jogador.

As regras de boundaries — execução garantida, alvo do owner e efeito persistente — são validadas também nos filhos, inclusive em publicação. Entry/exit de atividades e opções usam o mesmo processador; não há reducer de atributos exclusivo de preparação.

`CardInspectionContext.persistentActor` separa base permanente de `actor` vivo. O `EntityStatInfluenceProvider` já lê componente/value IDs do ator; tags/whitelists da pipeline determinam quais cartas usam o atributo. Upgrades da carta continuam base da carta, não stats do personagem.

Estado e regras serializam nos snapshots/hashes existentes. Testes incluem reinício em store real, retry, dez replays semânticos e fork isolado para a preparação; regressão integrada com a jornada e novo setting será executada no gate final.
