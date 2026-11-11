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
	check(hp != null and hp.amount.text == "40/100" and hp.amount.get_parent().name == "HealthSurface" and hp.bar.value == 40, "MVP health value overlays the bounded bar")
	var block = panel.resource_counter("block")
	check(block != null and block.text == "7 Block" and block.get_parent().get_node("Icon").texture.resource_path.ends_with("sbed/shield.svg"), "shield displays the published value and attributed icon")
	check(panel.resource_counter("energy") == null and panel.resource_view("energy") == null, "MVP actor plate does not duplicate energy")
	var burn = panel.status_badge("burn"); var fallback = panel.status_badge("unknown_status")
	check(burn.icon_rect.visible and burn.icon_rect.texture.resource_path.ends_with("sbed/burn.svg") and burn.stack_label.visible and burn.stack_label.text == "1", "known active status shows its icon and value one")
	check(fallback.icon_rect.visible and fallback.icon_rect.texture.resource_path.ends_with("lorc/aura.svg") and fallback.stack_label.text == "3", "unknown active status uses an attributed fallback and published value")
	panel.present_resource({"resourceId": "health", "currentValue": 25, "resourceField": "Current"}, 0.0)
	panel.present_resource({"resourceId": "block", "currentValue": 2, "resourceField": "Current"}, 0.0)
	check(hp.amount.text == "25/100" and hp.bar.value == 25 and block.text == "2 Block", "health and shield update from combat presentation")
	actor.statuses = [{"statusId": "burn", "stacks": 2, "isActive": true}, {"statusId": "unknown_status", "stacks": 0, "isActive": false}]
	panel.setup(actor, false, presentation, "", false, true)
	check(panel.status_badge("burn").stack_label.text == "2" and panel.status_badge("unknown_status") == null, "status counts update and inactive effects disappear")
	var old_panel = load("res://scenes/ui/components/actor_panel.tscn").instantiate()
	old_panel.visual_context = context; root.add_child(old_panel); old_panel.setup(actor, false, presentation, "")
	check(old_panel.resource_counter("energy") != null and not old_panel.status_badge("burn").mvp_icons, "legacy actor panel retains its original presentation")
	panel.queue_free(); old_panel.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
