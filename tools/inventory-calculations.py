"""Record authored calculation references without executing game expressions."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("output", type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
keys = {"formula", "formulavalue", "calculationpipelineid", "calculationchannel", "pipelineid", "defaultcalculationpipelines", "calculationpipelineids"}
files = []
references = []
def walk(value, pointer, source):
    if isinstance(value, dict):
        for key in sorted(value):
            child = pointer + "/" + key.replace("~", "~0").replace("/", "~1")
            if key.lower() in keys:
                references.append({"source": source, "pointer": child, "value": value[key]})
            walk(value[key], child, source)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            walk(child, pointer + "/" + str(index), source)
for file in sorted((root / "data/configs").rglob("*.json")):
    raw = file.read_bytes()
    source = file.relative_to(root).as_posix()
    files.append({"source": source, "sha256": hashlib.sha256(raw).hexdigest()})
    walk(json.loads(raw.decode("utf-8-sig")), "", source)
payload = {"scope": "Working-tree authored references; inheritance and runtime publication are not resolved", "files": files, "references": references}
encoded = (json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode()
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_bytes(encoded)
print(json.dumps({"files": len(files), "references": len(references), "sha256": hashlib.sha256(encoded).hexdigest()}))
