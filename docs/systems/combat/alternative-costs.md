# Alternative costs

Alternative costs are authored as cost components in a card container or in
an ability definition. Every option contains a stable `optionId` and one or
more explicit resource debits. Resources are identified only by `resourceId`;
the cost system does not assign special meaning to health, mana, energy, or
any other resource.

## Authoring

```json
{
  "componentId": "cost",
  "type": "cost",
  "costs": {
    "costs": [],
    "alternativeCosts": [
      {
        "optionId": "mana_cost",
        "description": "Pay 8 mana",
        "costs": [
          { "resourceId": "mana", "amount": 8 }
        ]
      },
      {
        "optionId": "health_cost",
        "description": "Pay 20 health",
        "costs": [
          { "resourceId": "health", "amount": 20 }
        ]
      }
    ]
  }
}
```

Definitions belong to an immutable content revision. Permanent card upgrades
may patch cost amounts by stable component and resource IDs. Contextual
discounts come from calculation influences such as statuses, relics, and run
modifiers; they do not rewrite the card definition.

## Read model

Use the card evaluation endpoints:

```http
GET /api/v1/combats/{combatId}/cards/evaluations
GET /api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation?targetIds=enemy-1&costOptionId=health_cost
```

The response includes the effective card, legal target IDs, cost options,
calculation traces, contextual influences, and whether the selected option is
currently playable. This projection is computed by the same pinned services
used for execution and never mutates the run.

## Execution

Choose the option in the canonical command. Do not send a replacement cost or
card definition from the client.

```http
POST /api/v1/combats/{combatId}/commands
Content-Type: application/json

{
  "commandId": "a4c5ba8f-6af4-4d84-96fb-f979c3ad598b",
  "expectedSequence": 8,
  "expectedStep": 12,
  "type": "PLAY_CARD",
  "payload": {
    "actorId": "hero",
    "cardInstanceId": "10000000-0000-8000-8000-000000000001",
    "targetIds": ["enemy-1"],
    "costOptionId": "health_cost"
  }
}
```

Validation and all resource debits are part of the same immutable transition.
An invalid or unaffordable option rejects the complete command, so no partial
payment or effect can be committed.

## Invariants

- Costs always reference configurable resources by ID.
- The run's `contentRevision` determines the authored options.
- The card instance determines permanent upgrades.
- Statuses, relics, and modifiers influence calculations contextually.
- Inspection and execution share the same evaluator.
- Only the command gateway may commit payment and effects.
- The chosen `costOptionId` is recorded in the durable command timeline.

Relevant implementation: `CardPlayEvaluator`, `CardPlayExecutor`,
`ActionCostEvaluator`, and `CombatCardController`.
