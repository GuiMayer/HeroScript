# Inventário de endpoints HTTP

Este inventário enumera rotas declaradas pelos controllers na revisão da fonte desta publicação. Consulte a [referência REST](../../openapi/heroscript-v1.json), os [contratos compartilhados](contracts.md) e a [governança](governance.md). A presença de uma rota não garante estabilidade ou disponibilidade em todo setting.

A marcação “fora do OpenAPI” indica apenas que a combinação literal de método e caminho não foi encontrada na especificação; nomes diferentes de parâmetros podem produzir divergências. Ela não implica ausência de documentação em outros guias. As implementações vinculadas são a fonte para parâmetros e respostas enquanto a especificação estiver incompleta.

## ActorAttributes

[Código do controller](../../src/API/Controllers/ActorAttributesController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/character-attributes` | `GetCharacter` | Incluído |
| `GET` | `/api/v1/combats/{combatId}/actors/{actorId}/attributes` | `GetActor` | Incluído |

## AdminContent

[Código do controller](../../src/API/Controllers/AdminContentController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/admin/content/drafts/{draftId}` | `Get` | Fora do contrato atual |
| `POST` | `/api/v1/admin/content/drafts/{draftId}/validate` | `Validate` | Fora do contrato atual |
| `POST` | `/api/v1/admin/content/drafts/{draftId}/publish` | `Publish` | Fora do contrato atual |

## AdminContentReload

[Código do controller](../../src/API/Controllers/AdminContentReloadController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/admin/content/reload` | `Reload` | Fora do contrato atual |

## AdminSetting

[Código do controller](../../src/API/Controllers/AdminSettingController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/admin/settings` | `List` | Fora do contrato atual |
| `GET` | `/api/v1/admin/settings/{settingId}` | `Get` | Fora do contrato atual |
| `POST` | `/api/v1/admin/settings/{settingId}/validate` | `Validate` | Fora do contrato atual |
| `POST` | `/api/v1/admin/settings/{settingId}/drafts` | `CreateDraft` | Fora do contrato atual |

## CardModifierItem

[Código do controller](../../src/API/Controllers/CardModifierItemController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/card-modifier-items` | `Read` | Incluído |
| `GET` | `/api/v1/runs/{runId}/card-modifier-items/{itemInstanceId}/preview/{cardInstanceId}` | `Preview` | Incluído |

## CardPayment

[Código do controller](../../src/API/Controllers/CardPaymentController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/combats/{combatId}/cards/payment-preview` | `Preview` | Incluído |

## CardPresentation

[Código do controller](../../src/API/Controllers/CardPresentationController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/cards/presentations` | `Read` | Incluído |

## CardSelection

[Código do controller](../../src/API/Controllers/CardSelectionController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/card-selections` | `List` | Fora do contrato atual |
| `GET` | `/api/v1/runs/{runId}/card-selections/{selectionInstanceId}` | `Get` | Fora do contrato atual |

## CombatCard

[Código do controller](../../src/API/Controllers/CombatCardController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation` | `Evaluate` | Incluído |
| `GET` | `/api/v1/combats/{combatId}/cards/evaluations` | `EvaluatePlayableCards` | Incluído |

## CombatCommand

[Código do controller](../../src/API/Controllers/CombatCommandController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/combats/{combatId}/commands` | `Execute` | Incluído |

## Combat

[Código do controller](../../src/API/Controllers/CombatController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/combats/{combatId}/legal-actions` | `GetLegalActions` | Incluído |
| `GET` | `/api/v1/runs/{runId}/encounters/current` | `GetCurrentRunEncounter` | Fora do contrato atual |
| `GET` | `/api/v1/combats/{combatId}` | `GetState` | Incluído |
| `GET` | `/api/v1/combats/{combatId}/history` | `GetHistory` | Incluído |

## CombatJournal

[Código do controller](../../src/API/Controllers/CombatJournalController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/combats/{combatId}/journal` | `GetJournal` | Incluído |
| `POST` | `/api/v1/combats/{combatId}/verify` | `Verify` | Incluído |

## CombatResolution

[Código do controller](../../src/API/Controllers/CombatResolutionController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/combats/{combatId}/resolutions/{commandId}` | `Get` | Incluído |

## CombatSandbox

[Código do controller](../../src/API/Controllers/CombatSandboxController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sandbox/scenarios/validate` | `Validate` | Incluído |
| `POST` | `/api/v1/sandbox/runs` | `Launch` | Incluído |
| `GET` | `/api/v1/sandbox/runs/{runId}/scenario` | `GetScenario` | Incluído |
| `GET` | `/api/v1/sandbox/runs/{runId}/snapshot` | `GetSnapshot` | Incluído |

## CombatTimelineBranch

[Código do controller](../../src/API/Controllers/CombatTimelineBranchController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/combats/{combatId}/timeline/{sequence}/branches` | `Create` | Incluído |

## CombatTimeline

[Código do controller](../../src/API/Controllers/CombatTimelineController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/combats/{combatId}/timeline` | `Get` | Incluído |
| `GET` | `/api/v1/combats/{combatId}/timeline/{sequence}/state` | `GetState` | Incluído |

## Content

[Código do controller](../../src/API/Controllers/ContentController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/content/settings` | `GetSettings` | Incluído |
| `GET` | `/api/v1/content/revisions` | `GetRevisions` | Incluído |
| `GET` | `/api/v1/content/revisions/{revision}` | `GetRevision` | Incluído |
| `GET` | `/api/v1/content/{kind}` | `GetDefinitions` | Incluído |
| `GET` | `/api/v1/content/{kind}/{definitionId}` | `GetDefinition` | Incluído |
| `POST` | `/api/v1/content/validate` | `Validate` | Fora do contrato atual |

## DailyChallenge

[Código do controller](../../src/API/Controllers/DailyChallengeController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/challenges/daily/current` | `GetCurrent` | Incluído |
| `POST` | `/api/v1/challenges/daily/current/attempts` | `StartAttempt` | Fora do contrato atual |
| `POST` | `/api/v1/challenges/daily/current/submissions` | `Submit` | Fora do contrato atual |
| `GET` | `/api/v1/challenges/daily/current/leaderboard` | `GetLeaderboard` | Fora do contrato atual |

## DeckCompartment

[Código do controller](../../src/API/Controllers/DeckCompartmentController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/deck-compartments` | `Read` | Fora do contrato atual |
| `POST` | `/api/v1/runs/{runId}/deck-compartments/preview` | `Preview` | Fora do contrato atual |

## DeckLoadout

[Código do controller](../../src/API/Controllers/DeckLoadoutController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/deck-loadout` | `Read` | Incluído |
| `POST` | `/api/v1/runs/{runId}/deck-loadout/preview` | `Preview` | Incluído |

## Diagnostics

[Código do controller](../../src/API/Controllers/DiagnosticsController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/admin/diagnostics/cache/stats` | `GetCacheStatistics` | Fora do contrato atual |
| `GET` | `/api/v1/admin/diagnostics/cache/stats/{cacheName}` | `GetCacheStatisticsByName` | Fora do contrato atual |
| `POST` | `/api/v1/admin/diagnostics/cache/invalidate/{cacheName}` | `InvalidateCache` | Fora do contrato atual |
| `POST` | `/api/v1/admin/diagnostics/cache/invalidate/{cacheName}/key` | `InvalidateCacheKey` | Fora do contrato atual |
| `GET` | `/api/v1/admin/diagnostics/health/cache` | `GetCacheHealth` | Fora do contrato atual |

## DurableEvents

[Código do controller](../../src/API/Controllers/DurableEventsController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/events` | `GetRunEvents` | Incluído |
| `GET` | `/api/v1/combats/{combatId}/events` | `GetCombatEvents` | Fora do contrato atual |
| `GET` | `/api/v1/runs/{runId}/events/stream` | `StreamRunEvents` | Incluído |
| `GET` | `/api/v1/combats/{combatId}/events/stream` | `StreamCombatEvents` | Fora do contrato atual |

## Formula

[Código do controller](../../src/API/Controllers/FormulaController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/simulations/formulas` | `GetFormulas` | Fora do contrato atual |
| `GET` | `/api/v1/simulations/formulas/{name}` | `GetFormula` | Fora do contrato atual |
| `POST` | `/api/v1/simulations/formulas/evaluate` | `Evaluate` | Fora do contrato atual |

## GameplayEvent

[Código do controller](../../src/API/Controllers/GameplayEventController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/content/{settingId}/revisions/{revision}/gameplay-events/catalog` | `Catalog` | Incluído |
| `GET` | `/api/v1/content/{settingId}/revisions/{revision}/gameplay-responses/schema` | `Schema` | Incluído |
| `GET` | `/api/v1/runs/{runId}/commits/{sequence}/gameplay-events` | `Inspect` | Incluído |

## InteractionSession

[Código do controller](../../src/API/Controllers/InteractionSessionController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/session` | `Read` | Incluído |

## Loadout

[Código do controller](../../src/API/Controllers/LoadoutController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/loadout` | `Read` | Incluído |
| `POST` | `/api/v1/runs/{runId}/loadout/preview` | `Preview` | Incluído |

## MathExpression

[Código do controller](../../src/API/Controllers/MathExpressionController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/simulations/math/expression/evaluate` | `Evaluate` | Fora do contrato atual |

## Operation

[Código do controller](../../src/API/Controllers/OperationController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/operations` | `GetOperations` | Fora do contrato atual |
| `GET` | `/api/v1/operations/{name}` | `GetOperation` | Fora do contrato atual |
| `GET` | `/api/v1/operations/categories` | `GetOperationsByCategory` | Fora do contrato atual |

## Preparation

[Código do controller](../../src/API/Controllers/PreparationController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/preparations` | `List` | Fora do contrato atual |
| `GET` | `/api/v1/runs/{runId}/preparations/{preparationInstanceId}` | `Get` | Fora do contrato atual |

## Profile

[Código do controller](../../src/API/Controllers/ProfileController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/profiles/{playerId}` | `Get` | Incluído |
| `GET` | `/api/v1/profiles/{playerId}/stats` | `GetStats` | Incluído |
| `GET` | `/api/v1/profiles/{playerId}/unlocks` | `GetUnlocks` | Incluído |
| `GET` | `/api/v1/profiles/{playerId}/achievements` | `GetAchievements` | Incluído |
| `GET` | `/api/v1/profiles/{playerId}/runs` | `GetRuns` | Incluído |

## RunBranch

[Código do controller](../../src/API/Controllers/RunBranchController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/runs/{runId}/branches` | `Create` | Incluído |
| `GET` | `/api/v1/runs/{runId}/branches` | `List` | Incluído |
| `GET` | `/api/v1/runs/{runId}/branch-tree` | `GetTree` | Incluído |

## RunCapability

[Código do controller](../../src/API/Controllers/RunCapabilityController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/capabilities` | `Get` | Fora do contrato atual |

## RunCommand

[Código do controller](../../src/API/Controllers/RunCommandController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/runs/{runId}/commands` | `Execute` | Incluído |

## RunContentActivation

[Código do controller](../../src/API/Controllers/RunContentActivationController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/content/activation-preview` | `Get` | Fora do contrato atual |

## Run

[Código do controller](../../src/API/Controllers/RunController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/runs` | `StartRun` | Incluído |
| `GET` | `/api/v1/runs/{runId}` | `GetState` | Incluído |
| `GET` | `/api/v1/runs/{runId}/map` | `GetMap` | Incluído |
| `GET` | `/api/v1/runs/{runId}/available-commands` | `GetAvailableCommands` | Incluído |
| `GET` | `/api/v1/runs` | `ListRuns` | Incluído |
| `GET` | `/api/v1/runs/{runId}/card-zones` | `GetCardZones` | Incluído |
| `GET` | `/api/v1/runs/{runId}/relics` | `GetRelics` | Fora do contrato atual |
| `GET` | `/api/v1/runs/{runId}/cards/{cardInstanceId}` | `GetCard` | Fora do contrato atual |
| `GET` | `/api/v1/runs/{runId}/cards/{cardInstanceId}/upgrade-options` | `GetCardUpgradeOptions` | Fora do contrato atual |
| `GET` | `/api/v1/runs/{runId}/cards/{cardInstanceId}/transformation-preview` | `PreviewTransformation` | Incluído |

## RunJournal

[Código do controller](../../src/API/Controllers/RunJournalController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/journal` | `GetJournal` | Incluído |
| `GET` | `/api/v1/runs/{runId}/commits` | `GetCommits` | Incluído |
| `GET` | `/api/v1/runs/{runId}/commits/{sequence}` | `GetCommit` | Incluído |
| `GET` | `/api/v1/runs/{runId}/timeline` | `GetTimeline` | Incluído |
| `GET` | `/api/v1/runs/{runId}/commits/{sequence}/presentation` | `GetPresentation` | Incluído |
| `POST` | `/api/v1/runs/{runId}/verify` | `Verify` | Incluído |

## Shop

[Código do controller](../../src/API/Controllers/ShopController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/shops` | `List` | Fora do contrato atual |
| `GET` | `/api/v1/runs/{runId}/shops/{shopInstanceId}` | `Get` | Fora do contrato atual |

## Simulation

[Código do controller](../../src/API/Controllers/SimulationController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `POST` | `/api/v1/simulations` | `Create` | Incluído |
| `GET` | `/api/v1/simulations/{simulationId}` | `Get` | Incluído |
| `GET` | `/api/v1/simulations/{simulationId}/result` | `GetResult` | Incluído |

## SupportLoadout

[Código do controller](../../src/API/Controllers/SupportLoadoutController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/runs/{runId}/support-loadout` | `Read` | Incluído |
| `POST` | `/api/v1/runs/{runId}/support-loadout/preview` | `Preview` | Incluído |

## System

[Código do controller](../../src/API/Controllers/SystemController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/health/live` | `Live` | Incluído |
| `GET` | `/api/v1/health/ready` | `Ready` | Incluído |
| `GET` | `/api/v1/version` | `Version` | Incluído |
| `GET` | `/api/v1/capabilities` | `Capabilities` | Incluído |

## Telemetry

[Código do controller](../../src/API/Controllers/TelemetryController.cs)

| Método | Caminho | Operação | OpenAPI |
| --- | --- | --- | --- |
| `GET` | `/api/v1/admin/telemetry` | `GetEvents` | Fora do contrato atual |
| `GET` | `/api/v1/admin/telemetry/combats/{combatId}` | `GetCombatEvents` | Fora do contrato atual |
| `GET` | `/api/v1/admin/telemetry/stream` | `StreamEvents` | Fora do contrato atual |
| `GET` | `/api/v1/admin/telemetry/combats/{combatId}/stream` | `StreamCombatEvents` | Fora do contrato atual |
| `GET` | `/api/v1/admin/telemetry/{eventId}` | `GetEvent` | Fora do contrato atual |
| `GET` | `/api/v1/admin/telemetry/categories` | `GetCategories` | Fora do contrato atual |
| `GET` | `/api/v1/admin/telemetry/severities` | `GetSeverities` | Fora do contrato atual |
| `DELETE` | `/api/v1/admin/telemetry` | `ClearHistory` | Fora do contrato atual |
