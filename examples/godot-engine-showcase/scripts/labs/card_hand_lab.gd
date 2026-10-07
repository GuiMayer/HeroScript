extends Control
## Offline visual fixture harness. No gateway, rules, run mutation or preference save.
const HandView = preload("res://scripts/ui/card_hand_view.gd")
const Fixtures = "res://data/labs/card_hand_lab.json"
var catalog: Dictionary = {}
var hand
var card_controls: Array = []
var models: Array = []
var preset_index := 0
var count := 5
var selected_id := ""
var details: RichTextLabel
var status: Label
var preset_picker: OptionButton
var count_picker: SpinBox
var language_picker: OptionButton
var scale_picker: OptionButton
var layout_picker: OptionButton
var _prior_preferences: Dictionary = {}
var _rotation := 0

func _ready() -> void:
	_prior_preferences = {"text_scale": Preferences.text_scale, "high_contrast": Preferences.high_contrast,
		"reduced_motion": Preferences.reduced_motion, "locale": I18n.locale}
	catalog = JSON.parse_string(FileAccess.get_file_as_string(Fixtures))
	_build()
	set_preset(0)
	_capture_if_requested.call_deferred()

func _build() -> void:
	var background := ColorRect.new()
	background.color = AppTheme.NIGHT
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(background)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 24)
	add_child(margin)
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 12)
	column.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 12)
	margin.add_child(outer)
	var upper_scroll := ScrollContainer.new()
	upper_scroll.custom_minimum_size.y = 150
	upper_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	upper_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	upper_scroll.add_child(column)
	outer.add_child(upper_scroll)
	column.add_child(AppTheme.title("CARD / HAND LAB", 25, AppTheme.GOLD))
	column.add_child(AppTheme.muted("Offline visual samples. The same CardView and hand component used in combat — no game rules or API calls.", 13, 320))
	var tools := HFlowContainer.new()
	tools.add_theme_constant_override("h_separation", 12)
	tools.add_theme_constant_override("v_separation", 8)
	column.add_child(tools)
	preset_picker = OptionButton.new()
	preset_picker.name = "FixturePreset"
	preset_picker.custom_minimum_size = Vector2(340, 40)
	for preset in catalog.presets: preset_picker.add_item(str(preset.name))
	preset_picker.item_selected.connect(set_preset)
	tools.add_child(preset_picker)
	tools.add_child(AppTheme.title("Cards", 14))
	count_picker = SpinBox.new()
	count_picker.name = "HandCount"
	count_picker.min_value = 0
	count_picker.max_value = 40
	count_picker.step = 1
	count_picker.value = count
	count_picker.custom_minimum_size = Vector2(85, 40)
	count_picker.value_changed.connect(func(value): set_count(int(value)))
	tools.add_child(count_picker)
	language_picker = OptionButton.new()
	language_picker.add_item("English")
	language_picker.add_item("Português")
	language_picker.select(1 if I18n.locale == "pt_BR" else 0)
	language_picker.item_selected.connect(func(index): set_language("pt_BR" if index == 1 else "en"))
	tools.add_child(language_picker)
	scale_picker = OptionButton.new()
	for value in [90, 100, 120]: scale_picker.add_item("Text %s%%" % value)
	scale_picker.select(2 if Preferences.text_scale > 1.1 else (0 if Preferences.text_scale < 1.0 else 1))
	scale_picker.item_selected.connect(func(index): set_text_scale([.9, 1.0, 1.2][index]))
	tools.add_child(scale_picker)
	var contrast := CheckButton.new()
	contrast.text = "High contrast"
	contrast.button_pressed = Preferences.high_contrast
	contrast.toggled.connect(func(value): Preferences.high_contrast = value; _refresh())
	tools.add_child(contrast)
	var motion := CheckButton.new()
	motion.text = "Reduced motion"
	motion.button_pressed = Preferences.reduced_motion
	motion.toggled.connect(func(value): Preferences.reduced_motion = value; _refresh())
	tools.add_child(motion)
	var actions := HFlowContainer.new()
	actions.add_theme_constant_override("h_separation", 8)
	actions.add_child(_button("Empty hand", func(): set_count(0)))
	actions.add_child(_button("Restore preset", func(): set_preset(preset_index)))
	actions.add_child(_button("Rotate sample", func(): _rotation += 1; _refresh()))
	actions.add_child(_button("Remove selected", remove_selected))
	actions.add_child(_button("Inspect selected", inspect_selected))
	column.add_child(actions)
	var workspace := PanelContainer.new()
	workspace.name = "SampleDetails"
	workspace.size_flags_vertical = Control.SIZE_EXPAND_FILL
	workspace.custom_minimum_size.y = 70
	workspace.add_theme_stylebox_override("panel", AppTheme.box(AppTheme.PANEL, 12))
	details = RichTextLabel.new()
	details.name = "FullSampleText"
	details.selection_enabled = true
	details.bbcode_enabled = false
	details.add_theme_font_size_override("normal_font_size", 16)
	workspace.add_child(details)
	column.add_child(workspace)
	status = AppTheme.caption("", 13, 280)
	status.name = "LabStatus"
	column.add_child(status)
	hand = HandView.new()
	hand.card_activated.connect(select_card)
	hand.selection_requested.connect(select_card)
	hand.inspection_requested.connect(func(id): select_card(id); inspect_selected())
	hand.card_previewed.connect(func(_id): pass)
	outer.add_child(hand)
	layout_picker = OptionButton.new()
	layout_picker.name = "LabHandLayout"
	layout_picker.add_item(I18n.text("Adaptive fan"))
	layout_picker.add_item(I18n.text("Reading — no overlap"))
	layout_picker.item_selected.connect(func(index): hand.mode_override = "reading" if index == 1 else "adaptive"; hand.request_layout())
	tools.add_child(layout_picker)
	var drag_toggle := CheckButton.new()
	drag_toggle.text = I18n.text("Drag cards to play")
	drag_toggle.toggled.connect(func(enabled): hand.drag_override = enabled)
	tools.add_child(drag_toggle)
	actions.add_child(_button("Add sample", func(): set_count(count + 1)))
	actions.add_child(_button("Enrich selected", enrich_selected))
	var read_only := CheckButton.new()
	read_only.text = "Read-only / busy"
	read_only.toggled.connect(func(enabled): hand.set_read_only(enabled))
	actions.add_child(read_only)

