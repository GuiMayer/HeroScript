extends "res://scripts/ui/screen_view.gd"
## Isolated passive scene. No session, consequences or gameplay transport.
signal intent_requested(action: String, arguments: Array)
var panel
var _model: Dictionary = {}
var _header: HBoxContainer
var _locked := false
var _loading: Label
var _recover: Button
func apply_projection(model: Dictionary, context: ContextType) -> void:
	mount(context); theme = context.theme
	if not is_instance_valid(_header):
		_header = HBoxContainer.new(); add_child(_header)
		var title := context.styles.title(context.translator.text("CONVERSATION"), 26)
		title.name = "ConversationTitle"
		title.size_flags_horizontal = Control.SIZE_EXPAND_FILL; _header.add_child(title)
		for action in ["map", "pause"]:
			var button := context.styles.button(context.translator.text("MAP" if action == "map" else "PAUSE"), 110)
			button.name = "Conversation_" + action
			button.pressed.connect(func(): intent_requested.emit(action, [])); _header.add_child(button)
		_loading = context.styles.muted(context.translator.text("Loading conversation…"), 18)
		add_child(_loading)
		_recover = context.styles.button(context.translator.text("RECONNECT"), 160)
		_recover.pressed.connect(func(): intent_requested.emit("recover", [])); add_child(_recover)
		panel = preload("res://scripts/ui/dialogue_panel.gd").new(context)
		panel.name = "DialoguePanel"
		panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
		panel.choice_requested.connect(_choice)
		add_child(panel)
	_header.get_node("ConversationTitle").text = context.translator.text("CONVERSATION")
	_header.get_node("Conversation_map").text = context.translator.text("MAP")
	_header.get_node("Conversation_pause").text = context.translator.text("PAUSE")
	_loading.text = context.translator.text("Loading conversation…")
	_recover.text = context.translator.text("RECONNECT")
	if not model.is_empty():
		_model = model.duplicate(true)
		panel.visual_context = context
		panel.apply_model(_model)
		panel.show()
	else: panel.hide()
	_loading.visible = model.is_empty()
	_recover.visible = model.is_empty()
	context.styles.apply_view_preferences(self)
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
