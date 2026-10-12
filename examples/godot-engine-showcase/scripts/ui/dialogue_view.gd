extends "res://scripts/ui/screen_view.gd"
## Isolated passive scene. No session, consequences or gameplay transport.
signal intent_requested(action: String, arguments: Array)
var panel
var _model: Dictionary = {}
var _header: HBoxContainer
var _content: ScrollContainer
var _locked := false
var _loading: Label
var _recover: Button
func apply_projection(model: Dictionary, context: ContextType) -> void:
	mount(context); theme = context.theme
	if not is_instance_valid(_header):
		_header = HBoxContainer.new(); add_child(_header)
		var title := context.styles.title(context.translator.text("CONVERSATION"), 26)
		title.size_flags_horizontal = Control.SIZE_EXPAND_FILL; _header.add_child(title)
		for action in ["map", "pause"]:
			var button := context.styles.button(context.translator.text("MAP" if action == "map" else "PAUSE"), 110)
			button.pressed.connect(func(): intent_requested.emit(action, [])); _header.add_child(button)
		_content = ScrollContainer.new(); _content.size_flags_vertical = Control.SIZE_EXPAND_FILL
		_content.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED; add_child(_content)
		_loading = context.styles.muted(context.translator.text("Loading conversation…"), 18)
		add_child(_loading)
		_recover = context.styles.button(context.translator.text("RECONNECT"), 160)
		_recover.pressed.connect(func(): intent_requested.emit("recover", [])); add_child(_recover)
		panel = preload("res://scripts/ui/dialogue_panel.gd").new(context)
		panel.name = "DialoguePanel"
		panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		panel.choice_requested.connect(_choice)
		_content.add_child(panel)
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
		for button in panel.choice_buttons: button.disabled = value or bool(button.get_meta("engine_disabled", false))
