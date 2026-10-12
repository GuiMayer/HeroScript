extends VBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
## Passive dialogue view. Only emits the exact command advertised by the engine.
signal choice_requested(choice: Dictionary)
var choice_buttons: Array[Button] = []
var model: Dictionary = {}
var _intent_owner
var visual_context: ContextType
var _context_id := 0
func reconcile_from(other) -> void:
	visual_context = other.visual_context
	apply_model(other.model)
func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()

func apply_model(data: Dictionary) -> void:
	if model == data and _context_id == visual_context.get_instance_id(): return
	_context_id = visual_context.get_instance_id()
	var history = get_node_or_null("ConversationHistory")
	var expanded: bool = history != null and history.visible
	var desired = get_script().new(visual_context)
	desired._intent_owner = self
	desired.setup(data)
	model = data.duplicate(true)
	load("res://scripts/ui/view_reconciler.gd").children(self, desired)
	desired.free()
	history = get_node_or_null("ConversationHistory")
	if history:
		history.visible = expanded
		get_node("ConversationHistoryToggle").set_pressed_no_signal(expanded)
	choice_buttons.clear()
	for child in get_children():
		if child is Button and child.get_meta("activity_choice", false): choice_buttons.append(child)

func _emit_choice(button: Button) -> void:
	if not button.disabled: choice_requested.emit(button.get_meta("dialogue_command", {}).duplicate(true))

func _toggle_history(value: bool) -> void:
	var history := get_node_or_null("ConversationHistory")
	if history: history.visible = value

func setup(data: Dictionary) -> void:
	theme = visual_context.theme
	_context_id = visual_context.get_instance_id()
	model = data.duplicate(true)
	var coordinator = self if _intent_owner == null else _intent_owner
	name = "DialoguePanel"
	add_theme_constant_override("separation", 16)
	var heading := visual_context.styles.wrap(visual_context.styles.title(str(model.title), 28, visual_context.styles.GOLD), 180)
	add_child(heading)
	var speech := HBoxContainer.new()
	speech.add_theme_constant_override("separation", 20)
	var portrait := preload("res://scripts/ui/art_slot.gd").new(visual_context)
	portrait.setup("portraits", str(model.portrait))
	portrait.custom_minimum_size = Vector2(130, 160)
	speech.add_child(portrait)
	var words := VBoxContainer.new()
	words.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	words.add_theme_constant_override("separation", 12)
	var speaker := visual_context.styles.wrap(visual_context.styles.title(str(model.speaker), 20, visual_context.styles.EMBER), 160)
	words.add_child(speaker)
	var text := visual_context.styles.wrap(visual_context.styles.title(str(model.text), 21), 220)
	text.name = "DialogueText"
	words.add_child(text)
	speech.add_child(words)
	add_child(visual_context.styles.panel(speech))
	for option in model.options:
		var button := visual_context.styles.button(str(option.text))
		button.name = "Choice_" + str(option.id)
		if not str(option.cost).is_empty(): button.text += "   ·   " + str(option.cost)
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		button.disabled = not bool(option.available)
		button.set_meta("engine_disabled", button.disabled)
		button.tooltip_text = str(option.reason)
		if not button.disabled and not choice_buttons.any(func(item): return not item.disabled): button.set_meta("initial_focus", true)
		button.set_meta("activity_choice", true)
		button.set_meta("dialogue_command", option.command.duplicate(true))
		button.pressed.connect(coordinator._emit_choice.bind(button))
		choice_buttons.append(button)
		add_child(button)
		if not bool(option.available) and not str(option.reason).is_empty(): add_child(visual_context.styles.muted(str(option.reason), 13))
	if not model.history.is_empty():
		var history := VBoxContainer.new()
		history.name = "ConversationHistory"
		history.visible = false
		history.add_theme_constant_override("separation", 10)
		for entry in model.history:
			history.add_child(visual_context.styles.muted((str(entry.speaker) + ": " if not str(entry.speaker).is_empty() else "") + str(entry.text), 15))
		var toggle := visual_context.styles.button(visual_context.translator.text("CONVERSATION HISTORY"))
		toggle.toggle_mode = true
		toggle.name = "ConversationHistoryToggle"
		toggle.toggled.connect(coordinator._toggle_history)
		add_child(toggle)
		add_child(history)
