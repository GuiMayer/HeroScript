# Exclusive card reward decisions

`card-selections` can opt into a terminal decompilation decision:

```json
{
  "selectionId": "prototype_reward",
  "pickCount": 1,
  "offerCount": 3,
  "cardPoolId": "prototype_rewards",
  "decompose": { "enabled": true, "completesSelection": true }
}
```

The definition's rules are captured in `CardSelectionState`. When
`completesSelection` is true, `DECOMPOSE_CARD_REWARD` grants that option's
published resource rewards and marks the entire selection completed in the
same transaction. `PICK_CARD_REWARD`, another decompilation, and reroll then
reject the closed selection. The ordinary node completion command becomes
available. A failed payout does not close the offer or grant a partial reward.

When the property is absent or false, the existing behavior remains: decomposing
one option removes that option while leaving other options open. The absent
property is omitted from snapshots, retaining older canonical wire shapes.
Already-created offers retain their captured policy through content revisions.

## Storage and material ownership

Card storage uses the setting's existing `cardZoneSystem.gameplayGrantFlowId`.
The tactical prototype configures a create-in-`owned` flow, leaving the awarded
instance outside `deck.active`. No automatic composition edit, draw or extra
inventory copy occurs. A future build change selects that same owned instance.

Materials are ordinary run resources. Each card's `decomposeRewards` publishes
resource ID and amount; the prototype grants three `parts`. These are separate
from the character's Health/Heat pools and survive retry according to the run's
existing rules. The engine gives neither `owned` nor `parts` a built-in purpose.

## REST and recovery

Use the existing run command envelopes:

- `CREATE_CARD_SELECTION`: `selectionId`.
- `PICK_CARD_REWARD`: `selectionInstanceId` and `cardIds`.
- `DECOMPOSE_CARD_REWARD`: `selectionInstanceId` and `cardId`.
- `RESOLVE_NODE`: the current node ID after a terminal decision.

Expected sequence/step and command ID follow the ordinary dispatcher contract.
After a lost receipt or server restart, resubmit the exact original envelope or
use pending-command recovery. A repeated command ID returns its receipt and
cannot duplicate the card or materials. A different command trying the opposite
choice is rejected, even with the current sequence.

Offer identity is scoped by `(selectionInstanceId, cardId)`. The current offer
generator selects definitions without replacement and rejects duplicate
definition entries before consuming RNG. Repeated card definitions in a player's
collection remain distinct card instances; they are not duplicate offer options.
No second offer identity system or compatibility route was introduced.

## Presentation

The UI reuses the normal CardView and published card facts. It shows the authored
destination outside the card face (`metadata.rewardDestination` on the activity
node), and displays the option's published material quantity before confirmation.
The destination text is presentation metadata; only the engine's grant flow
decides where the instance actually goes. Hover, preview and rendering grant
nothing. English is the fallback, with Portuguese translations for the new copy.

`Reward Workshop` (`reward-lab`) is a small separate JSON proof: a real one-hit
victory, reward decision, required preparation and another encounter. Tests close
and recreate the API host on the same disposable store, semantically replay the
journal, recover the receipt and verify that reserved cards stay out of the next
draw. The main prototype uses the same reward/preparation components with its
15+5 composition.
