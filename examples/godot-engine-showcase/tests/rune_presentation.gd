extends SceneTree
func _init() -> void: call_deferred("_run")
func _run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var facts := {"identity": {"kind": "Instance", "definitionId": "strike", "cardInstanceId": "a"}, "permanentChanges": [
		{"transformationId": 1, "category": "Affinity", "upgradeId": "obor_overwrite_ember", "componentId": "affinity"},
		{"transformationId": 1, "category": "Affinity", "upgradeId": "obor_overwrite_ember", "componentId": "tags"},
		{"transformationId": 2, "category": "Behavior", "upgradeId": "obor_overwrite_fragment", "componentId": "main"}]}
	var model: Dictionary = preload("res://scripts/presentation/card_facts_presenter.gd").new(context.translator, {}).project(facts)
	assert(model.runes.size() == 2)
	assert(model.runes[0].name == "Ember" and model.runes[1].name == "Fragment")
	var card := CardView.create(context); card.animate_entry = false; root.add_child(card); card.configure(model)
	assert(card.get_node("%UpgradeMarker").text == "◆⋔")
	assert(card.tooltip_text.contains("Ember") and card.tooltip_text.contains("Fragment"))
	card.queue_free(); await process_frame
	print("[PASS] rune markers deduplicate patch traces and name both transformations")
	quit(0)
