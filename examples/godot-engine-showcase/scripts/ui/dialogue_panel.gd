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
var _history: Button
var _gradual: CheckButton
var _locked := false
var _choices_key := ""
func _init(context = null) -> void:
	visual_context = context if context != null else ContextType.new()
func apply_model(data: Dictionary) -> void:
	theme = visual_context.theme; model = data.duplicate(true)
	if not is_instance_valid(_speech): _build()
	reading.bind(model, visual_context.translator.locale)
	_title.text = str(model.get("title", ""))
	_speaker.text = str(model.get("speaker", "")); _speaker.visible = not _speaker.text.is_empty()
	_portrait.visual_context = visual_context
	_portrait.setup("portraits", str(model.get("portrait", "")))
	var key := JSON.stringify([model.get("options", []), visual_context.presentation_key()])
	if key != _choices_key:
		_choices_key = key
		var desired := VBoxContainer.new()
		for option in model.get("options", []):
			var button := visual_context.styles.button(str(option.text))
			button.name = "Choice_" + str(option.id)
			if not str(option.cost).is_empty(): button.text += "   ·   " + str(option.cost)
			button.alignment = HORIZONTAL_ALIGNMENT_LEFT
			button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			button.set_meta("engine_disabled", not bool(option.available))
			button.set_meta("dialogue_command", option.command.duplicate(true))
			button.tooltip_text = str(option.reason)
			button.pressed.connect(func():
				if not button.disabled: choice_requested.emit(button.get_meta("dialogue_command").duplicate(true)))
			desired.add_child(button)
			if not str(option.reason).is_empty():
				var reason := visual_context.styles.muted(str(option.reason), 15)
				reason.name = "Reason_" + str(option.id); desired.add_child(reason)
		preload("res://scripts/ui/view_reconciler.gd").children(_choices, desired)
		desired.free(); choice_buttons.clear()
		for child in _choices.get_children():
			if child is Button: choice_buttons.append(child)
	_history.text = visual_context.translator.text("CONVERSATION HISTORY")
	_history.disabled = model.get("history", []).is_empty()
	_gradual.text = visual_context.translator.text("Reveal text gradually")
	_sync_reading()
func _build() -> void:
	name = "DialoguePanel"; add_theme_constant_override("separation", 14)
	var stage := HBoxContainer.new(); stage.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_portrait = preload("res://scripts/ui/art_slot.gd").new(visual_context)
	_portrait.custom_minimum_size = Vector2(160, 150); _portrait.size_flags_vertical = Control.SIZE_EXPAND_FILL
	stage.add_child(_portrait)
	_title = visual_context.styles.title("", 28); _title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL; _title.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	stage.add_child(_title); add_child(stage)
	var speech_box := VBoxContainer.new(); speech_box.add_theme_constant_override("separation", 10)
	_speaker = visual_context.styles.title("", 21); speech_box.add_child(_speaker)
	_speech = RichTextLabel.new(); _speech.name = "DialogueText"
	_speech.custom_minimum_size.y = 115; _speech.selection_enabled = true
	_speech.add_theme_font_size_override("normal_font_size", 21)
	_speech.bbcode_enabled = false; speech_box.add_child(_speech)
	var tools := HFlowContainer.new()
	_next = visual_context.styles.button(""); _next.name = "DialogueReadNext"
	_next.pressed.connect(advance_reading); tools.add_child(_next)
	_history = visual_context.styles.button(""); _history.pressed.connect(_open_history); tools.add_child(_history)
	_gradual = CheckButton.new(); _gradual.toggled.connect(func(value): reading.gradual = value; _sync_reading())
	tools.add_child(_gradual); speech_box.add_child(tools)
	add_child(visual_context.styles.panel(speech_box))
	var scroll := ScrollContainer.new(); scroll.custom_minimum_size.y = 150
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_choices = VBoxContainer.new(); _choices.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_choices.add_theme_constant_override("separation", 8); scroll.add_child(_choices); add_child(scroll)
func advance_reading() -> void: reading.advance_reading(); _sync_reading()
func _sync_reading() -> void:
	if not is_instance_valid(_speech): return
	if _speech.text != reading.text(): _speech.text = reading.text()
	_speech.visible_characters = -1 if reading.fully_revealed() else int(reading.revealed)
	_next.visible = not reading.fully_revealed() or not reading.last_page()
	_next.text = visual_context.translator.text("Reveal text" if not reading.fully_revealed() else "Next page") + " · %s/%s" % [reading.page + 1, reading.pages.size()]
	_gradual.set_pressed_no_signal(reading.gradual)
	_choices.visible = reading.fully_revealed() and reading.last_page()
	for button in choice_buttons: button.disabled = _locked or not _choices.visible or bool(button.get_meta("engine_disabled", false))
func set_read_only(value: bool) -> void: _locked = value; _sync_reading()
func _process(delta: float) -> void:
	if not is_instance_valid(_speech) or reading.fully_revealed(): return
	reading.revealed += delta * 45.; _sync_reading()
func _open_history() -> void:
	var lines: Array[String] = []
	for entry in model.get("history", []): lines.append((str(entry.speaker) + ": " if not str(entry.speaker).is_empty() else "") + str(entry.text))
	var dialog = preload("res://scripts/ui/inspection_dialog.gd").new(visual_context)
	dialog.setup(visual_context.translator.text("CONVERSATION HISTORY"), "\n\n".join(lines))
	add_child(dialog); dialog.popup_centered()
func capture_local_state() -> Dictionary: return reading.capture()
func restore_local_state(state: Dictionary) -> void: reading.restore(state); _sync_reading()
