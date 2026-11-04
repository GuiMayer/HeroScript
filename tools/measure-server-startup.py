"""Measure an isolated API executable using a fresh process and a dedicated store."""
import argparse
import json
import os
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--dll", required=True, type=Path)
parser.add_argument("--repo", type=Path, default=Path.cwd())
parser.add_argument("--store", required=True, type=Path)
parser.add_argument("--port", type=int, default=18573)
parser.add_argument("--samples", type=int, default=10)
args = parser.parse_args()
if args.samples < 1 or not 1024 <= args.port <= 65535:
    parser.error("samples must be positive and port must be between 1024 and 65535")
repo = args.repo.resolve()
dll = args.dll.resolve(strict=True)
store = args.store.resolve()
store.mkdir(parents=True, exist_ok=True)
url = f"http://127.0.0.1:{args.port}/api/v1/health/ready"
try:
    with urllib.request.urlopen(url, timeout=1):
        raise SystemExit("The measurement port already has a running server.")
except urllib.error.HTTPError:
    raise SystemExit("The measurement port already has a running HTTP server.")
except urllib.error.URLError:
    pass

durations = []
for sample in range(args.samples):
    env = os.environ.copy()
    env.update({
        "ASPNETCORE_URLS": f"http://127.0.0.1:{args.port}",
        "HEROSCRIPT_STARTUP_TIMINGS": "1",
        "Persistence__RunStatePath": str(store / "runs"),
        "Persistence__ContentStorePath": str(store / "content"),
        "Persistence__OperationalTelemetryPath": str(store / "telemetry"),
    })
    log_path = store / f"startup-{sample + 1}.log"
    with log_path.open("w", encoding="utf-8") as log:
        start = time.monotonic()
        process = subprocess.Popen(["dotnet", str(dll)], cwd=repo / "src" / "API",
                                   env=env, stdout=log, stderr=subprocess.STDOUT)
        try:
            while time.monotonic() - start < 120:
                if process.poll() is not None:
                    raise RuntimeError(f"API exited during startup. See {log_path}")
                try:
                    with urllib.request.urlopen(url, timeout=1) as response:
                        data = json.load(response)
                        if response.status == 200 and data.get("status") == "ready":
                            durations.append((time.monotonic() - start) * 1000)
                            break
                except (urllib.error.URLError, TimeoutError, ConnectionError):
                    time.sleep(0.1)
            else:
                raise TimeoutError(f"API did not become ready. See {log_path}")
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=20)

ordered = sorted(durations)
percentile = lambda fraction: ordered[max(0, int(__import__("math").ceil(len(ordered) * fraction)) - 1)]
print(json.dumps({"samples": len(ordered), "scope": "direct API process to HTTP readiness; OS cache not controlled",
                  "durations_ms": durations, "p50_ms": percentile(.5),
                  "p95_ms": percentile(.95), "max_ms": max(ordered)}, indent=2))
