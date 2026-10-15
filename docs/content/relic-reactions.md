# Reações de relíquias a aplicações de recurso

Primeira parte da etapa 5 do plano de inscrições. Este contrato não transforma inscrições em relíquias nem altera o valor base das cartas.

## Autoridade e execução

`RelicDefinition.reactions` contém componentes `EffectReactionDefinition`, copiados para a instância na aquisição. O executor observa a aplicação primária de um efeito, depois do cálculo, da absorção e do clamp do recurso. Não observa pagamentos de custos, prefixos ou settlements como se fossem novos impactos. Tipos `DAMAGE`, `HEAL` e `MODIFY_RESOURCE` continuam sendo operações genéricas; não há recurso de vida implícito.

Cada componente seleciona recurso, campo, operação, canais e tags. `ownerScope` associa a relíquia ao autor (`Source`) ou ao alvo atingido (`Target`). `selection` define se o `TARGET` dos efeitos resultantes será o autor ou o alvo original; `SELF` sempre será o dono da reação. `requireLivingOwner` é `true` por padrão. Desativá-lo permite executar efeitos de um dono derrotado, mas não autoriza automaticamente selecionar alvos mortos ou ressuscitá-los.

Reações executam imediatamente após o impacto e antes de seus filhos encadeados, ordenadas por prioridade crescente, ID da instância e ID da reação. Cada repetição/impacto observa seu próprio resultado. Instâncias repetidas de uma relíquia com stacks executam uma reação por impacto; `stacks` fica disponível à fórmula, não há multiplicação implícita.

Os efeitos resultantes passam pelo mesmo `EffectTriggerExecutor`, pipeline, processador imutável e regras de alvo. Usam a revisão de aquisição, provenance da relíquia e ligação ao impacto pai. Não herdam a carta, seus modificadores, seus resultados nomeados, tags ou rolls. Cada reação tem namespace próprio de resultados e escopos locais de execução; `OncePerAction` dentro dela se refere àquela subexecução, não a todos os impactos da carta original.

Efeitos de reação e todos os seus descendentes **não geram novas reações**. Essa fronteira impede loops lifesteal/reflexo; não depende de tags voluntárias no conteúdo. O orçamento de trabalho e a profundidade são compartilhados com a ação original. Qualquer falha rejeita toda a transação, incluindo o impacto inicial, sem alterar o snapshot ou o cursor aleatório recebido.

## Exemplo de recuperação por perda real

Fragmento de uma relíquia; o modo precisa habilitar o pipeline `recovery`, de canal `recovery` e unidade `points`. A fórmula e os IDs são escolhas deste exemplo, não comportamento hardcoded:

```json
{
  "relicId": "siphon_example",
  "reactions": [{
    "reactionId": "recover_from_loss",
    "ownerScope": "Source",
    "selection": "Target",
    "resourceId": "health",
    "resourceField": "Current",
    "operation": "Subtract",
    "channels": ["effect_amount"],
    "requiredTags": ["attack"],
    "effects": [{
      "effectId": "recover",
      "type": "HEAL",
      "target": "SELF",
      "targetResource": "health",
      "condition": "parent.applied_change < 0",
      "parameters": [{
        "parameter": "Amount",
        "formulaValue": "-parent.applied_change * 0.2",
        "channel": "recovery",
        "pipelineId": "recovery",
        "unitId": "points"
      }]
    }]
  }]
}
```

`parent.applied_change` é assinado: perda de 4 resulta em `-4`. Absorção completa resulta em zero; solicitar 30 de dano contra 10 restantes produz perda real de 10, não 30. A condição do exemplo suprime recuperação sem perda. O pipeline de recuperação pode aplicar suas próprias regras e limites; não deve reutilizar um pipeline ofensivo que recalcula o dano ou aplica críticos por acidente.

Reflexo usa `ownerScope: Target` e `selection: Source`. DoTs usam a origem persistida pelo sistema de status; não transferem a autoria para o personagem que está tendo seu turno.

## Limites desta entrega

- Conteúdo existente sem `reactions` mantém o campo ausente na serialização.
- Publicação valida os efeitos e referências; aquisição valida a forma antes de capturar a definição.
- O exemplo ainda não foi distribuído no catálogo do mundo 1.
- A relíquia completa de conversão do bônus crítico, a restrição universal de shield e sua substituição por armor ainda não estão concluídas. Zerar um ganho em uma carta ou em um bucket isolado não seria uma restrição universal e não é apresentado como tal.
- Integração REST do laboratório, aquisição de inscrições e certificação de migrações continuam nas etapas seguintes.
