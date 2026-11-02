@tool
extends VBoxContainer
## Single passive narrative composition. Does not interpret consequences.
const ContextType = preload("res://scripts/ui/visual_context.gd")
signal choice_requested(choice: Dictionary)
var visual_context: ContextType
var model: Dictionary = {}
var choice_buttons: Array[Button] = []
var reading = preload("res://scripts/ui/dialogue_reading.gd").new()
var _speech: RichTextLabel
var _speaker: Label
var _title: Label
var _portrait
var _choices: VBoxContainer
var _next: Button
var _locked := false
var _choices_key := ""
static func create(context = null):
	var view = load("res://scenes/ui/components/dialogue_panel.tscn").instantiate()
	view.visual_context = context if context != null else ContextType.new()
	return view
func _ready() -> void:
	_bind_nodes()
	if visual_context == null: visual_context = ContextType.new()
	theme = visual_context.theme
func _bind_nodes() -> void:
	if is_instance_valid(_speech): return
	_portrait = get_node("Stage/Portrait"); _title = get_node("Stage/Title")
	_speaker = get_node("SpeechPanel/Content/Speaker"); _speech = get_node("SpeechPanel/Content/DialogueText")
	_next = get_node("SpeechPanel/Content/Tools/DialogueReadNext"); _choices = get_node("ChoiceScroll/Choices")
	_next.pressed.connect(advance_reading)
func apply_model(data: Dictionary) -> void:
	theme = visual_context.theme; model = data.duplicate(true)
	_bind_nodes()
	if visual_context == null: visual_context = ContextType.new()
	reading.bind(model, visual_context.translator.locale)
	reading.gradual = visual_context.reveal_text_gradually
	_title.text = str(model.get("title", ""))
	_speaker.text = str(model.get("speaker", "")); _speaker.visible = not _speaker.text.is_empty()
	_portrait.visual_context = visual_context
	_portrait.setup("portraits", str(model.get("portrait", "")))
	var key := JSON.stringify([model.get("options", []), visual_context.presentation_key()])
	if key != _choices_key:
		_choices_key = key
		var live: Array[String] = []
		choice_buttons.clear()
		for option in model.get("options", []):
			var id := "Choice_" + str(option.id); live.append(id)
			var row = _choices.get_node_or_null(NodePath(id))
			if row == null:
				row = preload("res://scenes/ui/components/dialogue_choice.tscn").instantiate()
				row.name = id; _choices.add_child(row)
				var target: Button = row.get_node("Choose")
				target.pressed.connect(func():
					if not target.disabled: choice_requested.emit(target.get_meta("dialogue_command").duplicate(true)))
			_choices.move_child(row, live.size()-1)
			var button: Button = row.get_node("Choose")
			button.text = str(option.text)
			if not str(option.cost).is_empty(): button.text += "   ·   " + str(option.cost)
			button.set_meta("engine_disabled", not bool(option.available))
			button.set_meta("dialogue_command", option.command.duplicate(true))
			button.tooltip_text = str(option.reason)
			row.get_node("Reason").text = str(option.reason)
			row.get_node("Reason").visible = not str(option.reason).is_empty()
			choice_buttons.append(button)
		for row in _choices.get_children():
			if str(row.name) not in live: _choices.remove_child(row); row.queue_free()
	_sync_reading()
func advance_reading() -> void: reading.advance_reading(); _sync_reading()
func _sync_reading() -> void:
	if not is_instance_valid(_speech) or model.is_empty(): return
	if _speech.text != reading.text(): _speech.text = reading.text()
	_speech.visible_characters = -1 if reading.fully_revealed() else int(reading.revealed)
	_next.visible = not reading.fully_revealed() or not reading.last_page()
	_next.text = visual_context.translator.text("Reveal text" if not reading.fully_revealed() else "Next page") + " · %s/%s" % [reading.page + 1, reading.pages.size()]
	_choices.visible = reading.fully_revealed() and reading.last_page()
	for button in choice_buttons: button.disabled = _locked or not _choices.visible or bool(button.get_meta("engine_disabled", false))
func set_read_only(value: bool) -> void: _locked = value; _sync_reading()
func _process(delta: float) -> void:
	if Engine.is_editor_hint() or model.is_empty() or not is_instance_valid(_speech) or reading.fully_revealed(): return
	reading.revealed += delta * 45.; _sync_reading()
func capture_local_state() -> Dictionary: return reading.capture()
func restore_local_state(state: Dictionary) -> void: reading.restore(state); _sync_reading()
