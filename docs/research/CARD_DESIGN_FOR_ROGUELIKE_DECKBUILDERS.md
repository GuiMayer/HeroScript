# Card design for roguelike deckbuilders

## Goal

Define a readable, data-driven card surface for the Godot showcase without
moving any gameplay authority out of HeroScript. The card is a view of an
engine-owned container: costs, effects, upgrades and contextual calculations
come from the REST projection; colors, typography and art placement are client
presentation data.

## Research findings

- Magic places the name and mana cost in the top band, with the mana cost at
  the top-right. It gives the type line and rules text their own regions. This
  creates a stable scan path: identity, affordability, category, then exact
  behavior. Sources: [Anatomy of a Magic Card](https://magic.wizards.com/en/news/feature/anatomy-magic-card-2006-10-21)
  and [How to Play Magic](https://magic.wizards.com/en/how-to-play).
- Magic's symbols encode both amount and mana type. HeroScript can generalize
  that grammar: every configured resource gets a cost color, a short symbol and
  a localized name. A card may show multiple resource chips, and an unknown or
  modded resource receives a neutral fallback instead of becoming unreadable.
- Slay the Spire makes permanent upgrades visible with a `+` suffix and changed
  presentation, while the upgrade may change a value, behavior or cost. The UI
  therefore must not assume that an upgrade always means “more damage.” Source:
  [Slay the Spire upgrade reference](https://slaythespire.wiki.gg/wiki/Upgrade).
- Color cannot be the only carrier of meaning. Resource chips also need a
  symbol, amount and accessible full-name tooltip; modification badges need a
  glyph and text. This follows the platform guidance to pair color with labels
  or symbols and retain sufficient contrast. Sources: [Apple accessibility](https://developer.apple.com/design/human-interface-guidelines/accessibility)
  and [Apple color](https://developer.apple.com/design/human-interface-guidelines/color).
- Color identity and cost identity are different concerns. Magic intentionally
  connects its color pie to mechanical identity. In this demo, the requested
  rule is narrower: resource colors communicate payment only. Attack, skill and
  power framing remains a separate card-category signal and never changes the
  meaning of a resource chip. Background: [Magic color-pie design](https://magic.wizards.com/en/news/making-magic/pie-fights-2016-11-14).

## Implemented information hierarchy

1. **Header:** localized card name at left; one chip per engine-published cost
   at right.
2. **Art:** a stable replaceable art slot. While its manifest entry is marked as
   a placeholder, it carries the localized card name so every prototype card is
   identifiable even without final art.
3. **Type line:** card category and engine-owned rarity when inspection data is
   available.
4. **Effects:** a dedicated rules region made from the engine's previewed
   applications. Cost applications are deliberately excluded.
5. **Change state:** permanent upgrades and contextual calculations are shown
   independently:
   - `UPGRADED` means the card instance has an applied permanent upgrade;
   - `BUFFED` / `WEAKENED` means an actor, status, relic or modifier changed a
     calculation in the positive / negative direction;
   - `MODIFIED` means a target, encounter or game-mode rule changed resolution,
     or the contextual change is not safely classifiable as a buff/debuff.
6. **Interaction footer:** playability and interaction instructions remain
   separate from rules text.

## Authority boundaries

| Concern | Authority |
|---|---|
| Resource IDs, amounts and alternative costs | HeroScript legal actions/evaluation |
| Effects and calculated values | HeroScript preview applications and calculation traces |
| Permanent upgrade state | HeroScript card instance and `appliedUpgrades` |
| Buff/debuff/modification evidence | HeroScript calculation contribution traces |
| Resource cost color and symbol | Godot `data/presentation.json` |
| Art asset and placeholder flag | Godot `data/art_manifest.json` |
| Layout, animation and typography | Godot `CardView` |

The client does not recalculate affordability, damage, block, healing or
scaling. It only translates immutable engine evidence into a visual state.

## Extensibility rules

- Adding a resource requires no new UI branch. Give it a `symbol` and
  `cost_color` in presentation JSON; otherwise the neutral fallback is used.
- Multiple costs render in engine order. Alternative cost options remain
  explicit choices in the existing action chooser.
- Replacing artwork changes only the art manifest. Setting `placeholder` to
  `false` removes the overlaid prototype name automatically.
- New calculation source kinds default to `MODIFIED`, which is honest and
  forward-compatible until the presentation policy deliberately classifies
  them.
