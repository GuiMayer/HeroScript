extends Control
## Offline visual fixture harness. No gateway, rules, run mutation or preference save.
const HandView = preload("res://scripts/ui/card_hand_view.gd")
const Fixtures = "res://data/labs/card_hand_lab.json"
# Keep the reusable noise warm on rendered lab entry, not at the first click.
var _burn_noise: Texture2D
var catalog: Dictionary = {}
var visual_context
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
var _prior_profile: Resource
var ornaments: Array[Control] = []
var motion_lab
var motion_scenarios: Array = []
var animation_picker
var animation_cycle: SpinBox
var play_animation_button: Button
var apply_animation_button: Button

func _ready() -> void:
	if DisplayServer.get_name() != "headless": _burn_noise = load("res://data/card_burn_noise.tres")
	_prior_profile = AppTheme.profile
	AppTheme.set_profile(preload("res://data/themes/ivory_theme.tres"))
	visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n)
	var appearance = JSON.parse_string(FileAccess.get_file_as_string("res://data/presentation.json"))
	if appearance is Dictionary: visual_context.set_resource_styles(appearance.get("resources", {}))
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
	var trace := preload("res://scripts/ui/components/circuit_trace.gd").new()
	trace.name = "PrintedCircuit"
	trace.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
	trace.position = Vector2(-510, 18)
	trace.size = Vector2(480, 78)
	trace.mirrored = true
	add_child(trace)
	ornaments.append(trace)
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
	var heading := AppTheme.title(I18n.text("IVORY / CARD LAB"), 30)
	heading.set_meta("lab_label", "IVORY / CARD LAB")
	column.add_child(heading)
	var subtitle := AppTheme.muted(I18n.text("Shared game components. Visual samples only — no rules or API calls."), 14, 320)
	subtitle.set_meta("lab_label", "Shared game components. Visual samples only — no rules or API calls.")
	column.add_child(subtitle)
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
	var count_label := AppTheme.title(I18n.text("Cards"), 14)
	count_label.set_meta("lab_label", "Cards")
	tools.add_child(count_label)
	count_picker = SpinBox.new()
	count_picker.name = "HandCount"
	count_picker.min_value = 0
	count_picker.max_value = 100
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
	contrast.set_meta("lab_label", "High contrast")
	contrast.button_pressed = Preferences.high_contrast
	contrast.toggled.connect(func(value): Preferences.high_contrast = value; _refresh())
	tools.add_child(contrast)
	var motion := CheckButton.new()
	motion.text = "Reduced motion"
	motion.set_meta("lab_label", "Reduced motion")
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
	var shader_button := _button("Art effects lab", _open_art_effect_lab)
	shader_button.name = "OpenArtEffectLab"
	shader_button.set_meta("lab_fx_label", "Art effects lab")
	actions.add_child(shader_button)
	column.add_child(actions)
	var workspace := PanelContainer.new()
	workspace.name = "SampleDetails"
	workspace.size_flags_vertical = Control.SIZE_EXPAND_FILL
	workspace.custom_minimum_size.y = 70
	workspace.add_theme_stylebox_override("panel", AppTheme.box(AppTheme.PANEL, AppTheme.profile.corner_radius))
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
	hand = HandView.new(visual_context)
	hand.card_activated.connect(select_card)
	hand.selection_requested.connect(select_card)
	hand.inspection_requested.connect(func(id): select_card(id); inspect_selected())
	hand.card_previewed.connect(func(_id): pass)
	hand.card_hovered.connect(func(_id): if not get_tree().paused: GameAudio.play_event("card_hover"))
	outer.add_child(hand)
	layout_picker = OptionButton.new()
	layout_picker.name = "LabHandLayout"
	layout_picker.add_item(I18n.text("Adaptive fan"))
	layout_picker.add_item(I18n.text("Reading — no overlap"))
	layout_picker.item_selected.connect(func(index): hand.mode_override = "reading" if index == 1 else "adaptive"; hand.request_layout())
	tools.add_child(layout_picker)
	actions.add_child(_button("Add sample", func(): set_count(count + 1)))
	actions.add_child(_button("Enrich selected", enrich_selected))
	var read_only := CheckButton.new()
	read_only.text = "Read-only / busy"
	read_only.set_meta("lab_label", "Read-only / busy")
	read_only.toggled.connect(func(enabled): hand.set_read_only(enabled))
	actions.add_child(read_only)
	motion_scenarios = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_motion_lab.json")).scenarios
	motion_lab = preload("res://scripts/labs/card_motion_lab.gd").new(hand, visual_context,
		func(): return hand.scroll.global_position + Vector2(30, hand.scroll.size.y * .5),
		func(): return hand.scroll.global_position + Vector2(hand.scroll.size.x - 30, hand.scroll.size.y * .5),
		func(): return preload("res://scripts/presentation/card_choreography.gd").stage_center(hand))
	add_child(motion_lab)
	# Keep playback controls reachable when the sample/details area scrolls.
	var animation_controls := HFlowContainer.new()
	animation_controls.name = "AnimationControls"
	animation_controls.add_theme_constant_override("h_separation", 8)
	animation_controls.add_theme_constant_override("v_separation", 8)
	outer.add_child(animation_controls)
	outer.move_child(animation_controls, outer.get_children().find(hand))
	animation_picker = preload("res://scripts/labs/lab_animation_picker.gd").new()
	animation_controls.add_child(animation_picker)
	animation_picker.setup(JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_animation_catalog.json")), motion_scenarios, I18n)
	var cycle_label := AppTheme.title(I18n.text("Cycle duration (s)"), 14)
	cycle_label.set_meta("lab_label", "Cycle duration (s)")
	animation_controls.add_child(cycle_label)
	animation_cycle = SpinBox.new()
	animation_cycle.name = "AnimationCycleSeconds"
	animation_cycle.min_value = 0; animation_cycle.max_value = 30
	animation_cycle.step = .05; animation_cycle.value = 1
	animation_cycle.suffix = " s"
	animation_cycle.custom_minimum_size = Vector2(120, 40)
	animation_cycle.tooltip_text = I18n.text("Total active animation time. Zero is instant; pause and manual steps do not count.")
	animation_controls.add_child(animation_cycle)
	animation_picker.animation_selected.connect(_animation_selected)
	_animation_selected(animation_picker.selected_id)
	var instant := CheckButton.new()
	instant.text = I18n.text("Instant preview"); instant.set_meta("lab_label", "Instant preview")
	instant.toggled.connect(func(value): motion_lab.preset = "instant" if value else "normal")
	animation_controls.add_child(instant)
	var manual := CheckButton.new(); manual.text = I18n.text("Manual animation steps"); manual.set_meta("lab_label", "Manual animation steps")
	manual.toggled.connect(func(value): motion_lab.options.auto_animations = not value); animation_controls.add_child(manual)
	play_animation_button = _button("Play animation", play_selected_animation)
	play_animation_button.name = "PlayAnimationButton"
	animation_controls.add_child(play_animation_button)
	apply_animation_button = _button("Apply animation to combat", apply_animation_to_combat)
	animation_controls.add_child(apply_animation_button)
	apply_animation_button.disabled = not preload("res://scripts/presentation/card_animation_profile.gd").KEYS.has(str(animation_picker.selected_scenario().get("operation", "")))
	animation_controls.add_child(_button("Next", func(): motion_lab.director.advance()))
	animation_controls.add_child(_button("Skip presentation", func(): motion_lab.skip()))
	motion_lab.finished.connect(func(result, metrics):
		models = result; count = models.size(); count_picker.set_value_no_signal(count)
		card_controls = hand.ordered_cards()
		status.text = "Motion: %.1f ms · %s instant facts · %s CPU yields · %s" % [metrics.elapsed_ms, metrics.instant, metrics.cpu_yields, metrics.result])

func play_selected_animation() -> void:
	var scenario: Dictionary = animation_picker.selected_scenario()
	if scenario.is_empty(): return
	if scenario.operation == "burn" and models.is_empty(): return
	scenario["cycleSeconds"] = animation_cycle.value
	if not models.is_empty():
		scenario["cardInstanceId"] = selected_id if selected_id in hand.order else str(models[0].cardInstanceId)
	motion_lab.play(scenario, models)

func _open_art_effect_lab() -> void:
	var old = get_node_or_null("ArtEffectLab")
	if old != null: old.queue_free(); remove_child(old)
	var panel = preload("res://scripts/labs/art_effect_lab.gd").new()
	var card_model := selected_model()
	if card_model.is_empty() and not models.is_empty(): card_model = models[0]
	panel.setup(visual_context, card_model)
	add_child(panel)
	panel.popup_centered_clamped(Vector2i(900,540),.9)

func _animation_selected(_id: String) -> void:
	var operation := str(animation_picker.selected_scenario().get("operation", ""))
	var keys: Array = preload("res://scripts/presentation/card_animation_profile.gd").KEYS.get(operation, [])
	if apply_animation_button != null: apply_animation_button.disabled = keys.is_empty()
	if animation_cycle == null or keys.is_empty(): return
	var policy = visual_context.capture_motion()
	# Display the configured time even when accessibility currently skips motion.
	# Never mutate the captured policy merely to populate a numeric field.
	var seconds := 0.0
	for key in keys: seconds += float(policy._values[str(key)]) / policy.speed
	animation_cycle.set_value_no_signal(seconds)

func apply_animation_to_combat() -> void:
	var scenario: Dictionary = animation_picker.selected_scenario()
	var policy = visual_context.capture_motion()
	var result: Error = preload("res://scripts/presentation/card_animation_profile.gd").save(str(scenario.get("operation", "")), animation_cycle.value * policy.speed, str(scenario.get("direction", "center")))
	status.text = I18n.text("Animation applied to combat. Active animations keep their captured settings.") if result == OK else I18n.text("Could not save the animation profile.")

func enrich_selected() -> void:
	for data in models:
		if str(data.labId) == selected_id:
			data["previewNote"] = "Enriched visual sample — no API call"
	_render_hand()

func _button(label: String, action: Callable) -> Button:
	var button := AppTheme.button(label)
	button.custom_minimum_size.y = 40
	button.pressed.connect(action)
	button.set_meta("lab_label", label)
	return button

func set_preset(index: int) -> void:
	preset_index = clampi(index, 0, catalog.presets.size() - 1)
	preset_picker.select(preset_index)
	_rotation = 0
	set_count(catalog.presets[preset_index].cards.size())

func set_count(value: int) -> void:
	if motion_lab != null and motion_lab._running: motion_lab.skip()
	count = clampi(value, 0, 100)
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
	if motion_lab != null and motion_lab._running: motion_lab.skip()
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	AppTheme.configure_preferences(Preferences.text_scale, Preferences.high_contrast)
	theme = AppTheme.build(Preferences.high_contrast)
	_translate_controls(self)
	animation_picker.refresh_labels()
	animation_cycle.tooltip_text = I18n.text("Total active animation time. Zero is instant; pause and manual steps do not count.")
	for i in 3: scale_picker.set_item_text(i, I18n.text("Text %s%%") % [90, 100, 120][i])
	layout_picker.set_item_text(0, I18n.text("Adaptive fan"))
	layout_picker.set_item_text(1, I18n.text("Reading — no overlap"))
	for ornament in ornaments: ornament.simplified = Preferences.high_contrast
	models.clear()
	var entries: Array = catalog.presets[preset_index].cards
	for i in count:
		var fixture_id: String = entries[(i + _rotation) % entries.size()]
		var data: Dictionary = catalog.cards[fixture_id].duplicate(true)
		data["rarityId"] = str(data.get("rarity", "")).to_lower()
		if I18n.locale == "pt_BR": data.merge(data.get("pt_BR", {}), true)
		data.erase("pt_BR")
		data["labId"] = "%s:%s" % [fixture_id, i]
		data["cardInstanceId"] = data.labId
		data["summary"] = "\n".join(data.get("effects", []))
		models.append(data)
	_render_hand()

func _translate_controls(node: Node) -> void:
	if node.has_meta("lab_label"):
		var key := str(node.get_meta("lab_label"))
		node.text = I18n.text(key)
		if node is CheckButton:
			if key == "High contrast": node.set_pressed_no_signal(Preferences.high_contrast)
			elif key == "Reduced motion": node.set_pressed_no_signal(Preferences.reduced_motion)
	if node.has_meta("lab_fx_label"): node.text = preload("res://scripts/labs/art_effect_lab.gd").label_for(I18n.locale,str(node.get_meta("lab_fx_label")))
	for child in node.get_children(): _translate_controls(child)

func _render_hand() -> void:
	hand.sync_models(models, "lab:" + str(preset_index), I18n.text("Your hand is empty."))
	card_controls = hand.ordered_cards()
	AppTheme.apply_view_preferences(self)
	select_card(selected_id)
	preload("res://scripts/ui/focus_navigation.gd").queue(self)

func select_card(id: String) -> void:
	selected_id = id
	hand.set_selection(id)
	var selected := selected_model()
	status.text = I18n.text("%s cards · %s · %s%% text · visual samples only") % [models.size(), I18n.locale, roundi(Preferences.text_scale * 100)]
	details.text = I18n.text("Select a sample to read its full text. Hover for a summary; Inspect selected opens persistent details. No effects are executed here.")
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
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new(visual_context)
	dialog.setup(str(data.name), str(data.summary), data, data)
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()
	dialog.tree_exiting.connect(func(): if selected_id in hand.order: hand.card_for(selected_id).grab_focus())

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("inspect_card"):
		select_card(hand.active_card_id()); inspect_selected(); get_viewport().set_input_as_handled()

func _exit_tree() -> void:
	if _prior_preferences.is_empty(): return
	Preferences.text_scale = _prior_preferences.text_scale
	Preferences.high_contrast = _prior_preferences.high_contrast
	Preferences.reduced_motion = _prior_preferences.reduced_motion
	I18n.set_locale(str(_prior_preferences.locale))
	AppTheme.set_profile(_prior_profile)
	AppTheme.configure_preferences(Preferences.text_scale, Preferences.high_contrast)

func _capture_if_requested() -> void:
	var target := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-preset="):
			var id := argument.trim_prefix("--lab-preset=")
			for i in catalog.presets.size():
				if str(catalog.presets[i].id) == id: set_preset(i)
		elif argument.begins_with("--lab-count="): set_count(int(argument.trim_prefix("--lab-count=")))
		elif argument.begins_with("--lab-scale="): set_text_scale(float(argument.trim_prefix("--lab-scale=")))
		elif argument.begins_with("--lab-locale="): set_language(argument.trim_prefix("--lab-locale="))
		elif argument.begins_with("--lab-resolution="):
			var parts := argument.trim_prefix("--lab-resolution=").split("x")
			if parts.size() == 2: get_window().size = Vector2i(int(parts[0]), int(parts[1]))
		elif argument == "--lab-high-contrast": Preferences.high_contrast = true; _refresh()
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
