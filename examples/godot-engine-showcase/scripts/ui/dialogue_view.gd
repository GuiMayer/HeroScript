@tool
extends "res://scripts/ui/screen_view.gd"
## Isolated passive scene. No session, consequences or gameplay transport.
signal intent_requested(action: String, arguments: Array)
var panel
var _model: Dictionary = {}
var _header: HBoxContainer
var _locked := false
var _loading: Label
var _recover: Button
func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint():
		mount(ContextType.new()); theme = visual_context.theme
func _bind_nodes() -> void:
	if is_instance_valid(_header): return
	_header = get_node("Header"); _loading = get_node("Loading"); _recover = get_node("Recover")
	panel = get_node("DialoguePanel")
	for action in ["map", "pause"]:
		_header.get_node("Conversation_" + action).pressed.connect(func(): intent_requested.emit(action, []))
	_recover.pressed.connect(func(): intent_requested.emit("recover", []))
	panel.choice_requested.connect(_choice)
func apply_projection(model: Dictionary, context: ContextType) -> void:
	mount(context); theme = context.theme
	_bind_nodes()
	_header.get_node("ConversationTitle").text = context.translator.text("CONVERSATION")
	_header.get_node("Conversation_map").text = context.translator.text("MAP")
	_header.get_node("Conversation_pause").text = context.translator.text("PAUSE")
	_recover.text = context.translator.text("RECONNECT")
	_model = model.duplicate(true)
	if not model.is_empty():
		panel.visual_context = context
		panel.apply_model(_model)
		panel.show()
	else: panel.hide()
	set_loading_state(false)
	context.styles.apply_view_preferences(self)
func set_loading_state(failed: bool) -> void:
	_loading.visible = _model.is_empty()
	_loading.text = visual_context.translator.text(
		"Could not refresh the game. Reconnect before playing again." if failed else "Loading conversation…")
	_recover.visible = _loading.visible and failed
func _choice(command: Dictionary) -> void:
	if _locked: return
	for option in _model.get("options", []):
		if option.command == command: intent_requested.emit("choice", [option.reference]); return
func set_read_only(value: bool) -> void:
	_locked = value
	if is_instance_valid(panel):
		panel.set_read_only(value)
func capture_local_state() -> Dictionary: return panel.capture_local_state()
func restore_local_state(state: Dictionary) -> void: panel.restore_local_state(state)
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("confirm_action") and not event.is_echo() and panel.visible:
		panel.advance_reading(); get_viewport().set_input_as_handled()
