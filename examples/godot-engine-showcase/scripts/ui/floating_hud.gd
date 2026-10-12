extends CanvasLayer
## Non-modal reading/context overlays. Independent of battlefield layout and card z.
var host := Control.new()
var _slots: Dictionary = {}

func replace_slot(slot: String, node: Control) -> void:
	if _slots.get(slot) == node: return
	release_slot(slot)
	_slots[slot] = node
	if node.get_parent() == null: host.add_child(node)
	elif node.get_parent() != host: node.reparent(host, false)

func take_slot(slot: String) -> Control:
	var node: Control = _slots.get(slot)
	_slots.erase(slot)
	return node

func release_slot(slot: String) -> void:
	var node = _slots.get(slot)
	_slots.erase(slot)
	if is_instance_valid(node):
		if node.get_parent() == host: host.remove_child(node)
		node.queue_free()

func _init() -> void:
	name = "FloatingCombatHUD"
	layer = preload("res://scripts/ui/shell/overlay_layers.gd").FLOATING
	host.name = "FloatingHUDHost"
	host.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(host)

func fit_to(rect: Rect2, view_theme: Theme) -> void:
	host.theme = view_theme
	host.position = rect.position
	host.size = rect.size
