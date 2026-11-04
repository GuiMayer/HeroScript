# CalculationBenchmark

Run in Release with the path of a JSON array of cases. Each case declares `name`, `request` (CalculationRequest) and `pipeline` (CalculationPipelineDefinition). The benchmark uses the real reducer with traces and fingerprints. It deliberately does not supply a formula evaluator; Formula buckets need a separate configured harness.

```powershell
dotnet run --project tools/CalculationBenchmark -c Release -- tests/Fixtures/calculation-baseline/grouping.json
```

Each case is calculated once for reference, warmed 20 times, then measured 200 times. Every measured fingerprint must equal the reference. Input JSON serialization must remain unchanged. Output is JSON Lines with value, fingerprint, runtime and p50/p95/p99 milliseconds. These numbers exclude resolver, networking, persistence and UI and cannot establish a POST latency target. Rerun after changes to code, runtime, content or hardware; a sample timing is not a CI performance threshold.