func enrich_selected() -> void:
	for data in models:
		if str(data.labId) == selected_id:
			data["previewNote"] = "Enriched visual sample — no API call"
	_render_hand()

func _button(label: String, action: Callable) -> Button:
	var button := AppTheme.button(label)
	button.custom_minimum_size.y = 40
	button.pressed.connect(action)
	return button

func set_preset(index: int) -> void:
	preset_index = clampi(index, 0, catalog.presets.size() - 1)
	preset_picker.select(preset_index)
	_rotation = 0
	set_count(catalog.presets[preset_index].cards.size())

func set_count(value: int) -> void:
	count = clampi(value, 0, 40)
	count_picker.set_value_no_signal(count)
	selected_id = ""
	_refresh()

func set_language(locale: String) -> void:
	I18n.set_locale(locale)
	language_picker.select(1 if locale == "pt_BR" else 0)
	_refresh()

func set_text_scale(value: float) -> void:
	Preferences.text_scale = clampf(value, .9, 1.2)
	scale_picker.select(2 if Preferences.text_scale > 1.1 else (0 if Preferences.text_scale < 1.0 else 1))
	_refresh()

func _refresh() -> void:
	theme = AppTheme.build(Preferences.high_contrast)
	models.clear()
	var entries: Array = catalog.presets[preset_index].cards
	for i in count:
		var fixture_id: String = entries[(i + _rotation) % entries.size()]
		var data: Dictionary = catalog.cards[fixture_id].duplicate(true)
		data["rarityId"] = str(data.get("rarity", "")).to_lower()
		if I18n.locale == "pt_BR": data.merge(data.get("pt_BR", {}), true)
		data.erase("pt_BR")
		data["labId"] = "%s:%s" % [fixture_id, i]
		data["summary"] = "\n".join(data.get("effects", []))
		models.append(data)
	_render_hand()

