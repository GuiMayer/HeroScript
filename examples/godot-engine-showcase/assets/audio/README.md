# Demo sound effects

Selections from Kenney Interface Sounds 1.0, Impact Sounds 1.0 and Casino Audio 1.1.
All packs are CC0; original licenses are included alongside the selected clips.
Sources: https://kenney.nl/assets/interface-sounds, https://kenney.nl/assets/impact-sounds
and https://kenney.nl/assets/casino-audio.

`kenney-cards` contains nine edited card/paper one-shots, not synthetic UI scrapes:
hover 85 ms (card-place transients), draw 200 ms (card-slide), play 260 ms (card-place).
The hover is limited to one simultaneous voice. Each event has three variants.
Edits: remove quiet lead-in/DC, mono, short fades, trim and normalize peak; no looping.
`kenney-cards/provenance.json` records every original source, edit offset and SHA-256.
The complete downloaded pack stays in `.runtime/audio-packs/casino`, outside the game.
Other UI/impact clips retain their original OGG bytes; their mappings are unchanged.

Author-time reproduction: `tools/prepare-card-audio.py <extracted-pack> <output>`
with Python packages `numpy` and `soundfile`. Optional `--preview <path.wav>` exports
a review sequence; no Python, editing or network access is needed by the game.

Event mapping and per-event mix: `res://data/audio_manifest.json`.
No generated tones, permanent background drone, or runtime download fallback.
These samples are cosmetic feedback; they do not define gameplay rules.
