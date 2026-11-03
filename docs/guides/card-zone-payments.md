# Pagamentos com cartas

Implementação de engine da etapa 5 e integração REST/Godot da etapa 6 do MVP. O [contrato de seleção e os exemplos públicos](../api/card-payments.md) usam o mesmo componente de cartas e o executor do combate; definições novas não são habilitadas implicitamente em settings existentes.

## Definição

O componente `cost` preserva `costs` e suas alternativas de recursos. `zonePayments` acrescenta pagamentos AND; `costOptionId`, quando presente, vincula o pagamento a uma alternativa já declarada em `costs.alternativeCosts`. Uma alternativa pode ter uma lista de recursos vazia e exigir apenas cartas. Os IDs das opções precisam existir no componente correspondente.

```json
{
  "type": "cost",
  "componentId": "activation",
  "costs": { "costs": [{ "resourceId": "energy", "amount": 1 }] },
  "zonePayments": [{
    "paymentId": "offer",
    "flowId": "offer.transfer",
    "sourceZoneId": "available",
    "targetZoneId": "offered",
    "count": 1,
    "allowResolvingCard": false,
    "order": "SourceOrder",
    "requiredTags": [],
    "excludedTags": []
  }]
}
```

Os nomes de zonas e tags são arbitrários. A engine não interpreta sacrifício, mão, descarte ou exílio. O fluxo referenciado precisa permitir `Effect`, usar `actorScope: Any` e conter exatamente uma transferência explícita, incondicional, com políticas de insuficiência/overflow `RejectTransaction`:

```json
{
  "flowId": "offer.transfer",
  "allowedInvocations": ["Effect"],
  "steps": [{
    "stepId": "transfer",
    "operation": "Move",
    "sourceZoneId": "available",
    "targetZoneId": "offered",
    "sourceOwner": "FlowOwner",
    "targetOwner": "FlowOwner",
    "selection": { "strategy": "Explicit" }
  }]
}
```

As zonas da definição do pagamento são restrições verificadas contra o fluxo. Não existe um segundo executor de movimentação. Propriedade deve ser resolvida pelo ator (`FlowOwner`, `ActiveActor`, `SourceActor`, `TargetActor`) ou por escopo compartilhado explicitamente configurado (`RunOwner`, `Global`). Destinos, capacidade, ordenação e inserção continuam no grafo de zonas.

## Validação e execução

A avaliação retorna requisitos por componente/pagamento e IDs elegíveis, sem enumerar combinações. Identidades de uma origem oculta não são publicadas nem aceitas para pagamento manual. Zonas privadas de outro participante não são substituídas pela zona do ator. Tags usam o resolver efetivo revisionado, incluindo melhorias e componentes herdados.

A intenção referencia `componentId`, `paymentId` e `cardInstanceIds`. A quantidade deve ser exata, entre 1 e 64 por pagamento; custo zero deve ser representado omitindo o pagamento. IDs repetidos, desconhecidos, fora da origem, inelegíveis ou já usados em outro pagamento da ação são rejeitados. Seleções de alternativas não escolhidas também são rejeitadas.

`SourceOrder` é a configuração inicial: normaliza a seleção pela ordem da origem, ou por identidade quando a zona não possui ordem. `ChosenOrder` preserva explicitamente a ordem enviada. A projeção não revela a ordem de uma origem cuja apresentação a oculta.

O planejamento simula as mesmas transferências sobre snapshots locais para verificar a capacidade conjunta. Não publica estado, RNG ou journal. Na execução, custos de recurso são efeitos pré-resolvidos de recurso; pagamentos são efeitos pré-resolvidos `CARD_ZONE_FLOW`, processados pelo `RunEffectReducer` e executor de zonas comuns. Ambos precedem os efeitos autorados da carta e participam das mesmas observações/respostas e da transação externa. Falha em qualquer pagamento, resposta, efeito ou disposição descarta a resolução inteira.

Com `allowResolvingCard: false`, a própria instância em uso é inelegível. Com `true`, ela pode ser escolhida. Se for paga, seu destino é o fluxo do pagamento e a disposição normal não é executada novamente. Ela ainda resolve os efeitos da carta; não há destruição implícita. Se ela não for escolhida, a disposição normal permanece intacta.

Sem `zonePayments`, os campos adicionais são omitidos da serialização e os custos existentes mantêm seu caminho. A avaliação de alternativas exige que a opção selecionada seja válida em cada componente que declara alternativas; uma opção disponível em outro componente não autoriza ignorar um custo.
