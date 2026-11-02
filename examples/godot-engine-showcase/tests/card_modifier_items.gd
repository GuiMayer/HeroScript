extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var i18n = root.get_node("I18n")
	i18n.set_locale("en")
	var context = load("res://scripts/ui/visual_context.gd").new(null, {}, load("res://data/themes/ivory_run.tres"))
	var commands := [{"type": "ACQUIRE_CARD_MODIFIER_ITEM", "validPayload": {"itemDefinitionIds": ["fire"]}},
		{"type": "APPLY_CARD_MODIFIER_ITEM", "validPayload": {"options": [{"itemInstanceId": "unit", "cardInstanceId": "card"}]}}]
	var choices: Array = load("res://scripts/application/activity_choices.gd").build({}, commands)
	check(choices.size() == 2 and choices[0].payload.itemDefinitionId == "fire" and choices[1].payload.cardInstanceId == "card", "choices preserve separate acquisition and application identities")
	var facts := {"identity": {"kind": "Instance", "definitionId": "strike", "cardInstanceId": "card"}, "faceValueMode": "ComposedBase", "rarity": "Common", "costs": [],
		"effects": [{"effectPath": "main", "definition": {"type": "DAMAGE", "targetResource": "health", "flatValue": 999}, "parameters": [{"parameter": "Amount", "values": {"original": 6, "composedBase": 8}}]}]}
	var unit := {"instance": {"itemInstanceId": "unit", "state": "Available"}, "definition": {"itemDefinitionId": "fire", "presentation": {"name": "Fire imprint"}},
		"cards": [{"cardInstanceId": "card", "compatible": true}]}
	var inventory = load("res://scripts/ui/components/card_modifier_inventory_view.gd").create()
	root.add_child(inventory)
	inventory.configure({"items": [unit]}, choices, [facts], {}, context)
	var selected: Array = []
	inventory.choice_requested.connect(func(reference): selected.append(reference))
	for _i in 3: await process_frame
	inventory._select(unit)
	for _i in 3: await process_frame
	var cards: Array = inventory.find_children("*", "Button", true, false).filter(func(button): return button is CardView)
	check(cards.size() == 1 and not cards[0].disabled, "eligible target is the shared card component, not a name-only list")
	cards[0].pressed.emit()
	check(selected.size() == 1 and selected[0] == load("res://scripts/presentation/activity_presenter.gd").option_reference(choices[1]), "target click emits only the advertised command reference")
	await _capture(inventory, context)
	unit.cards[0].compatible = false
	unit.cards[0]["reason"] = "No available slot"
	inventory._select(unit)
	for _i in 3: await process_frame
	cards = inventory.find_children("*", "Button", true, false).filter(func(button): return button is CardView)
	check(cards[0].disabled, "engine rejection disables the target without evaluating slots in the UI")
	check(load("res://scripts/application/activity_choices.gd").only_forced_advance([choices[1], {"type": "ADVANCE_NODE"}]).type == "ADVANCE_NODE", "optional inventory tools do not block forced travel")
	# Reproduce the screenshot using the OLD saved revision: five missing descriptions
	# and the old technical condensation copy. No new content or engine restart required.
	var catalog: Array = []
	var ids := ["w1_efficiency_item", "w1_calibration_item", "w1_amplify_item", "w1_recall_item", "w1_ward_item", "w1_condense_item"]
	for id in ids: catalog.append({"itemDefinitionId": id, "presentation": {"name": id}})
	catalog[-1].presentation["description"] = "Replaces the main effect with the existing DoT stack-release recipe."
	var unchanged := JSON.stringify(catalog)
	var old_choices: Array = load("res://scripts/application/activity_choices.gd").build({}, [
		{"type": "ACQUIRE_CARD_MODIFIER_ITEM", "validPayload": {"itemDefinitionIds": ids}}])
	for language in ["en", "pt_BR"]:
		i18n.set_locale(language)
		var old_presenter = load("res://scripts/presentation/activity_presenter.gd").new({}, old_choices, {}, i18n, [], {"catalog": catalog})
		var offers = load("res://scripts/ui/components/activity_options_view.gd").new()
		root.add_child(offers)
		offers.apply_projection({}, old_choices, old_presenter, load("res://scripts/ui/visual_context.gd").new(i18n))
		for index in old_choices.size():
			var model: Dictionary = old_presenter.offer(old_choices[index])
			var expected: String = i18n.content_name(ids[index] + ".description")
			check(model.summary == expected and not model.summary.contains("Select an item") and not model.summary.contains("recipe"), "old saved modifier has a meaningful effect: " + ids[index] + " / " + language)
			check(offers.find_children("*", "Label", true, false).any(func(label): return label.text == expected), "effect is actually visible in the offer: " + ids[index] + " / " + language)
		offers.queue_free()
		await process_frame
	check(JSON.stringify(catalog) == unchanged, "presentation repair does not mutate the saved content revision")
	i18n.set_locale("en")
	var presenter_type = load("res://scripts/presentation/activity_presenter.gd")
	check(presenter_type.modifier_description({"itemDefinitionId": "custom_mod", "presentation": {"description": "A custom authored effect."}}, i18n) == "A custom authored effect.", "unknown mod keeps its authored description")
	check(presenter_type.modifier_description({"itemDefinitionId": "unknown"}, i18n).begins_with("Effect description unavailable"), "unknown missing effect is reported honestly, never replaced by selection instructions")
	var old_inventory = load("res://scripts/ui/components/card_modifier_inventory_view.gd").create()
	root.add_child(old_inventory)
	var old_item := {"definition": catalog[1], "instance": {"itemInstanceId": "old", "state": "Available"}, "cards": []}
	old_inventory.configure({"items": [old_item]}, [], [], {}, load("res://scripts/ui/visual_context.gd").new(i18n))
	old_inventory._select(old_item)
	check(old_inventory._targets.find_children("*", "Label", true, false).any(func(label): return label.text.contains("40 percentage points")), "owned legacy item's selected detail uses the same effect description")
	old_inventory.queue_free()
	inventory.queue_free()
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)

func _capture(inventory: Control, context) -> void:
	var output := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output = argument.trim_prefix("--lab-output=")
		if argument.begins_with("--capture-resolution="):
			var parts: PackedStringArray = argument.trim_prefix("--capture-resolution=").split("x")
			root.size = Vector2i(int(parts[0]), int(parts[1]))
	if output.is_empty(): return
	root.gui_embed_subwindows = true
	root.remove_child(inventory)
	var dialog := AcceptDialog.new()
	dialog.theme = context.theme
	dialog.title = context.translator.text("Modifier items")
	inventory.custom_minimum_size = Vector2(950, 570)
	dialog.add_child(inventory)
	root.add_child(dialog)
	dialog.popup_centered_clamped(Vector2i(1100, 680), .92)
	for _i in 20: await process_frame
	await RenderingServer.frame_post_draw
	var path := output.path_join("modifier-items-%sx%s.png" % [root.size.x, root.size.y])
	check(root.get_texture().get_image().save_png(path) == OK, "native inventory rendering captured")
	check(dialog.size.x <= root.size.x and dialog.size.y <= root.size.y, "inventory dialog fits the selected native resolution")
