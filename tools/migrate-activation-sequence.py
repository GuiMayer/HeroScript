"""Explicit, idempotent SOURCE migration. Never reads/writes saves or bundles.
Usage: python tools/migrate-activation-sequence.py [paths...] [--write]
Default scope: data/configs. Without --write only reports intended changes.
"""
import argparse
import json
from pathlib import Path


def migrate_document(document):
    changed = []
    if not isinstance(document, dict):
        raise ValueError("Expected a definition map")
    for key, definition in document.items():
        if not isinstance(definition, dict) or "combatRulesId" not in definition:
            continue
        flow = definition.get("flow")
        if not isinstance(flow, dict):
            raise ValueError(f"{key}: missing or invalid flow")
        if "activationSequence" not in flow:
            flow["activationSequence"] = {"strategy": "PerRound"}
            changed.append(key)
    return changed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", default=["data/configs"])
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    files = set()
    for raw in args.paths:
        path = Path(raw)
        if not path.exists():
            parser.error(f"Path not found: {path}")
        files.update([path] if path.is_file() else
                     (file for file in path.rglob("*.json") if "combat-rules" in file.parts))
    prepared = []
    for path in sorted(files):
        document = json.loads(path.read_text(encoding="utf-8-sig"))
        ids = migrate_document(document)
        if ids:
            prepared.append((path, document, ids))
    # Validate all inputs before writing any source file.
    for path, document, ids in prepared:
        print(f"{'WRITE' if args.write else 'DRY-RUN'} {path}: {', '.join(ids)}")
        if args.write:
            path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Changed definitions: {sum(len(ids) for _, _, ids in prepared)}; files: {len(prepared)}")


if __name__ == "__main__":
    main()
