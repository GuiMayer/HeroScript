# Card payment selection

This is an optional extension to ordinary `PLAY_CARD`. A cost component declares the requirements through revisioned JSON; see [authoring](../guides/card-zone-payments.md). Existing cards and command envelopes are unchanged when the feature is absent.

The compact session publishes `cardPresentations.cards[].hasCardPayments: true` for a composed card with these costs, including components granted by the build. It does not execute a full inspection for every card. Clients open the selector on demand.

## Preview

`POST /api/v1/combats/{combatId}/cards/payment-preview` captures one immutable run/combat revision. `expectedSequence` is the **run sequence**; `expectedStep` is the **combat step**.

```json
{
  "expectedSequence": 2,
  "expectedStep": 0,
  "command": {
    "actorId": "player",
    "actionType": "PLAY_CARD",
    "cardInstanceId": "70000000-0000-4000-8000-000000000001",
    "targetIds": ["enemy_0"],
    "paymentSelections": [{
      "componentId": "cost",
      "paymentId": "offer",
      "cardInstanceIds": ["70000000-0000-4000-8000-000000000002"]
    }]
  }
}
```

An incomplete selection returns a successful read with `isLegal: false`, reasons and `evaluation.paymentRequirements`; a stale revision returns HTTP 409. Requirements contain component/payment identity, optional alternative-cost identity, quantity, source/destination, order policy and visible eligible IDs. They describe individual requirements, not every possible combination.

The response contains `version`, the public `command`, `evaluation`, canonical visible card faces and preview `applications`. Resource outcomes are server-calculated. `outcomeUncertain` distinguishes a sampled random outcome from an exact preview. Internal transfer plans and pile order traces are excluded; hidden faces/identities are not synthesized. Preview changes neither state, RNG nor journal.

## Commit and recovery

Submit the returned `command` as the `payload` of the existing combat command endpoint, with `type: PLAY_CARD`, a stable `commandId`, and the captured expected sequence/step. The gateway validates the selections again in the authoritative transaction. Losing the response requires retrying that same envelope, not preparing a second payment command.

The Godot selector keeps targets, cost option and offered cards local until confirmation. Every change invalidates its previous preview. Out-of-order responses cannot enable Play; changing session, run, branch, setting or version closes the old selection. Cancel sends no gameplay command. The confirmed movement facts use the existing card animation executor.

## Integrated proof

Select **Payment Workshop** (`payment-lab`) in the game setting menu. Its authored five-card deck contains **Hand offering** and **Spent catalyst**. Offer a different card from hand with the first card, then offer that discarded instance with the second. The setting inherits the existing rules/content and adds only its proof cards, transfers, encounter and durable test target. It does not change existing settings.

Editable selector: `examples/godot-engine-showcase/scenes/ui/overlays/card_payment.tscn`; reusable group: `scenes/ui/components/card_payment_group.tscn`. Both the offline UI test and real combat instantiate this selector and the canonical CardView. QA uses dedicated persistence and a separate API port; it does not use player saves.
