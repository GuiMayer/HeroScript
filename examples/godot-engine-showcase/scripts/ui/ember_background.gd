class_name EmberBackground
extends Control

var drift := 0.0

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_process(true)

func _process(delta: float) -> void:
	if Preferences.reduced_motion or get_tree().paused:
		return
	drift += delta * 0.22
	queue_redraw()

func _draw() -> void:
	var size := get_rect().size
	draw_rect(Rect2(Vector2.ZERO, size), Color("#080b18"))
	for i in 9:
		var ratio := float(i) / 8.0
		var tint := Color("#171228").lerp(Color("#0d2430"), ratio)
		draw_rect(Rect2(0, ratio * size.y, size.x, size.y / 8.0 + 1), tint)
	draw_colored_polygon(PackedVector2Array([
		Vector2(0, size.y * .64), Vector2(size.x * .18, size.y * .31),
		Vector2(size.x * .34, size.y * .64), Vector2(size.x * .51, size.y * .25),
		Vector2(size.x * .72, size.y * .64), Vector2(size.x, size.y * .37),
		Vector2(size.x, size.y), Vector2(0, size.y)
	]), Color("#121526"))
	draw_colored_polygon(PackedVector2Array([
		Vector2(0, size.y * .77), Vector2(size.x * .25, size.y * .51),
		Vector2(size.x * .47, size.y * .78), Vector2(size.x * .73, size.y * .48),
		Vector2(size.x, size.y * .72), Vector2(size.x, size.y), Vector2(0, size.y)
	]), Color("#171a28"))
	for i in 28:
		var x := fmod(float(i * 193 + 41), maxf(size.x, 1.0))
		var base_y := fmod(float(i * 83), maxf(size.y, 1.0))
		var y := fmod(base_y - drift * (22.0 + i % 5 * 8.0) + size.y, size.y)
		var glow := 1.3 + float(i % 3)
		draw_circle(Vector2(x, y), glow * 2.5, Color(0.93, 0.32, 0.18, 0.05))
		draw_circle(Vector2(x, y), glow, Color(1.0, 0.62, 0.28, 0.34))
