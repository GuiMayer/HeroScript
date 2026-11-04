"""Exercise startup cache hits, corruption and source changes against a real isolated API."""
import argparse
import json
import os
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--dll", type=Path, required=True)
parser.add_argument("--resources", type=Path, required=True)
args = parser.parse_args()
root = Path.cwd()
dll = args.dll.resolve(strict=True)
with tempfile.TemporaryDirectory(prefix="heroscript-startup-") as temporary:
    base = Path(temporary)
    resources = base / "resources"
    shutil.copytree(args.resources.resolve(strict=True), resources)
    store = base / "store"
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    url = f"http://127.0.0.1:{port}"
    env = os.environ.copy()
    env.update({"ASPNETCORE_URLS": url, "HEROSCRIPT_STARTUP_TIMINGS": "1",
                "Mods__PackageRoots__0": str(resources),
                "Persistence__ContentStorePath": str(store),
                "Persistence__RunStatePath": str(base / "runs"),
                "Persistence__OperationalTelemetryPath": str(base / "telemetry")})

    def run(name):
        logfile = base / (name + ".log")
        with logfile.open("w", encoding="utf-8") as output:
            proc = subprocess.Popen(["dotnet", str(dll)], cwd=root / "src/API",
                                    env=env, stdout=output, stderr=output)
            try:
                started = time.monotonic()
                while time.monotonic() - started < 90 and proc.poll() is None:
                    try:
                        with urllib.request.urlopen(url + "/api/v1/content/settings", timeout=1) as response:
                            catalog = json.load(response)
                            break
                    except OSError:
                        time.sleep(.1)
                else:
                    raise RuntimeError(f"API failed to start: {logfile.read_text()}")
            finally:
                if proc.poll() is None:
                    proc.terminate()
                    proc.wait(timeout=20)
        events = [json.loads(line) for line in logfile.read_text().splitlines()
                  if line.startswith('{"kind":"startup_timing"')]
        return catalog, events

    cold, cold_events = run("cold")
    index = store / "startup-index-v1.json"
    saved = json.loads(index.read_text())
    warm, warm_events = run("warm")
    assert cold == warm
    assert any(event["phase"] == "setting_compile" for event in cold_events)
    assert sum(event["phase"] == "setting_cached" for event in warm_events) == len(saved["Revisions"])
    index.write_text("{invalid", encoding="utf-8")
    recovered, recovered_events = run("corrupted")
    assert recovered == cold
    assert any(event["phase"] == "setting_compile" for event in recovered_events)
    (resources / "cache-invalidation-probe.txt").write_text("changed", encoding="utf-8")
    changed, changed_events = run("changed")
    assert changed == cold
    assert any(event["phase"] == "setting_compile" for event in changed_events)
    assert json.loads(index.read_text())["Fingerprint"] != saved["Fingerprint"]
    print("PASS cold and cached catalogs, corrupt-index recovery and input invalidation")
