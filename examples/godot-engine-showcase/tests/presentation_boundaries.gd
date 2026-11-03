extends SceneTree
## Etapa 3: Typed Presentation Models & Boundary Validation Tests.
## Verifies missing fields, nulls, unexpected types, UUID/ID checks, extension preservation,
## rejection of synthetic legal actions, and protection against delayed/out-of-order responses.

const ViewModels = preload("res://scripts/presentation/view_models.gd")
const Boundary = preload("res://scripts/contracts/presentation_boundary.gd")
const CombatPresenter = preload("res://scripts/presentation/combat_presenter.gd")
const CardZonePresenter = preload("res://scripts/presentation/card_zone_presenter.gd")
const VisualContext = preload("res://scripts/ui/visual_context.gd")

var failures := 0

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok:
		failures += 1

func _run() -> void:
	var tr = VisualContext.English.new()

	# 1. UUID and ID validation
	check(Boundary.is_valid_uuid("550e8400-e29b-41d4-a716-446655440000"), "accepts canonical 36-char UUID")
	check(not Boundary.is_valid_uuid("550e8400-e29b-41d4-a716-44665544000Z") and not Boundary.is_valid_uuid(null) and not Boundary.is_valid_uuid(42),
		"rejects malformed, null, or numeric UUID values")
	check(Boundary.is_valid_id("card-instance-1") and not Boundary.is_valid_id("") and not Boundary.is_valid_id("  bad ") and not Boundary.is_valid_id(null),
		"validates non-empty clean string identifiers")

	# 2. Typed models preserve unknown/setting extensions and defensive copies
	var raw_card := {
		"cardInstanceId": "c-101",
		"definitionId": "ember_bolt",
		"costs": [{"resourceId": "energy", "amount": 2}],
		"settingExtension": {"element": "pyre", "resonance": 3}
	}
	var card_model := ViewModels.Card.new(raw_card)
	raw_card.settingExtension.resonance = 99
	var ext := card_model.extensions(ViewModels.Card.KNOWN_KEYS)
	check(card_model.is_valid() and card_model.instance_id == "c-101" and card_model.definition_id == "ember_bolt",
		"Card model exposes typed identity properties")
	check(ext.get("settingExtension", {}).get("resonance") == 3,
		"Card model preserves unknown setting extension fields without aliasing source dictionary")

	var interaction := ViewModels.InteractionState.new({
		"selectedCardId": "c-101",
		"hoveredCardId": "c-102",
		"selectedTargetId": "enemy-1",
		"submitting": false,
		"scopeKey": "run-1:combat-1"
	})
	check(interaction.selected_card_id == "c-101" and interaction.selected_target_id == "enemy-1" and not interaction.submitting,
		"InteractionState model exposes typed local interaction fields")

	# 3. Missing fields, nulls, and unexpected types in boundary adapters
	var actors_res := Boundary.adapt_actors([
		null,
		"not-a-dict",
		{"instanceId": ""},
		{"instanceId": 404},
		{"instanceId": "hero", "definitionId": "adept", "resources": "bad-type", "statuses": null, "customTrait": "swift"}
	])
	check(not actors_res.ok and actors_res.errors.size() == 6 and actors_res.models.size() == 1,
		"adapt_actors flags nulls, wrong types, and missing IDs while keeping valid actor")
	check(actors_res.items[0].get("instanceId") == "hero" and actors_res.items[0].get("resources") == {} and actors_res.items[0].get("customTrait") == "swift",
		"adapt_actors sanitizes malformed nested collections and preserves custom actor extensions")

	var cards_res := Boundary.adapt_cards([
		null,
		{"cardInstanceId": null, "definitionId": "strike"},
		{"cardInstanceId": 12, "definitionId": "strike"},
		{"cardInstanceId": "valid-card", "definitionId": "strike", "customMeta": {"foil": true}}
	])
	check(not cards_res.ok and cards_res.models.size() == 1 and cards_res.items[0].get("customMeta", {}).get("foil") == true,
		"adapt_cards rejects null/non-string cardInstanceId and retains valid card with extensions")

	# 4. Legal actions never fabricate synthetic valid actions from malformed DTOs
	var actions_res := Boundary.adapt_legal_actions([
		null,
		{},
		{"command": null},
		{"command": "invalid"},
		{"command": {}},
		{"command": {"cardInstanceId": null, "actionType": null}},
		{"command": {"cardInstanceId": 999}},
		{"source": "Player", "command": {"cardInstanceId": "valid-card", "actionType": "PLAY_CARD", "targetIds": ["enemy-1", null, 42]}}
	])
	check(not actions_res.ok and actions_res.models.size() == 1,
		"adapt_legal_actions rejects every malformed candidate without inventing defaults")
	var valid_action: ViewModels.LegalAction = actions_res.models[0]
	check(valid_action.card_instance_id == "valid-card" and valid_action.action_type == "PLAY_CARD" and valid_action.target_ids == ["enemy-1"],
		"LegalAction model filters non-string target IDs and exposes typed command metadata")

	# 5. Zone boundary validation
	var zones_res := Boundary.adapt_zones({
		"zones": [
			null,
			{"zoneId": "", "count": 2},
			{"zoneId": "draw", "count": -3},
			{"zoneId": "discard", "count": "invalid"},
			{"zoneId": "hand", "count": 2, "contentsVisible": true, "allowsCardPlay": true, "cards": [
				{"cardInstanceId": "valid-card", "definitionId": "strike"},
				{"cardInstanceId": null}
			], "customZoneMeta": "arc"}
		]
	})
	check(not zones_res.ok and zones_res.zones.size() == 1,
		"adapt_zones rejects zones with empty ID, negative count, or non-numeric count")
	check(zones_res.zones[0].get("cards", []).size() == 1 and zones_res.zones[0].get("customZoneMeta") == "arc",
		"adapt_zones filters invalid cards inside visible zones and preserves zone metadata")

	# 6. Revision context and delayed/out-of-order response protection
	var run_dto := {"runId": "run-alpha", "branchId": "main", "sequence": 5, "contentRevision": "rev-2"}
	var combat_dto := {"combatId": "combat-1", "runId": "run-alpha", "step": 3, "actors": [{"instanceId": "hero", "sideId": "player"}]}
	var valid_candidates := [{"command": {"cardInstanceId": "c1", "actionType": "PLAY_CARD", "targetIds": ["hero"]}}]
	var zones_dto := {"zones": [{"zoneId": "hand", "count": 1, "contentsVisible": true, "allowsCardPlay": true, "cards": [{"cardInstanceId": "c1", "definitionId": "strike"}]}]}

	var presenter := CombatPresenter.new(run_dto, combat_dto, valid_candidates, tr, {}, zones_dto)
	presenter.accept_evaluations([
		{"version": {"runId": "run-alpha", "runSequence": 5, "combatId": "combat-1", "combatStep": 3, "contentRevision": "rev-2"}, "evaluation": {"cardInstanceId": "c1", "failureReasons": ["Current reason"]}},
		{"version": {"runId": "run-other", "runSequence": 5, "combatId": "combat-1", "combatStep": 3, "contentRevision": "rev-2"}, "evaluation": {"cardInstanceId": "c1", "failureReasons": ["Wrong run"]}},
		{"version": {"runId": "run-alpha", "runSequence": 4, "combatId": "combat-1", "combatStep": 3, "contentRevision": "rev-2"}, "evaluation": {"cardInstanceId": "c1", "failureReasons": ["Stale sequence"]}},
		{"version": {"runId": "run-alpha", "runSequence": 5, "combatId": "combat-1", "combatStep": 2, "contentRevision": "rev-2"}, "evaluation": {"cardInstanceId": "c1", "failureReasons": ["Stale combat step"]}},
		{"version": {"runId": "run-alpha", "runSequence": 5, "combatId": "combat-1", "combatStep": 3, "contentRevision": "rev-1"}, "evaluation": {"cardInstanceId": "c1", "failureReasons": ["Stale revision"]}}
	])
	check(presenter.unavailable_reason("c1") == "Current reason",
		"CombatPresenter rejects delayed evaluations from wrong run, sequence, combatStep, or contentRevision")

	# 7. StateKeeper preserves last valid snapshot when malformed or delayed snapshot arrives
	var keeper := Boundary.StateKeeper.new()
	var first_accept := keeper.accept_snapshot({"run": run_dto, "combat": combat_dto, "actions": valid_candidates, "zones": zones_dto})
	check(first_accept.accepted and keeper.last_identity.run_sequence == 5 and keeper.last_identity.combat_step == 3,
		"StateKeeper accepts valid initial snapshot")

	var stale_seq := keeper.accept_snapshot({
		"run": {"runId": "run-alpha", "branchId": "main", "sequence": 4},
		"combat": combat_dto,
		"actions": valid_candidates,
		"zones": zones_dto
	})
	check(not stale_seq.accepted and int(stale_seq.snapshot.run.sequence) == 5,
		"StateKeeper rejects delayed response with older runSequence and preserves last valid state")

	var stale_step := keeper.accept_snapshot({
		"run": run_dto,
		"combat": {"combatId": "combat-1", "runId": "run-alpha", "step": 2},
		"actions": valid_candidates,
		"zones": zones_dto
	})
	check(not stale_step.accepted and int(stale_step.snapshot.combat.step) == 3,
		"StateKeeper rejects delayed response with older combatStep and preserves last valid state")

	var mismatched_run := keeper.accept_snapshot({
		"run": run_dto,
		"combat": {"combatId": "combat-1", "runId": "run-beta", "step": 4},
		"actions": valid_candidates,
		"zones": zones_dto
	})
	check(not mismatched_run.accepted and str(mismatched_run.snapshot.combat.runId) == "run-alpha",
		"StateKeeper rejects snapshot mixing different runIds between run and combat")

	var malformed_run := keeper.accept_snapshot({"run": null, "combat": combat_dto})
	check(not malformed_run.accepted and str(malformed_run.snapshot.run.runId) == "run-alpha",
		"StateKeeper rejects null run payload and preserves last valid snapshot")

	# 8. Input immutability and payload parity on valid data
	var zone_presenter := CardZonePresenter.new(zones_dto, tr)
	check(zone_presenter.boundary_errors.is_empty() and zone_presenter.playable_cards().size() == 1 and zone_presenter.total_cards() == 1,
		"CardZonePresenter preserves exact playable cards and counts on valid snapshot")

	root.get_node("GameAudio").shutdown()
	await create_timer(0.05).timeout
	quit(0 if failures == 0 else 1)
