class_name FloatingCombatHUD
extends CanvasLayer
## Non-modal reading/context overlays. Independent of battlefield layout and card z.
var host: Control
var _slots: Dictionary = {}

static func create() -> FloatingCombatHUD:
	var view = (load("res://scenes/ui/components/floating_hud.tscn") as PackedScene).instantiate()
	view.host = view.get_node("FloatingHUDHost"); return view
func _bind_nodes() -> void:
	host = get_node("FloatingHUDHost")
func replace_slot(slot: String, node: Control) -> void:
	_bind_nodes()
	if _slots.get(slot) == node: return
	release_slot(slot)
	_slots[slot] = node
	if node.get_parent() == null: host.add_child(node)
	elif node.get_parent() != host:
		node.owner = null
		node.reparent(host, false)

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

func _ready() -> void:
	_bind_nodes()
	layer = preload("res://scripts/ui/shell/overlay_layers.gd").FLOATING

func fit_to(rect: Rect2, view_theme: Theme) -> void:
	_bind_nodes()
	host.theme = view_theme
	host.position = rect.position
	host.size = rect.size
