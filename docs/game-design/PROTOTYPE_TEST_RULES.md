# Tactical campaign: executable initial rules

Setting: `prototype-test`. Package: `heroscript.prototype-test`, version 1.1.0.
This is the first combat proof; the reward loop and three-encounter chapter are
subsequent implementation stages. Existing settings are unchanged.

## Build and circulation

The character owns 27 card instances. Fifteen hero cards and five cards of the
selected module form the active composition; seven remain outside circulation.
Repeated definitions are separate instances. The default module is Void;
Necromantic is the alternative. Changes outside a pending encounter use the
existing atomic composition editor and capture a new build at the next opening.

The neutral `owned` zone is collection storage, not the draw pile. At encounter
opening, only `deck.active` moves to `draw` and is shuffled by the run RNG. Five
cards enter `hand`. Each later hero activation draws two, up to eight. Ordinary
cards remain in hand; the graph retains its explicit ethereal exception. An
insufficient draw recycles discard once, then takes the available remainder.
Overflow leaves cards in their source instead of losing them.

The gameplay grant destination is `owned`, ready for the later reserve reward
flow. There is no free player-invokable draw action in the normal mode. Played
cards move to discard; selected Necromantic fuel moves to `banish`. Encounter
closure returns hand, discard, banish and remaining draw to collection, retaining
the same instances and permanent mutations.

## Resources and initial cards

The hero starts each attempt at 100 Health, zero Heat and zero Block. It has no
Energy or Mana pool. Heat ranges from 0 to 100 and loses ten at the end of the
hero's activation, clamped at zero. The preset uses integral heat changes.
Capacity conditions are part of the relevant card components, including the
boundary that permits reaching exactly 100. Heat is not a special engine role.

| Hero card | Initial copies | Heat | Function |
| --- | ---: | ---: | --- |
| Thermal cut | 4 | +10 | 8 physical damage before attributes, critical and defenses |
| Brace | 3 | +5 | Generate 8 Block before matching character modifiers |
| Ignite | 3 | +15 | 4 direct fire damage and two Burn stacks, base potency 2 each |
| Soak | 2 | +5 | Apply Wet for two affected-actor duration advances |
| Coolant | 2 | −20 | Cool down and restore 4 Health |
| Condense | 1 | +10 | Consume eligible captured fire potency in one proc |

Each module has two bolts, two guards and one release. Void pays one other hand
card to discard. Necromantic pays one discard card to temporary banishment.
Module cards add no Heat. Choices belong to the existing cost component, and the
player selects the exact fuel instance. The resolving card cannot pay itself.
The module's card identities, effects and allowed substitutions are authored
separately from the character's inscription slots or passive relic system.

## Scaling locations

All values below are content choices. The shared calculation engine consumes
requests and returns results; it does not recognize a thermal hero or a mana type.

| Input | Immediate damage | Burn capture | Tick | Condensation |
| --- | --- | --- | --- | --- |
| Card base/mutation | Composed base | Composed payload base | Stored quantity | Sum of consumed quantities |
| Attack/spell/elemental attributes | Matching configured tags/buckets | Not automatically reused | Not reapplied | Not reapplied |
| Residual potency attribute | Not automatic | Origin stage once | Already incorporated | Already incorporated |
| Heat | +0.5% per point for matching fire requests | Same origin bonus once | Not reapplied | Not reapplied |
| Critical chance/bonus | Global impact policy | No critical capture | No critical tick | No critical release in this preset |
| Wet | No bonus | No bonus | No bonus | ×1.25 for fire condensation only |
| Armor/Block | Target stage | Not captured | Current target stage | Current target stage |

Heat gain resolves before that card's ordinary effects. A later Heat increase
cannot amplify stored Burn potency again. The residual pipeline's release stage
does not add the older world's 150% release bonus. It aggregates each consumed
stack's potency, independently of remaining duration, and rounds at the target
stage after current modifiers/defenses.

Burn lasts three affected-actor end-of-activation duration advances. Wet lasts
two and never dispels Burn. Wet's target influence is restricted by both fire and
condensation tags. A support designation before an opponent's activation can
therefore spend its first duration advance at that opponent's upcoming ending.

Initial critical chance is 15%, bonus 100% per tier and Armor zero. Chance 100%
guarantees tier one (×2), and 200% guarantees tier two (×3). Each impact resolves
its own configured input. Fractional tier probability and all influence/capture
definitions remain in the published calculation policies.

## Support and attempts

The tactical sentinel acts after every three completed hero activations. Finish
targets the lowest current enemy Health; Pressure targets the highest. Both apply
Wet using the same ordinary ability, with canonical tie-breaking. It is an
ordinary participant explicitly excluded from outcome counting, so its survival
does not prevent the hero's defeat.

Defeat, draw and abandoned combat are retryable. Confirming the outcome records
the attempt without completing the encounter node or applying its exit rewards.
Persistent ownership, build, equipment and materials remain. Life resets to its
persistent maximum; Heat/Block reset to zero. A new encounter starts fresh combat
statuses, cooldowns and cadence, draws its seed from the continuing campaign RNG
and captures the current build. Historical attempts remain unchanged.

The normal mode uses pinned content and normal gameplay capabilities. The test
mode has explicit development capabilities, replay/timeline policies and
outside-combat versioned activation. Build/support revision migration requires
`RebindCompatible` and successful validation; it does not rewrite an active or
historical encounter.

## Evidence

`PrototypeTrialTests` uses a real API host for composition, retained hand, capacity,
both payments, temporary banishment, defeat, cleanup and another attempt.
`PrototypeTrialCalculationTests` executes controlled immutable snapshots against
the published processors for Heat boundaries, potency capture, Wet release and
critical tiers; it verifies no authoritative store write and repeated fingerprints.
`prototype_trial_online.gd` uses the normal Godot scenes against a disposable real
API store. Passing these checks verifies the rules; balance and chapter playtests
remain necessary.
