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
	var inventory = load("res://scripts/ui/components/card_modifier_inventory_view.gd").new()
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
