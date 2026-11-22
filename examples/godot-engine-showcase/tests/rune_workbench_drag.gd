extends SceneTree
## Exercises native Godot mouse drag/drop; optional renderer screenshots preserve evidence.
var failures := 0
var last_position := Vector2.ZERO
var output := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func move_mouse(position: Vector2, pressed := false) -> void:
	if DisplayServer.get_name() != "headless": root.warp_mouse(position)
	var event := InputEventMouseMotion.new(); event.position = position; event.global_position = position; event.relative = position - last_position
	event.button_mask = MOUSE_BUTTON_MASK_LEFT if pressed else 0; last_position = position
	Input.parse_input_event(event)
func button_down(position: Vector2, pressed: bool) -> void:
	if DisplayServer.get_name() != "headless": root.warp_mouse(position)
	var event := InputEventMouseButton.new(); event.position = position; event.global_position = position; event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
	Input.parse_input_event(event)
func frames(count := 5) -> void:
	for _i in count: await process_frame
func capture(name: String) -> void:
	if output.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output.path_join(name + ".png")) == OK, "captured " + name)
func _run() -> void:
	var resolution := Vector2i(1440, 900)
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--capture="): output = arg.trim_prefix("--capture="); DirAccess.make_dir_recursive_absolute(output)
		if arg == "--compact": resolution = Vector2i(960, 540)
	root.content_scale_size = resolution; root.size = resolution
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var background := ColorRect.new(); background.color = Color("f7f3e9"); root.add_child(background); background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var shell := MarginContainer.new(); root.add_child(shell); shell.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for edge in ["left", "right", "top", "bottom"]: shell.add_theme_constant_override("margin_" + edge, 32)
	var column := VBoxContainer.new(); shell.add_child(column)
	var title := Label.new(); title.text = "OBOR  /  RUNE FORGE"; title.add_theme_font_size_override("font_size", 34); title.add_theme_color_override("font_color", Color("292723")); column.add_child(title)
	var workbench = load("res://scenes/ui/components/pattern_workshop_inventory.tscn").instantiate(); column.add_child(workbench); workbench.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var fact := {"identity": {"kind": "Instance", "definitionId": "strike", "cardInstanceId": "card-a"}, "faceValueMode": "ComposedBase", "rarity": "Common", "costs": [{"costs": {"costs": [{"resourceId": "energy", "amount": 1}]}}], "effects": [{"effectPath": "main", "definition": {"type": "DAMAGE", "targetResource": "health", "flatValue": 6}, "parameters": [{"parameter": "Amount", "values": {"original": 6, "composedBase": 6}}]}]}
	var duplicate: Dictionary = fact.duplicate(true); duplicate.identity.cardInstanceId = "card-b"
	var item := {"instance": {"itemInstanceId": "unit-a", "state": "Available"}, "definition": {"itemDefinitionId": "obor_overwrite_fragment_item", "presentation": {"name": "Fragmentation", "description": "Split one strike into three impacts."}}, "cards": [{"cardInstanceId": "card-a", "compatible": true}, {"cardInstanceId": "card-b", "compatible": false, "reason": "Slot occupied"}]}
	var other: Dictionary = item.duplicate(true); other.instance.itemInstanceId = "unit-b"; other.definition.presentation.name = "Ember"; other.definition.itemDefinitionId = "obor_overwrite_ember_item"
	var choices := [{"type": "APPLY_CARD_MODIFIER_ITEM", "subjectId": "unit-a", "payload": {"itemInstanceId": "unit-a", "cardInstanceId": "card-a"}}]
	workbench.configure({"runId": "qa", "runSequence": 1, "contentRevision": "revision", "items": [item, other]}, choices, [fact, duplicate], {}, context)
	var captured: Array = []; workbench.choice_requested.connect(func(reference): captured.append(reference))
	await frames(12); await capture("01-forge-before")
	var list: ItemList = workbench.get_node("ItemsColumn/Items")
	var src: Vector2 = list.get_global_rect().position + list.get_item_rect(0).get_center()
	var target: CardView = workbench.get_node("CardsColumn/Cards/Slot1/Card")
	var dst := target.get_global_rect().get_center()
	if DisplayServer.get_name() != "headless":
		move_mouse(src); await frames(); button_down(src, true); await frames(); move_mouse(src + Vector2(24, 12), true); await frames(10)
		check(root.gui_is_dragging(), "native mouse drag started on rune")
		move_mouse(dst, true); await frames(10); await capture("02-forge-dragging")
		button_down(dst, false); await frames(10); await capture("03-forge-drop-selected")
		check(captured.size() == 1, "dropping on card body emitted one advertised choice")
		var dialog: ConfirmationDialog = preload("res://scenes/ui/overlays/transformation_confirmation.tscn").instantiate()
		dialog.theme = context.theme; dialog.title = "Bind rune"; dialog.ok_button_text = "Engrave"; dialog.cancel_button_text = "Cancel"
		root.add_child(dialog); var content := dialog.get_node("Content"); content.show()
		content.get_node("Description").text = "Fragmentation  ◆  Strike\nA permanent binding · split one impact into three."
		var after: Dictionary = fact.duplicate(true); after.effects[0].definition.repeat = 3
		after.effects[0].definition.parameters = [{"parameter": "Amount", "distribution": {"sourceStageIds": ["origin"]}}]
		after.permanentChanges = [{"transformationId": 1, "category": "Behavior", "upgradeId": "obor_overwrite_fragment"}]
		for side in ["Before", "After"]:
			var shown_card: CardView = content.get_node("Scroll/Comparison/" + side + "/Card")
			shown_card.visual_context = context; shown_card.configure(preload("res://scripts/presentation/card_facts_presenter.gd").new(context.translator, {}).project(fact if side == "Before" else after))
		dialog.popup_centered_clamped(Vector2i(700, 680), .92); await frames(10); await capture("04-forge-comparison")
		check(dialog.size.x <= resolution.x and dialog.size.y <= resolution.y, "comparison fits the viewport")
		dialog.queue_free()
		await frames(10)
		var incompatible: CardView = workbench.get_node("CardsColumn/Cards/Slot2/Card")
		var invalid_dst := incompatible.get_global_rect().get_center()
		move_mouse(src); await frames(); button_down(src, true); await frames(); move_mouse(src + Vector2(25, 11), true); await frames(10)
		check(root.gui_is_dragging(), "second native drag begins normally")
		move_mouse(invalid_dst, true); await frames(10); await capture("05-forge-incompatible")
		button_down(invalid_dst, false); await frames(10)
		check(captured.size() == 1 and not root.gui_is_dragging(), "dropping on incompatible card cancels without a command")
	else:
		workbench._select_item(0); var payload := preload("res://scripts/ui/components/rune_drag.gd").payload(workbench.get_instance_id(), workbench._generation, workbench._snapshot, item)
		check(workbench._accept_drag(0, payload) and not workbench._accept_drag(1, payload), "compatible target accepts identity and incompatible target rejects")
		workbench._drop_rune(1, payload); check(captured.is_empty(), "invalid drop never submits")
		workbench._drop_rune(0, payload); check(captured.size() == 1, "valid drop requests comparison")
		workbench._generation += 1; workbench._drop_rune(0, payload); check(captured.size() == 1, "stale generation cannot submit")
	if not captured.is_empty(): check(captured[0] == preload("res://scripts/presentation/activity_presenter.gd").option_reference(choices[0]), "drop uses exact item and card instance reference")
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
