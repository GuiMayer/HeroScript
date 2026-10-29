# Gameplay response migration preflight

This tool reads one configuration's own `Resources/status-effects`, `Resources/relics` and `Resources/phase-sequences` directories. Run it separately for inherited configurations. It invokes the Core typed converters, preserves unrelated JSON fields, and reports before/after definitions, proposed documents and file SHA-256 hashes. It does not edit content, publish revisions, migrate saves, or change the engine version.

```powershell
dotnet run --project tools/GameplayResponseMigration -- --setting data/configs/ascendant --output migration-report.json
```

Exit code 0 means the examined conversions can be proposed; it does **not** mean publication is enabled. Exit code 2 reports input errors, ambiguous mixed formats or unsupported migrations. The report marks `publicationReady: false` while the cutover is incomplete.

External formulas requiring expansion and stochastic/continuation identities without equivalence coverage remain rejected. There is no `--apply` flag yet: application with stale-file protection, complete package validation and publication cutover are the remaining part of implementation stage 11. Review the plan's implementation progress before using a proposed document as content.

Regression checks:

```powershell
dotnet build tools/GameplayResponseMigration -c Release --artifacts-path .runtime/tools
python -X utf8 -m unittest discover -s tools/GameplayResponseMigration/tests -v
```
