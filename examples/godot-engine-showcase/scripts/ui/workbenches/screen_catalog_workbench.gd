extends Control
## Offline harness: instances production visuals, never controllers or gateways.
const Registry = preload("res://scripts/ui/scene_registry.gd")
const Context = preload("res://scripts/ui/visual_context.gd")
var visual_context = Context.new()
var preview: Node
func _ready() -> void:
	theme = visual_context.theme
	get_node("%Background").color = visual_context.styles.NIGHT
	var selector: OptionButton = get_node("%ScreenSelector")
	selector.add_item("Shared components")
	selector.add_item("Loadout laboratory")
	selector.add_item("Main menu")
	selector.item_selected.connect(show_preview)
	show_preview(0)
func show_preview(index: int) -> void:
	var host = get_node("%ContentHost")
	if is_instance_valid(preview):
		host.remove_child(preview)
		preview.queue_free()
	if index == 1: preview = Registry.instantiate("loadout_lab")
	elif index == 2: preview = Registry.instantiate("menu")
	else: preview = load("res://scenes/ui/workbenches/ui_catalog_workbench.tscn").instantiate()
	host.add_child(preview)
