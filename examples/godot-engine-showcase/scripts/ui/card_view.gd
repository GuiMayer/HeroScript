class_name CardView
extends Button

var motion: Tween
var chosen := false

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
	chosen = value
	set_pressed_no_signal(value)
	_emphasize(value)

func _emphasize(value: bool) -> void:
	if motion:
		motion.kill()
	modulate.a = 1.0
	z_index = 2 if value else 0
	if Preferences.reduced_motion:
		scale = Vector2.ONE
		return
	motion = create_tween()
	motion.tween_property(self, "scale", Vector2(1.06, 1.06) if value else Vector2.ONE,
		.12 / Preferences.animation_speed).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

func fly_to(destination: Vector2, overlay: Node) -> void:
	if Preferences.reduced_motion:
		return
	var ghost := Button.new()
	ghost.process_mode = Node.PROCESS_MODE_PAUSABLE
	ghost.text = text
	ghost.autowrap_mode = autowrap_mode
	ghost.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ghost.focus_mode = Control.FOCUS_NONE
	ghost.add_theme_stylebox_override("normal", get_theme_stylebox("normal"))
	overlay.add_child(ghost)
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
