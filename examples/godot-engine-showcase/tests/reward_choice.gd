extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	var translator = root.get_node("I18n"); translator.set_locale("en")
	var context = load("res://scripts/ui/visual_context.gd").from_nodes(root.get_node("Preferences"), translator)
	var state := {"runId": "reward", "currentNodeId": "offer", "map": {"nodes": [{"nodeId": "offer", "activity": {"type": "CardSelection"}, "metadata": {"rewardDestination": "Reserve"}}]},
		"cardSelections": [{"selectionInstanceId": "selection", "decompose": {"completesSelection": true}, "options": [{"cardId": "pattern", "decomposeRewards": [{"resourceId": "parts", "amount": 3}]}]}]}
	var choices: Array = load("res://scripts/application/activity_choices.gd").build(state, [
		{"type": "PICK_CARD_REWARD", "validPayload": {"selectionInstanceId": "selection", "cardIds": ["pattern"]}},
		{"type": "DECOMPOSE_CARD_REWARD", "validPayload": {"selectionInstanceId": "selection", "cardIds": ["pattern"]}}])
	var facts := {"identity": {"kind": "Offer", "definitionId": "pattern", "offerId": "selection/pattern"}, "rarity": "Common", "costs": [],
		"effects": [{"effectPath": "main", "definition": {"type": "DAMAGE", "target": "TARGET", "targetResource": "health", "flatValue": 6}}]}
	var before := JSON.stringify(state)
	var presenter = load("res://scripts/presentation/activity_presenter.gd").new(state, choices, {"cards": {"pattern": {"name": "Pattern"}}}, translator, [facts])
	var store: Dictionary = presenter.offer(choices[0]); var decompile: Dictionary = presenter.offer(choices[1])
	check(presenter.choice_label(choices[0], store).begins_with("STORE"), "exclusive reward uses storage language outside the card")
	check(presenter.choice_label(choices[1], decompile).contains("3 Parts"), "decompile button states published material amount before confirmation")
	var view = load("res://scenes/ui/components/activity_options.tscn").instantiate(); root.add_child(view)
	view.apply_projection(state, choices, presenter, context)
	for _i in 4: await process_frame
	check(view.find_children("*", "Label", true, false).any(func(label): return label.text == "Destination: Reserve"), "destination is a separate label outside canonical card face")
	check(view.choice_buttons.any(func(button): return button is CardView and button.model.has("presentationFacts")), "storage offer reuses canonical CardView and facts")
	check(JSON.stringify(state) == before, "view rendering never mutates reward state or grants items")
	state.cardSelections[0].decompose.completesSelection = false
	var legacy = load("res://scripts/presentation/activity_presenter.gd").new(state, choices, {}, translator, [facts])
	check(not legacy.offer(choices[0]).has("rewardDestination") and not legacy.offer(choices[1]).has("terminalReward"), "legacy multi-option offers retain their existing presentation semantics")
	view.queue_free()
	for _i in 8: await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