func _render_hand() -> void:
	hand.sync_models(models, "lab:" + str(preset_index), I18n.text("Your hand is empty."))
	card_controls = hand.ordered_cards()
	AppTheme.apply_view_preferences(self)
	select_card(selected_id)
	preload("res://scripts/ui/focus_navigation.gd").wire.call_deferred(self)

func select_card(id: String) -> void:
	selected_id = id
	hand.set_selection(id)
	var selected := selected_model()
	status.text = "%s cards · %s · %s%% text · visual samples only" % [models.size(), I18n.locale, roundi(Preferences.text_scale * 100)]
	details.text = "Select a sample to see the complete text. Use the horizontal scrollbar when the hand is full.\n\nUnknown art/resources/types, long titles, stacked changes and translated rules deliberately exercise the current component limits. No effects are executed here."
	if not selected.is_empty():
		details.text = "%s\n%s · %s\n\n%s\n\n%s" % [selected.name, selected.definitionId, selected.cardType, selected.summary, selected.availability]
		for badge in selected.get("changeBadges", []):
			details.text += "\n%s: %s" % [badge.label, ", ".join(badge.get("sources", []))]

func selected_model() -> Dictionary:
	for data in models:
		if str(data.labId) == selected_id: return data.duplicate(true)
	return {}

func remove_selected() -> void:
	if selected_model().is_empty(): return
	models = models.filter(func(data): return str(data.labId) != selected_id)
	count = models.size()
	count_picker.set_value_no_signal(count)
	selected_id = ""
	_render_hand()

func inspect_selected() -> void:
	var data := selected_model()
	if data.is_empty(): return
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new()
	dialog.setup(str(data.name), str(data.summary), data, data)
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()
	dialog.tree_exiting.connect(func(): if selected_id in hand.order: hand.card_for(selected_id).grab_focus())

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("inspect_card"):
		select_card(hand.active_card_id()); inspect_selected(); get_viewport().set_input_as_handled()
	elif event.is_action_pressed("hand_overview"):
		hand.show_overview(); get_viewport().set_input_as_handled()

func _exit_tree() -> void:
	if _prior_preferences.is_empty(): return
	Preferences.text_scale = _prior_preferences.text_scale
	Preferences.high_contrast = _prior_preferences.high_contrast
	Preferences.reduced_motion = _prior_preferences.reduced_motion
	I18n.set_locale(str(_prior_preferences.locale))

func _capture_if_requested() -> void:
	var target := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-preset="):
			var id := argument.trim_prefix("--lab-preset=")
			for i in catalog.presets.size():
				if str(catalog.presets[i].id) == id: set_preset(i)
		elif argument.begins_with("--lab-count="): set_count(int(argument.trim_prefix("--lab-count=")))
		elif argument.begins_with("--lab-scale="): set_text_scale(float(argument.trim_prefix("--lab-scale=")))
		elif argument == "--lab-reading": hand.mode_override = "reading"; layout_picker.select(1); hand.request_layout()
		elif argument.begins_with("--lab-screenshot="): target = argument.trim_prefix("--lab-screenshot=")
	if target.is_empty(): return
	if DisplayServer.get_name() == "headless":
		push_error("Capture requires a rendering display; use the headless test for layout validation.")
		get_tree().quit(2)
		return
	await get_tree().create_timer(.3).timeout
	if "--lab-overview" in OS.get_cmdline_user_args():
		hand.show_overview()
		await get_tree().create_timer(.3).timeout
	if "--lab-tooltip" in OS.get_cmdline_user_args() and not card_controls.is_empty():
		var pointer := InputEventMouseMotion.new()
		pointer.position = card_controls[0].get_global_rect().get_center()
		get_viewport().push_input(pointer, true)
		await get_tree().create_timer(float(ProjectSettings.get_setting("gui/timers/tooltip_delay_sec", .5)) + .3).timeout
	if "--lab-inspect" in OS.get_cmdline_user_args():
		select_card(str(models[0].labId))
		inspect_selected()
		await get_tree().create_timer(.3).timeout
	await RenderingServer.frame_post_draw
	var saved := get_viewport().get_texture().get_image().save_png(target)
	get_tree().quit(0 if saved == OK else 2)
