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
      "condition": "parent.applied_change * -1",
      "parameters": [{
        "parameter": "Amount",
        "formulaValue": "parent.applied_change * -0.2",
        "channel": "recovery",
        "pipelineId": "recovery",
        "unitId": "points"
      }]
    }]
  }]
}
```

`parent.applied_change` é assinado: perda de 4 resulta em `-4`. Absorção completa resulta em zero; solicitar 30 de dano contra 10 restantes produz perda real de 10, não 30. Condições numéricas passam quando o resultado é positivo; por isso o exemplo multiplica a perda por `-1`. A gramática inline usa operadores aritméticos separados por espaços, não comparação `<` ou negação de variável sem operador. O pipeline de recuperação pode aplicar suas próprias regras e limites; não deve reutilizar um pipeline ofensivo que recalcula o dano ou aplica críticos por acidente.

Reflexo usa `ownerScope: Target` e `selection: Source`. DoTs usam a origem persistida pelo sistema de status; não transferem a autoria para o personagem que está tendo seu turno.

`targetRelationship`, quando declarado, filtra a relação dirigida do autor original para o alvo original (`Ally`, `Enemy`, `Neutral`). Ele permite excluir autodano/aliados sem comparar nomes de entidades. Ausente, não restringe relações.

## Limites desta entrega

- Conteúdo existente sem `reactions` mantém o campo ausente na serialização.
- Publicação valida os efeitos e referências; aquisição valida a forma antes de capturar a definição.
- O Ascendant contém as definições executáveis `w1_blood_covenant` e `w1_iron_vow`, publicadas pelo manifesto do pacote. A primeira zera o bônus por tier no bucket terminal `bonus_cap` (sem remover tiers/chance), e recupera 20% da perda real de health por ataque direto contra inimigo. A segunda usa [restrições de recurso](resource-restrictions.md) para descartar/bloquear Block e concede 25 Armor pelo provider existente. Ambos são valores/configurações iniciais do setting, não constantes da engine.
- O mundo 1 distribui Iron Vow em `elite_relic_iron` e Blood Covenant em `relic_field`. Cinco nós de bônus numéricos passaram a oferecer inscrições; definições antigas permanecem disponíveis para históricos, sem conversão automática de saves. Nenhuma carta foi editada para habilitar as regras.
- O laboratório pode conectar à run via REST. Consulte [aquisição](inscription-acquisition.md) e [migrações](inscription-migrations.md) para políticas e limites.
