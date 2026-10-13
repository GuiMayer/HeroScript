# Replay read baseline

Read existing data without command lookup, appends, index repair or gameplay execution:

`dotnet run -c Release --project tools/ReplayReadBenchmark -- <store> <run-id>`

Generate a new isolated storage-only fixture and measure it:

`dotnet run -c Release --project tools/ReplayReadBenchmark -- --fixture .runtime/replay-fixture-125 125 16`

Repeat with 25/125/500/1000/4001 commands and different payload KiB. The output
reports allocations, read/projection time and payload bytes. Sample zero is
first read of that store instance; it is not a guarantee of cold OS cache/JIT.
No FindCommand calls: that operation can repair indexes.

Fixtures validate storage contracts but are not semantic gameplay runs.
Use real isolated API journeys for semantic verification/end-to-end QA.
The directory must not exist; existing runs are never overwritten.
