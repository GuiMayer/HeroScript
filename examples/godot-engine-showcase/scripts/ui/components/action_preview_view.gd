@tool
class_name ActionPreviewView
extends VBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
var detail_label: RichTextLabel
var selection_label: Label
var queue_label: Label
static func create() -> ActionPreviewView:
	return (load("res://scenes/ui/components/action_preview.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if detail_label == null: mount(visual_context.translator.text("Select a card, then choose a highlighted target."))
func mount(hint: String) -> void:
	theme = visual_context.theme
	detail_label = get_node("Content/ActionPreview"); selection_label = get_node("Selection"); queue_label = get_node("Queue")
	detail_label.text = hint
	get_node("Content/Arrow").texture = preload("res://scripts/ui/combat_chrome.gd").texture("arrow")
	get_node("Content/Arrow").modulate = visual_context.styles.INK
	queue_label.add_theme_color_override("font_color", visual_context.styles.TEAL)
