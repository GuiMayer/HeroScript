class_name CardView
extends Button

var motion: Tween
var chosen := false
var model: Dictionary = {}

func configure(data: Dictionary) -> void:
	model = data.duplicate(true)
	text = ""
	custom_minimum_size = Vector2(224, 254 + maxf(0, Preferences.text_scale - 1.0) * 60)
	var tone: Color = {"attack": AppTheme.BLOOD, "power": Color("#9a86d8")}.get(str(data.get("tone", "skill")), AppTheme.TEAL)
	for state in ["normal", "disabled", "pressed"]:
		add_theme_stylebox_override(state, AppTheme.box(tone.darkened(.76), 12, tone if state != "pressed" else AppTheme.GOLD, 2))
	add_theme_stylebox_override("hover", AppTheme.box(tone.darkened(.62), 12, AppTheme.GOLD, 2))
	add_theme_stylebox_override("focus", AppTheme.box(Color.TRANSPARENT, 12, AppTheme.GOLD, 3))
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 12)
	add_child(margin)
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 4)
	margin.add_child(column)
	var cost := AppTheme.muted(str(data.get("cost", "")), 13)
	cost.autowrap_mode = TextServer.AUTOWRAP_OFF
	cost.add_theme_color_override("font_color", AppTheme.GOLD)
	cost.name = "Cost"
	cost.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	column.add_child(cost)
	var title := AppTheme.title(str(data.get("name", "")), 18)
	title.name = "CardName"
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	column.add_child(title)
	var art := preload("res://scripts/ui/art_slot.gd").new()
	art.setup("cards", str(data.get("definitionId", "")))
	art.custom_minimum_size.y = 60 if Preferences.text_scale > 1.1 else 76
	column.add_child(art)
	var rules := Label.new()
	rules.name = "Rules"
	rules.text = str(data.get("summary", ""))
	rules.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	rules.max_lines_visible = 4
	rules.add_theme_font_size_override("font_size", 14)
	rules.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(rules)
	var availability := AppTheme.muted(str(data.get("availability", "")), 11)
	availability.autowrap_mode = TextServer.AUTOWRAP_OFF
	availability.name = "Availability"
	availability.add_theme_color_override("font_color", tone.lightened(.2))
	availability.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	column.add_child(availability)
	_ignore_mouse(margin)
	tooltip_text = "%s\n%s\n%s" % [data.get("name", ""), data.get("cost", ""), data.get("summary", "")]

func _ignore_mouse(control: Control) -> void:
	control.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for child in control.get_children():
		if child is Control: _ignore_mouse(child)

func _ready() -> void:
	pivot_offset = size * 0.5
	resized.connect(func(): pivot_offset = size * 0.5)
	mouse_entered.connect(func(): _emphasize(true))
	mouse_exited.connect(func(): _emphasize(chosen))
	focus_entered.connect(func(): _emphasize(true))
	focus_exited.connect(func(): _emphasize(chosen))
	if not Preferences.reduced_motion:
		modulate.a = 0.0
		scale = Vector2(.92, .92)
		motion = create_tween().set_parallel()
		motion.tween_property(self, "modulate:a", 1.0, .18 / Preferences.animation_speed)
		motion.tween_property(self, "scale", Vector2.ONE, .22 / Preferences.animation_speed).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

func select_card(value: bool) -> void:
	var changed := chosen != value
	chosen = value
	set_pressed_no_signal(value)
	if changed: _emphasize(value or is_hovered() or has_focus())

func _emphasize(value: bool) -> void:
	if motion:
		motion.kill()
	modulate.a = 1.0
	z_index = 2 if value else 0
	if Preferences.reduced_motion:
		scale = Vector2.ONE
		return
	motion = create_tween()
	motion.tween_property(self, "scale", Vector2(1.035, 1.035) if value else Vector2.ONE,
		.12 / Preferences.animation_speed).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

func fly_to(destination: Vector2, overlay: Node) -> void:
	if Preferences.reduced_motion:
		return
	var ghost := CardView.new()
	ghost.configure(model)
	ghost.process_mode = Node.PROCESS_MODE_PAUSABLE
	ghost.text = text
	ghost.autowrap_mode = autowrap_mode
	ghost.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ghost.focus_mode = Control.FOCUS_NONE
	ghost.add_theme_stylebox_override("normal", get_theme_stylebox("normal"))
	overlay.add_child(ghost)
	if ghost.motion: ghost.motion.kill()
	ghost.modulate.a = 1.0
	ghost.global_position = global_position
	ghost.size = size
	ghost.pivot_offset = size * .5
	ghost.z_index = 100
	var tween := ghost.create_tween().set_parallel()
	tween.tween_property(ghost, "global_position", destination - size * .5, .22 / Preferences.animation_speed).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	tween.tween_property(ghost, "scale", Vector2(.45, .45), .22 / Preferences.animation_speed)
	tween.tween_property(ghost, "modulate:a", 0.0, .22 / Preferences.animation_speed)
	await tween.finished
	ghost.queue_free()
