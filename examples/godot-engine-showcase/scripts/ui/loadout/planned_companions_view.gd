@tool
class_name PlannedCompanionsView
extends VBoxContainer
## Informational placeholder, never an inventory or a progression feature.
var visual_context
static func create() -> PlannedCompanionsView:
	return (load("res://scenes/ui/components/loadout/planned_companions.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null: mount(preload("res://scripts/ui/visual_context.gd").new())
func mount(context) -> void:
	visual_context = context; theme = context.theme
	get_node("Title").text = context.translator.text("Companions · Planned")
	get_node("Description").text = context.translator.text("Companions are not implemented. There is no equipment or progression to save here.")
