"""Author-time edits of Kenney Casino Audio 1.1 (CC0); never used by the game.

Install numpy/soundfile outside the game, then run with the extracted pack path.
Only selected, trimmed card/paper clips and provenance are shipped.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import soundfile as sf

RECIPES = {
    "hover": (0.085, ("card-place-1", "card-place-2", "card-place-4"), .65),
    "draw": (0.200, ("card-slide-2", "card-slide-4", "card-slide-6"), .8),
    "play": (0.260, ("card-place-1", "card-place-3", "card-place-4"), .8),
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pack", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--preview", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    records = []
    preview = []
    for event, (seconds, sources, peak) in RECIPES.items():
        for index, source in enumerate(sources, 1):
            path = args.pack / "Audio" / (source + ".ogg")
            samples, rate = sf.read(path, dtype="float32", always_2d=True)
            mono = samples.mean(axis=1)
            # Locate the first substantial transient, rejecting quiet lead-in/noise.
            envelope = np.convolve(np.abs(mono), np.ones(max(1, int(rate * .003))) / max(1, int(rate * .003)), mode="same")
            active = np.flatnonzero(envelope >= max(float(envelope.max()) * .22, .002))
            start = max(0, int(active[0]) - int(rate * .002)) if len(active) else 0
            clip = mono[start:start + round(rate * seconds)].copy()
            clip -= clip.mean()
            fade_in = min(len(clip), round(rate * .002))
            fade_out = min(len(clip), round(rate * (.012 if event == "hover" else .018)))
            clip[:fade_in] *= np.linspace(0, 1, fade_in)
            clip[-fade_out:] *= np.linspace(1, 0, fade_out)
            maximum = float(np.max(np.abs(clip)))
            if maximum > 0:
                clip *= peak / maximum
            target = args.output / f"card-{event}-{index}.ogg"
            sf.write(target, clip, rate, format="OGG", subtype="VORBIS")
            decoded, decoded_rate = sf.read(target)
            records.append({"event": event, "file": target.name, "source": source + ".ogg",
                "sourceSha256": digest(path), "sourceOffsetSeconds": round(start / rate, 6),
                "durationSeconds": round(len(decoded) / decoded_rate, 6), "sampleRate": decoded_rate,
                "channels": 1, "peak": round(float(np.max(np.abs(decoded))), 5), "sha256": digest(target)})
            if event == "hover":
                preview.extend([decoded, np.zeros(round(rate * .22))])
    (args.output / "provenance.json").write_text(json.dumps({
        "pack": "Kenney Casino Audio 1.1", "license": "CC0-1.0",
        "source": "https://kenney.nl/assets/casino-audio",
        "edits": "mono, onset trim, duration cap, DC removal, short fades, peak normalization; no loop or time stretch",
        "clips": records}, indent=2) + "\n", encoding="utf-8")
    # Review material, not a runtime asset or background loop.
    if args.preview:
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        sf.write(args.preview, np.concatenate(preview), rate, subtype="PCM_16")
    print(json.dumps(records, indent=2))


if __name__ == "__main__":
    main()
