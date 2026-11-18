extends SceneTree
var failures := 0
func _init() -> void: run.call_deferred()
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new(null, {}, preload("res://data/themes/mvp_combat_theme.tres"))
	var actor := {"instanceId": "hero", "name": "Hero", "resources": {
		"health": {"current": 40, "minimum": 0, "maximum": 100},
		"block": {"current": 7}, "energy": {"current": 3, "maximum": 3}},
		"statuses": [{"statusId": "burn", "stacks": 1, "isActive": true}, {"statusId": "unknown_status", "stacks": 3, "isActive": true}]}
	var presentation := {"resource_order": ["health", "block", "energy"], "resources": {"health": {"display": "bar"}, "block": {"display": "counter"}, "energy": {"display": "counter"}}}
	var panel = load("res://scenes/ui/components/battlefield_actor.tscn").instantiate()
	panel.visual_context = context; root.add_child(panel); panel.setup(actor, false, presentation, "", false, true)
	await process_frame
	var hp = panel.resource_view("health")
	check(hp != null and hp.amount.text == "40/100" and hp.amount.get_global_rect().end.y <= hp.bar.get_global_rect().position.y and hp.bar.value == 40, "MVP health value has a dedicated row above the bounded bar")
	var block = panel.resource_counter("block")
	check(block != null and block.text == "7" and block.get_parent().get_node("Icon").texture.resource_path.ends_with("sbed/shield.svg"), "shield displays the published value and attributed icon")
	check(panel.resource_counter("energy") == null and panel.resource_view("energy") == null, "MVP actor plate does not duplicate energy")
	var burn = panel.status_badge("burn"); var fallback = panel.status_badge("unknown_status")
	check(burn.icon_rect.visible and burn.icon_rect.texture.resource_path.ends_with("sbed/burn.svg") and burn.stack_label.visible and burn.stack_label.text == "1", "known active status shows its icon and value one")
	check(fallback.icon_rect.visible and fallback.icon_rect.texture.resource_path.ends_with("lorc/aura.svg") and fallback.stack_label.text == "3", "unknown active status uses an attributed fallback and published value")
	panel.present_resource({"resourceId": "health", "currentValue": 25, "resourceField": "Current"}, 0.0)
	panel.present_resource({"resourceId": "block", "currentValue": 2, "resourceField": "Current"}, 0.0)
	check(hp.amount.text == "25/100" and hp.bar.value == 25 and block.text == "2", "health and shield update from combat presentation")
	actor.statuses = [{"statusId": "burn", "stacks": 2, "isActive": true}, {"statusId": "unknown_status", "stacks": 0, "isActive": false}]
	panel.setup(actor, false, presentation, "", false, true)
	check(panel.status_badge("burn").stack_label.text == "2" and panel.status_badge("unknown_status") == null, "status counts update and inactive effects disappear")
	actor.resources.block.current = 0
	actor.statuses = [
		{"statusId": "wet", "stacks": 9},
		{"statusId": "vulnerable", "stacks": 12, "duration": 2, "description": "Takes more damage."},
		{"statusId": "strength", "stacks": 12},
		{"statusId": "focus", "stacks": 1},
		{"statusId": "burn", "stacks": 2},
	]
	panel.setup(actor, false, presentation, "", false, true)
	await process_frame
	check(panel.resource_counter("block").text == "0", "published zero remains visible")
	var inspect_button: Button = panel.find_child("InspectEffects", true, false)
	check(inspect_button.text == "…" and panel.status_badge("burn").visible and panel.status_badge("wet").visible, "five statuses are visible alongside inspection")
	actor.statuses.append({"statusId": "zz_hidden", "stacks": 2})
	panel.setup(actor, false, presentation, "", false, true)
	check(inspect_button.text == "+1" and not panel.status_badge("zz_hidden").visible, "overflow counts only additional statuses")
	inspect_button.grab_focus()
	check(root.gui_get_focus_owner() == inspect_button, "effects inspection can receive navigation focus")
	inspect_button.pressed.emit()
	await process_frame
	var dialog: AcceptDialog = panel.find_children("*", "AcceptDialog", false, false)[0]
	var detail: String = dialog.get_child(1).text
	check(dialog.visible and "12" in detail and "Duration: 2 turns" in detail and "Takes more damage." in detail, "inspection includes hidden effects and available details")
	dialog.hide()
	await process_frame
	check(root.gui_get_focus_owner() == inspect_button, "closing inspection restores navigation focus")
	var enlarged = preload("res://scripts/ui/visual_context.gd").new(null, {"text_scale": 1.6}, context.profile)
	panel.visual_context = enlarged
	panel.setup(actor, false, presentation, "", false, true)
	for frame in 3: await process_frame
	var two_digit_badge = panel.status_badge("strength")
	check(two_digit_badge.stack_label.text == "12" and two_digit_badge.size.is_equal_approx(StatusBadge.battlefield_cell(enlarged)), "enlarged badge retains both digits in its reserved cell")
	check(two_digit_badge.icon_rect.get_global_rect().end.y <= two_digit_badge.stack_label.get_global_rect().position.y, "status count does not overlap the icon at enlarged text size")
	var row: Rect2 = panel.get_node("%ActorStatusSlot").get_global_rect()
	for control in panel.get_node("%ActorStatusBadges").get_children():
		if control.visible:
			check(row.encloses(control.get_global_rect()), "visible status controls fit the reserved row at enlarged text size")
	var shield_icon: TextureRect = panel.resource_counter("block").get_parent().get_node("Icon")
	for icon: TextureRect in [shield_icon, two_digit_badge.icon_rect]:
		check(icon.stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED and is_equal_approx(icon.size.x, icon.size.y) and icon.texture.get_width() == icon.texture.get_height(), "icon preserves a square texture and layout at enlarged text size")
	actor.resources.erase("block")
	panel.setup(actor, false, presentation, "", false, true)
	check(panel.resource_counter("block") == null, "absent shield does not appear as zero")
	var old_panel = load("res://scenes/ui/components/actor_panel.tscn").instantiate()
	old_panel.visual_context = context; root.add_child(old_panel); old_panel.setup(actor, false, presentation, "")
	check(old_panel.resource_counter("energy") != null and not old_panel.status_badge("burn").mvp_icons, "legacy actor panel retains its original presentation")
	panel.queue_free(); old_panel.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
