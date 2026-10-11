extends SceneTree
## Definitions/source quantities on cards; actual target outcomes in the combat preview.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1

func _run() -> void:
	var translator = root.get_node("I18n")
	var locale: String = translator.locale
	var effect := {"type": "DAMAGE", "target": "TARGET", "targetResource": "mana", "flatValue": 8}
	var actor := {"instanceId": "foe", "definitionId": "unknown_foe", "name": "Named opponent"}
	var other := {"instanceId": "other", "definitionId": "other_foe", "name": "Second opponent"}
	var inspection := {"version": {"runSequence": 1}, "evaluation": {"cardInstanceId": "card"},
		"compiledContainer": {"components": [{"componentId": "effect", "effect": effect}]},
		"effectiveBase": {"components": [{"componentId": "effect", "effect": effect}]},
		"previewScope": {"selectedTargetIds": ["foe"], "hasExecutablePreview": true},
		"previewSteps": [{"applied": true, "provenance": {"componentId": "effect"}, "identity": {"parentProcId": null},
			"calculation": {"baseValue": 8, "value": 3, "quantity": {"incorporatedStages": [
				{"stageId": "custom_origin", "scope": "Actor"}, {"stageId": "roll", "scope": "Shared"}, {"stageId": "defense", "scope": "Target"}]},
				"buckets": [{"stageId": "custom_origin", "output": 12}, {"stageId": "roll", "output": 24}, {"stageId": "defense", "output": 3}]}}]}
	var actions: Array = []
	for target in ["foe", "other"]:
		actions.append({"command": {"cardInstanceId": "card", "targetIds": [target]}, "costs": [{"resourceId": "energy", "amount": 1}],
			"applications": [{"targetEntityId": target, "resourceId": "mana", "previousValue": 10, "currentValue": 7}]})
	var run := {"sequence": 1, "deck": {"cardInstances": [{"cardInstanceId": "card", "definitionId": "custom_card"}]}}
	var combat := {"actors": [actor, other]}
	var appearance := {"cards": {"custom_card": {"text": "Generic card definition"}}}
	var before := JSON.stringify([run, combat, actions, inspection])
	var source = load("res://scripts/presentation/card_section_presenter.gd").new(translator, {}, [actor, other])
	for language in ["en", "pt_BR"]:
		translator.set_locale(language)
		var presenter = load("res://scripts/presentation/combat_presenter.gd").new(run, combat, actions, translator, appearance)
		var fallback: Dictionary = presenter.card_view_model("card")
		check(fallback.summary == "Generic card definition", "no inspection: use definition, not a candidate's target/delta")
		presenter.accept_evaluations([inspection])
		var model: Dictionary = presenter.card_view_model("card")
		check(not model.summary.contains("Named opponent") and not model.summary.contains("Second opponent"), "card text never contains actor names: " + language)
		check(model.effectRows[0].segments[1].value == 12 and model.effectRows[0].segments[1].baseValue == 8, "Actor-prefix output preserves scaling, excluding random impact and target mitigation")
		check(presenter.action_preview("card", "foe").contains("Named opponent") and presenter.action_preview("card", "other").contains("Second opponent"), "separate preview identifies the actual target")
		check(presenter.action_preview("card", "foe").contains("10 → 7"), "separate preview consumes the actual resource transition")
		var card = load("res://scripts/ui/card_view.gd").new(load("res://scripts/ui/visual_context.gd").from_nodes(root.get_node("Preferences"), root.get_node("I18n")))
		card.animate_entry = false
		card.configure(model)
		check(not card.tooltip_text.contains("Named opponent") and not card.tooltip_text.contains("Second opponent"), "accessible card tooltip does not leak preview/diagnostic target names")
		card.free()
		check(presenter.action_preview("card") == translator.text("Choose a highlighted target."), "multiple targets do not implicitly select the first candidate")
		var changed := inspection.duplicate(true)
		changed.previewScope.selectedTargetIds = ["other"]
		changed.previewSteps[0].calculation.value = 1
		changed.previewSteps[0].calculation.buckets[2].output = 1
		presenter.accept_evaluations([changed])
		check(presenter.card_view_model("card").effectRows == model.effectRows, "changing target/mitigation does not rewrite the card")
		changed["calculations"] = [{"quantity": {"incorporatedStages": [{"stageId": "defense", "scope": "Target"}]},
			"buckets": [{"stageId": "defense", "contributions": [{"sourceKind": "Status", "sourceId": "target_debuff", "applied": true, "input": 12, "output": 15}]}]}]
		presenter.accept_evaluations([changed])
		check(presenter.card_view_model("card").changeBadges.is_empty(), "target-side influences never masquerade as modifications to the card")
		var unknown := inspection.duplicate(true)
		unknown.previewSteps[0].calculation.erase("quantity")
		check(source.sections(unknown, {}).effectRows[0].segments[1].value == 8, "unscoped legacy traces safely retain effective base instead of a target result")
		var distributed := inspection.duplicate(true)
		distributed.previewSteps[0]["sequenceBudgets"] = [{"parameter": "Amount", "capture": {"captureOnly": true, "baseValue": 8, "value": 12,
			"quantity": {"incorporatedStages": [{"stageId": "custom_origin", "scope": "Actor"}]}}}]
		check(source.sections(distributed, {}).effectRows[0].segments[1].value == 12, "multi-hit uses the complete published source budget, not one critical share")
		for kind in ["SELF", "ALL_ENEMIES", "ALL_ALLIES", "RANDOM_ENEMY", "LOWEST_RESOURCE_ENEMY", "HIGHEST_RESOURCE_ENEMY"]:
			var typed := inspection.duplicate(true)
			typed.effectiveBase.components[0].effect.target = kind
			check(not load("res://scripts/ui/card_effect_text.gd").plain_text(source.sections(typed, {}).effectRows).contains("Named opponent"), "generic targeting semantics preserved: " + kind)
	check(JSON.stringify([run, combat, actions, inspection]) == before, "projection leaves all input snapshots and RNG facts untouched")
	translator.set_locale(locale)
	root.get_node("GameAudio").shutdown()
	print("CARD_PREVIEW_SEPARATION: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
