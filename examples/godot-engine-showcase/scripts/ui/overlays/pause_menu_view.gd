@tool
extends CanvasLayer
## Passive modal chrome. The application shell owns all gameplay callbacks.
signal intent_requested(action: String)
@export var visual_profile: Resource = preload("res://data/themes/ivory_theme.tres")
@export var preview_allow_abandon := true
var visual_context
var _surface: StyleBoxFlat
var _allow_abandon := false
var _has_history := false
func _ready() -> void:
	if visual_context == null and (Engine.is_editor_hint() or get_parent() == get_tree().root):
		setup(preload("res://scripts/ui/visual_context.gd").new(null,{},visual_profile),preview_allow_abandon,false)
func setup(context, allow_abandon: bool, has_history: bool) -> void:
	_allow_abandon = allow_abandon; _has_history = has_history
	get_node("%AbandonRunButton").visible = allow_abandon
	get_node("%ConversationHistoryButton").disabled = not has_history
	for button in find_children("*","Button",true,false):
		var callback := _emit.bind(str(button.get_meta("action")))
		if not button.pressed.is_connected(callback): button.pressed.connect(callback)
	apply_context(context)
func apply_context(context) -> void:
	visual_context = context
	var overlay = get_node("%PauseOverlay")
	overlay.theme = context.theme; overlay.color = context.styles.color("surface.modal_scrim")
	var panel = get_node("%Frame")
	var current: StyleBoxFlat = panel.get_theme_stylebox("panel")
	if _surface == null: _surface = current.duplicate()
	else:
		for side in [SIDE_LEFT,SIDE_TOP,SIDE_RIGHT,SIDE_BOTTOM]: _surface.set_content_margin(side,current.get_content_margin(side))
	_surface.bg_color = context.styles.PANEL; _surface.border_color = context.styles.color("stroke.panel")
	_surface.set_meta("background_role","surface.panel"); _surface.set_meta("border_role","stroke.panel")
	panel.add_theme_stylebox_override("panel",_surface)
	if panel.has_meta("base_style_panel"): panel.remove_meta("base_style_panel")
	if panel.has_meta("visual_preferences_revision"): panel.remove_meta("visual_preferences_revision")
	for node in find_children("*","Control",true,false):
		if node.has_meta("translation_key"): node.text = context.translator.text(str(node.get_meta("translation_key")))
	get_node("%PauseCircuit").simplified = context.high_contrast
	# Do not serialize runtime preference-cache metadata into authored editor nodes.
	if not Engine.is_editor_hint(): context.styles.apply_view_preferences(overlay,context.text_scale,context.high_contrast)
func _emit(action: String) -> void: intent_requested.emit(action)
