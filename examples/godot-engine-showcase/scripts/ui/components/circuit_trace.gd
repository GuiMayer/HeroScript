extends Control
## Static parallel offset polylines. No input, minimum-size or frame processing.
@export_range(1, 8) var tracks := 5
@export var spacing := 4.0
@export var thickness := 1.0
@export var ink := Color("#20201c")
@export var terminal := Color("#b83e25")
@export var mirrored := false
var simplified := false:
	set(value):
		simplified = value
		queue_redraw()
func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	focus_mode = Control.FOCUS_NONE
	clip_contents = true
	resized.connect(queue_redraw)
func paths() -> Array[PackedVector2Array]:
	var result: Array[PackedVector2Array] = []
	var reach := maxf(110.0, size.x - 30)
	for i in tracks:
		var offset := i * spacing
		# Mitered offset: adjacent diagonal segments remain spacing apart,
		# rather than merely translating an elbow vertically.
		var miter := offset * (1.0 - sqrt(2.0))
		var line := PackedVector2Array([Vector2(0, 8 + offset), Vector2(48 + miter, 8 + offset), Vector2(88 + miter, 48 + offset), Vector2(reach, 48 + offset)])
		if mirrored:
			for j in line.size(): line[j].x = size.x - line[j].x
		result.append(line)
	return result
func _draw() -> void:
	if simplified: return
	for line in paths():
		draw_polyline(line, ink, thickness, true)
		draw_rect(Rect2(line[-1] - Vector2(2, 2), Vector2(4, 4)), terminal)
