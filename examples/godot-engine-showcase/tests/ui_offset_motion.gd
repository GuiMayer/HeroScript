extends SceneTree

var failures := 0
func _init() -> void: call_deferred("_run")
func check(condition: bool, message: String) -> void:
	print("[PASS] " if condition else "[FAIL] ", message)
	if not condition: failures += 1
func settle(seconds := .3) -> void: await create_timer(seconds).timeout

func _run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var column := VBoxContainer.new()
	column.position = Vector2(50, 50)
	root.add_child(column)
	var button := Button.new()
	button.text = "ACTION"
	button.theme_type_variation = &"PrimaryAction"
	button.custom_minimum_size = Vector2(240, 48)
	column.add_child(button)
	var sibling := Button.new()
	sibling.text = "TARGET"
	column.add_child(sibling)
	var helper = preload("res://scripts/ui/button_affordance.gd")
	helper.attach(button, context); helper.attach(button, context); helper.attach(sibling, context)
	await process_frame
	check(button.get_child_count() == 1 and sibling.get_child_count() == 0, "only action chrome gets one motion helper")
	var rectangle := button.get_global_rect()
	var sibling_rect := sibling.get_global_rect()
	button.button_down.emit()
	await settle()
	check(button.offset_transform_scale.is_equal_approx(Vector2(1.025,.96)), "pressed squash affects visuals")
	check(button.offset_transform_visual_only and button.get_global_rect() == rectangle and sibling.get_global_rect() == sibling_rect, "input rectangle and sibling layout remain unchanged")
	button.disabled = true
	await process_frame
	await process_frame
	check(button.offset_transform_scale == Vector2.ONE, "disabled action immediately resets")
	button.disabled = false
	button.button_up.emit()
	context._options["reduced_motion"] = true
	context.changed.emit()
	button.button_down.emit()
	await settle()
	check(button.offset_transform_scale == Vector2.ONE, "reduced motion suppresses squash")
	var panel := PanelContainer.new()
	root.add_child(panel)
	var motion = preload("res://scripts/ui/panel_motion.gd")
	check(motion.enter(panel, context) == null and panel.offset_transform_position == Vector2.ZERO, "reduced motion suppresses panel transitions")
	context._options["reduced_motion"] = false
	var transition = motion.enter(panel, context)
	check(transition != null and panel.offset_transform_position == Vector2(0,8), "panel enters with visual-only offset")
	motion.cancel(panel)
	check(panel.offset_transform_position == Vector2.ZERO and panel.modulate.a == 1.0, "cancel settles panel without stale visual state")
	var energy := Label.new()
	energy.name = "EnergyIndicator"
	energy.text = "Energy 3 / 3"
	energy.custom_minimum_size = Vector2(240, 36)
	column.add_child(energy)
	preload("res://scripts/ui/counter_affordance.gd").attach(energy, context)
	await process_frame
	var energy_rect := energy.get_global_rect()
	energy.text = "Energy 2 / 3"
	await process_frame
	await process_frame
	check(energy.offset_transform_scale.x > 1.0 and energy.get_global_rect() == energy_rect, "energy change acknowledges without moving hand layout")
	context._options["reduced_motion"] = true
	context.changed.emit()
	check(energy.offset_transform_scale == Vector2.ONE and energy.offset_transform_position == Vector2.ZERO, "preference change cancels active counter animation")
	context._options["reduced_motion"] = false
	context.changed.emit()
	var pause = preload("res://scenes/ui/overlays/pause_menu.tscn").instantiate()
	pause.setup(context, true, false)
	root.add_child(pause)
	await settle(.7)
	check(pause.get_node("%ResumeButton").offset_transform_position == Vector2.ZERO, "staggered pause actions settle")
	check(pause.get_node("%ConversationHistoryButton").disabled, "stagger does not alter action availability")
	pause.queue_free(); column.queue_free(); panel.queue_free()
	await process_frame
	quit(failures)
