extends AcceptDialog
class_name InspectionDialogView
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var content: VBoxContainer
var _summary := ""
var _data: Dictionary = {}
var _detail: Control
func _init(context = null) -> void:
    visual_context = context if context != null else ContextType.new()
static func create(context = null) -> InspectionDialogView:
    var dialog = (load("res://scenes/ui/overlays/inspection_dialog.tscn") as PackedScene).instantiate()
    if context != null: dialog.visual_context = context
    return dialog
func _bind_nodes() -> void:
    content = get_node("%Content")
    var technical: CheckButton = get_node("%TechnicalToggle")
    if not technical.toggled.is_connected(_sync_display): technical.toggled.connect(_sync_display)
    if not confirmed.is_connected(queue_free): confirmed.connect(queue_free)
    if not canceled.is_connected(queue_free): canceled.connect(queue_free)
    if not visibility_changed.is_connected(_on_visibility_changed): visibility_changed.connect(_on_visibility_changed)
func setup(title_text: String, summary: String, data: Dictionary = {}, card_model: Dictionary = {}, style: Resource = null) -> void:
    _bind_nodes()
    theme = visual_context.theme.duplicate()
    title = title_text
    _summary = summary; _data = data.duplicate(true)
    var slot = get_node("%DetailSlot")
    if not card_model.is_empty():
        if not is_instance_valid(_detail):
            _detail = preload("res://scripts/ui/card_details_panel.gd").new(visual_context)
            _detail.size_flags_vertical = Control.SIZE_EXPAND_FILL
            slot.add_child(_detail)
        _detail.configure(card_model, style if style != null else visual_context.card_style)
    elif is_instance_valid(_detail):
        slot.remove_child(_detail); _detail.queue_free(); _detail = null
    var technical: CheckButton = get_node("%TechnicalToggle")
    technical.text = visual_context.translator.text("Show engine details")
    technical.visible = not data.is_empty() and str(data.get("detail", "Full")) in ["Full", "2", "2.0"]
    technical.set_pressed_no_signal(false)
    _sync_display(false)
func _sync_display(technical := false) -> void:
    var summary: RichTextLabel = get_node("%Summary")
    summary.text = JSON.stringify(_data, "  ") if technical else _summary
    var has_detail := is_instance_valid(_detail)
    summary.visible = technical or not has_detail
    get_node("%DetailSlot").visible = has_detail and not technical
func _on_visibility_changed() -> void:
    if not is_instance_valid(content): return
    if visible: preload("res://scripts/ui/panel_motion.gd").enter(content, visual_context)
    else: preload("res://scripts/ui/panel_motion.gd").cancel(content)
