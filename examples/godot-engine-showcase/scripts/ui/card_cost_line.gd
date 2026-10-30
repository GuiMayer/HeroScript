@tool
extends Control
## One passive resource line; long alternative costs fit without wrapping the face.
var content: HBoxContainer
func _init(row: HBoxContainer, height: float) -> void:
	name = "CostLine"
	custom_minimum_size.y = height
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	content = row
	add_child(content)
func reconcile_from(other) -> void:
	custom_minimum_size = other.custom_minimum_size
	preload("res://scripts/ui/view_reconciler.gd").children(self, other)
	content = get_child(0)
	_subscribe()
	_fit.call_deferred()
func _ready() -> void:
	resized.connect(_fit)
	_subscribe()
	_fit.call_deferred()
func _subscribe() -> void:
	if not content.minimum_size_changed.is_connected(_fit): content.minimum_size_changed.connect(_fit)
func _fit() -> void:
	if not is_inside_tree(): return
	var natural := content.get_combined_minimum_size()
	content.size = natural
	var factor := minf(1.0, minf(size.x / maxf(natural.x, 1.0), size.y / maxf(natural.y, 1.0)))
	content.scale = Vector2.ONE * factor
	content.position = Vector2(0.0, (size.y - natural.y * factor) * .5)
