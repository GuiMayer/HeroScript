class_name ActorPortrait
extends Control

var hostile := false
var boss := false
var pulse := 0.0

func configure(is_hostile: bool, is_boss := false) -> void:
	hostile = is_hostile
	boss = is_boss
	queue_redraw()

func flash_hit() -> void:
	pulse = 1.0
	var tween := create_tween()
	tween.tween_property(self, "pulse", 0.0, 0.32 / maxf(Preferences.animation_speed, .25))
	tween.tween_callback(queue_redraw)

func _process(_delta: float) -> void:
	if pulse > 0.0:
		queue_redraw()

func _draw() -> void:
	var ratio := minf(size.y / 190.0, 1.0)
	draw_set_transform(Vector2(size.x * (1.0 - ratio) * .5, 0), 0, Vector2.ONE * ratio)
	var center := Vector2(size.x * .5, 190.0 * .54)
	var base := AppTheme.BLOOD if hostile else AppTheme.TEAL
	if pulse > 0.0:
		base = base.lerp(Color.WHITE, pulse)
	draw_circle(center + Vector2(0, 45), 74 if boss else 60, Color(0, 0, 0, .23))
	if hostile:
		var body := PackedVector2Array([
			center + Vector2(-54, 58), center + Vector2(-37, -22),
			center + Vector2(-18, -61), center + Vector2(0, -82 if boss else -66),
			center + Vector2(23, -57), center + Vector2(44, -19),
			center + Vector2(58, 58)
		])
		draw_colored_polygon(body, base.darkened(.46))
		draw_polyline(body, base, 4.0, true)
		draw_circle(center + Vector2(-18, -22), 6, AppTheme.GOLD)
		draw_circle(center + Vector2(18, -22), 6, AppTheme.GOLD)
		if boss:
			for x in [-38, 0, 38]:
				draw_colored_polygon(PackedVector2Array([
					center + Vector2(x - 13, -55), center + Vector2(x, -103), center + Vector2(x + 13, -55)
				]), base)
	else:
		draw_circle(center + Vector2(0, -27), 33, Color("#d8b49a"))
		draw_colored_polygon(PackedVector2Array([
			center + Vector2(-58, 72), center + Vector2(-38, 4),
			center + Vector2(0, -3), center + Vector2(42, 4), center + Vector2(62, 72)
		]), base.darkened(.36))
		draw_arc(center + Vector2(0, 0), 48, PI * .08, PI * .92, 20, base, 8)
		draw_circle(center + Vector2(-11, -29), 3, Color("#17212a"))
		draw_circle(center + Vector2(11, -29), 3, Color("#17212a"))
		draw_arc(center + Vector2(0, 12), 20, PI * 1.1, PI * 1.9, 14, AppTheme.EMBER, 5)
