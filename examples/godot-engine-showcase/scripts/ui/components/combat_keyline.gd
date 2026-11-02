@tool
extends Control
var ink := Color.BLACK
var cut := 8.0
var width := 1.0
var focus_ink := Color.RED
var _last: Array = []
func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE; resized.connect(queue_redraw)
func _process(_delta: float) -> void:
	var button := get_parent() as Button
	if button == null: return
	var state: Array = [button.has_focus(), button.is_hovered(), button.disabled]
	if state != _last: _last = state; queue_redraw()
func _draw() -> void:
	var r := size - Vector2(2, 2); var c := minf(cut, r.y * .25)
	var active: bool = get_parent() is Button and get_parent().has_focus()
	draw_polyline(PackedVector2Array([Vector2(1+c,1), Vector2(r.x,1), Vector2(r.x,r.y-c), Vector2(r.x-c,r.y), Vector2(1,r.y), Vector2(1,1+c), Vector2(1+c,1)]), focus_ink if active else ink, width + 1 if active else width, true)
