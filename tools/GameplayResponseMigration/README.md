# Gameplay response migration

The default operation is read-only. Core performs the typed conversion; the report contains before/after definitions and SHA-256 preimages. Reports must be outside the setting directory.

```powershell
dotnet run --project tools/GameplayResponseMigration -- --setting data/configs/ascendant --output migration-report.json
dotnet run --project tools/GameplayResponseMigration -- --root data/configs --apply .runtime/response-migration-backup
```

Apply requires the complete package root. It locks the affected files, checks the preimages, compiles every discovered setting with the proposed documents, validates their complete graphs, and creates a new backup directory before writing. A caught write failure restores the original bytes. The backup contains every original file plus the reviewed migration manifest; after a process/power failure, restore those files before retrying. Publication remains a separate operation: no captured bundle or save is rewritten.

Current authoring uses content manifest schema 2, with canonical responses. Schema 1 remains readable for historical bundles. EngineVersion 34 is unchanged because historical inputs/execution remain compatible; new content has a distinct schema/revision. Unsupported external formula expansion or stochastic/continuation identity conversions fail explicitly.

The shipped packages are version 1.1.0 after migration. Internal historical adapters remain only for schema-1 captured content; schema-2 publication rejects their fields when nonempty. There is one effect dispatcher for both paths.

Regression checks:

```powershell
dotnet build tools/GameplayResponseMigration -c Release --artifacts-path .runtime/tools
python -X utf8 -m unittest discover -s tools/GameplayResponseMigration/tests -v
```
