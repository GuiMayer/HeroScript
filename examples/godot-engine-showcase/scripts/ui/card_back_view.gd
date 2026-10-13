extends Panel
## Anonymous printed back: no identity, effect or hidden order is represented.
var ink := Color("#20201c")
func _ready() -> void: resized.connect(queue_redraw)
func _draw() -> void:
	var inset := size * .065
	draw_rect(Rect2(inset, size - inset * 2), ink, false, 1.0)
	var center := size * .5
	var radius := minf(size.x, size.y) * .15
	for factor in [1.0, .8]:
		var r: float = radius * factor
		draw_polyline(PackedVector2Array([center + Vector2(0, -r), center + Vector2(r, 0), center + Vector2(0, r), center + Vector2(-r, 0), center + Vector2(0, -r)]), ink, 1.0, true)
	for line in 4:
		var y := size.y * .26 + line * 5.0
		draw_polyline(PackedVector2Array([Vector2(inset.x, y), Vector2(size.x * .52, y), Vector2(size.x * .68, y + size.x * .16), Vector2(size.x - inset.x, y + size.x * .16)]), ink, 1.0, true)
