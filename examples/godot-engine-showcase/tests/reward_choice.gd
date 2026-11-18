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
	var materials: String = "3 " + translator.content_name("parts")
	check(presenter.choice_label(choices[1], decompile).contains(materials), "decompile button states published material amount before confirmation")
	var view = load("res://scenes/ui/components/activity_options.tscn").instantiate(); root.add_child(view)
	view.apply_projection(state, choices, presenter, context)
	for _i in 4: await process_frame
	check(view.find_children("*", "Label", true, false).any(func(label): return label.text.begins_with("Destination: Reserve")), "destination is shown outside the canonical card face")
	check(view.find_children("*", "Label", true, false).any(func(label): return label.text.contains("Ends this reward choice")), "the card offer explains that it ends this reward choice")
	check(view.choice_buttons.any(func(button): return button is CardView and button.model.has("presentationFacts")), "storage offer reuses canonical CardView and facts")
	var decompile_button: Button = view.find_child("Decompile", true, false)
	check(decompile_button.visible and decompile_button.get_index() == 1 and decompile_button.icon != null and decompile_button.tooltip_text.contains(materials), "decompile action appears under its associated card with icon and reward")
	var calls: Array = []
	view.intent_requested.connect(func(action, args): calls.append([action, args]))
	decompile_button.pressed.emit()
	check(calls.size() == 1 and calls[0][1][0] == presenter.option_reference(choices[1]), "decompile button emits the matching published choice")
	var journey = load("res://scenes/ui/components/journey_view.tscn").instantiate()
	journey.size = Vector2(1280, 720)
	root.add_child(journey)
	journey.apply_projection(state, choices, {}, {}, presenter, context)
	for _i in 12: await process_frame
	var scroll: ScrollContainer = journey.find_child("ActionScroll", true, false)
	check(not scroll.get_v_scroll_bar().visible and scroll.scroll_vertical == 0, "single row of reward cards fits without vertical scrolling")
	var options: Array = []
	var commands: Array = []
	var offers: Array = []
	for index in 8:
		var card_id := "pattern_%s" % index
		options.append({"cardId": card_id, "decomposeRewards": [{"resourceId": "parts", "amount": 3}]})
		offers.append({"identity": {"kind": "Offer", "definitionId": card_id, "offerId": "selection/" + card_id}, "rarity": "Common", "costs": [], "effects": facts.effects})
	var first_three: Array = options.slice(0, 3)
	var short_state := state.duplicate(true)
	short_state.cardSelections[0].options = first_three
	var short_commands := [{"type": "PICK_CARD_REWARD", "validPayload": {"selectionInstanceId": "selection", "cardIds": first_three.map(func(item): return item.cardId)}}, {"type": "DECOMPOSE_CARD_REWARD", "validPayload": {"selectionInstanceId": "selection", "cardIds": first_three.map(func(item): return item.cardId)}}]
	var short_choices: Array = load("res://scripts/application/activity_choices.gd").build(short_state, short_commands)
	var short_presenter = load("res://scripts/presentation/activity_presenter.gd").new(short_state, short_choices, {}, translator, offers)
	journey.apply_projection(short_state, short_choices, {}, {}, short_presenter, context)
	for _i in 12: await process_frame
	check(not scroll.get_v_scroll_bar().visible and journey.action_panel.choice_buttons.filter(func(button): return button.name == "Decompile" and button.visible).size() == 3, "three offers fit one row without scrolling")
	commands = [{"type": "PICK_CARD_REWARD", "validPayload": {"selectionInstanceId": "selection", "cardIds": options.map(func(item): return item.cardId)}}, {"type": "DECOMPOSE_CARD_REWARD", "validPayload": {"selectionInstanceId": "selection", "cardIds": options.map(func(item): return item.cardId)}}]
	var larger_state := state.duplicate(true)
	larger_state.cardSelections[0].options = options
	var many_choices: Array = load("res://scripts/application/activity_choices.gd").build(larger_state, commands)
	var many_presenter = load("res://scripts/presentation/activity_presenter.gd").new(larger_state, many_choices, {}, translator, offers)
	journey.apply_projection(larger_state, many_choices, {}, {}, many_presenter, context)
	for _i in 12: await process_frame
	check(scroll.get_v_scroll_bar().visible, "multiple rows remain accessible through vertical scrolling")
	check(journey.action_panel.choice_buttons.filter(func(button): return button.name == "Decompile" and button.visible).size() == 8, "each reward has its own decompile button")
	journey.queue_free()
	check(JSON.stringify(state) == before, "view rendering never mutates reward state or grants items")
	state.cardSelections[0].decompose.completesSelection = false
	var legacy = load("res://scripts/presentation/activity_presenter.gd").new(state, choices, {}, translator, [facts])
	check(not legacy.offer(choices[0]).has("rewardDestination") and not legacy.offer(choices[1]).has("terminalReward"), "legacy multi-option offers retain their existing presentation semantics")
	view.queue_free()
	for _i in 8: await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
