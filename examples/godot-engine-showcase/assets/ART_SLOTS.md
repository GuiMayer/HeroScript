# Art slots

Visuals are configured in `data/art_manifest.json`. These assets are original geometric SVG placeholders, not extracted Slay the Spire artwork.

## Contract

| Category | Source identity | Suggested final asset | Placement |
|---|---|---|---|
| cards | card definitionId | square illustration, 512×512 or higher | contain; centered above rules |
| actors | entity definitionId | transparent full silhouette, square 512×512 | contain; centered in target surface |
| activities | activity type | square vignette/icon, 512×512 | contain |
| backgrounds | combat | landscape 1600×600 or higher | cover; edges may crop |

Keep essential elements in the center 80% of square assets. For actors, use consistent feet/baseline within the square. Avoid embedded text: names, costs, translations, statuses and interaction borders belong to UI controls.

## Replace one placeholder

1. Add an asset under `assets/art/`, retaining its license/author information.
2. Change the slot's `path` in `data/art_manifest.json`.
3. Set `placeholder` to `false`, keeping the same slot ID.
4. Use `fit: contain` for uncropped silhouettes or `cover` for background fill.
5. Reimport the Godot project and restart the demo. Textures are cached for the process lifetime.
6. Check English/Portuguese, keyboard focus, reduced motion, 1280×720 and ultrawide.

Example:

```json
"actor_adept": {
  "path": "res://assets/art/adept.png",
  "fit": "contain",
  "placeholder": false
}
```

Map another content definition to this slot through `categories.actors`. A missing mapping or missing mapped file uses the category default. The UI never constructs an asset filename from a gameplay ID. Each ArtSlot exposes its stable identifier as `art_slot` metadata for inspection.

Do not replace the target button with a sprite hitbox: its focus, highlighting and click area are intentionally independent of the illustration. Do not add numeric gameplay data to the art manifest. All resource values, prices, legal actions and effects remain engine-owned.

## Files and responsibility

- `scripts/presentation/art_catalog.gd`: manifest, defensive reads and texture cache.
- `scripts/ui/art_slot.gd`: aspect handling and presentation-only feedback.
- `scripts/ui/card_view.gd`: reusable card surface and hover/play animation.
- `scripts/ui/actor_panel.gd`: portrait, engine intent and resources, usable in live/history views.
- `tests/gameplay_polish.gd`: fallback and integration/layout regression checks.

Unknown content must remain legible through its name and generic placeholder. An unmapped visual is not an invalid game definition.
