extends VBoxContainer
## Passive dialogue view. Only emits the exact command advertised by the engine.
signal choice_requested(choice: Dictionary)
var choice_buttons: Array[Button] = []

func setup(model: Dictionary) -> void:
	name = "DialoguePanel"
	add_theme_constant_override("separation", 16)
	var heading := AppTheme.wrap(AppTheme.title(str(model.title), 28, AppTheme.GOLD), 180)
	add_child(heading)
	var speech := HBoxContainer.new()
	speech.add_theme_constant_override("separation", 20)
	var portrait := preload("res://scripts/ui/art_slot.gd").new()
	portrait.setup("portraits", str(model.portrait))
	portrait.custom_minimum_size = Vector2(130, 160)
	speech.add_child(portrait)
	var words := VBoxContainer.new()
	words.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	words.add_theme_constant_override("separation", 12)
	var speaker := AppTheme.wrap(AppTheme.title(str(model.speaker), 20, AppTheme.EMBER), 160)
	words.add_child(speaker)
	var text := AppTheme.wrap(AppTheme.title(str(model.text), 21), 220)
	text.name = "DialogueText"
	words.add_child(text)
	speech.add_child(words)
	add_child(AppTheme.panel(speech))
	for option in model.options:
		var button := AppTheme.button(str(option.text))
		button.name = "Choice_" + str(option.id)
		if not str(option.cost).is_empty(): button.text += "   ·   " + str(option.cost)
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		button.disabled = not bool(option.available)
		button.set_meta("engine_disabled", button.disabled)
		button.tooltip_text = str(option.reason)
		if not button.disabled and not choice_buttons.any(func(item): return not item.disabled): button.set_meta("initial_focus", true)
		button.pressed.connect(func(): choice_requested.emit(option.command.duplicate(true)))
		choice_buttons.append(button)
		add_child(button)
		if not bool(option.available) and not str(option.reason).is_empty(): add_child(AppTheme.muted(str(option.reason), 13))
	if not model.history.is_empty():
		var history := VBoxContainer.new()
		history.visible = false
		history.add_theme_constant_override("separation", 10)
		for entry in model.history:
			history.add_child(AppTheme.muted((str(entry.speaker) + ": " if not str(entry.speaker).is_empty() else "") + str(entry.text), 15))
		var toggle := AppTheme.button(I18n.text("CONVERSATION HISTORY"))
		toggle.toggle_mode = true
		toggle.toggled.connect(func(value): history.visible = value)
		add_child(toggle)
		add_child(history)
