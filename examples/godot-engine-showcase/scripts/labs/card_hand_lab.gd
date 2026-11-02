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
	var lab_profile = preload("res://scripts/ui/styles/programmer_art_profile.gd").create(preload("res://data/themes/ivory_theme.tres"))
	lab_profile.art_manifest = "res://data/programmer_art_manifest.json"
	AppTheme.set_profile(lab_profile)
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
	hand = get_node("Margin/Outer/Hand")
	details = get_node("Margin/Outer/UpperScroll/Column/SampleDetails/FullSampleText")
	status = get_node("Margin/Outer/UpperScroll/Column/LabStatus")
	preset_picker = get_node("Margin/Outer/UpperScroll/Column/Tools/FixturePreset")
	count_picker = get_node("Margin/Outer/UpperScroll/Column/Tools/HandCount")
	language_picker = get_node("Margin/Outer/UpperScroll/Column/Tools/LanguagePicker")
	scale_picker = get_node("Margin/Outer/UpperScroll/Column/Tools/ScalePicker")
	layout_picker = get_node("Margin/Outer/UpperScroll/Column/Tools/LabHandLayout")
	animation_picker = get_node("Margin/Outer/AnimationControls/LabAnimationPicker")
	animation_cycle = get_node("Margin/Outer/AnimationControls/AnimationCycleSeconds")
	play_animation_button = get_node("Margin/Outer/AnimationControls/PlayAnimationButton")
	apply_animation_button = get_node("Margin/Outer/AnimationControls/ApplyAnimationButton")
	get_node("Background").color = AppTheme.NIGHT
	ornaments.append(get_node("PrintedCircuit"))
	hand.mount(visual_context)
	preset_picker.clear()
	for preset in catalog.presets: preset_picker.add_item(str(preset.name))
	preset_picker.item_selected.connect(set_preset)
	count_picker.set_value_no_signal(count)
	count_picker.value_changed.connect(func(value): set_count(int(value)))
	language_picker.select(1 if I18n.locale == "pt_BR" else 0)
	language_picker.item_selected.connect(func(index): set_language("pt_BR" if index == 1 else "en"))
	scale_picker.select(2 if Preferences.text_scale > 1.1 else (0 if Preferences.text_scale < 1.0 else 1))
	scale_picker.item_selected.connect(func(index): set_text_scale([.9, 1.0, 1.2][index]))
	get_node("Margin/Outer/UpperScroll/Column/Tools/Contrast").set_pressed_no_signal(Preferences.high_contrast)
	get_node("Margin/Outer/UpperScroll/Column/Tools/Contrast").toggled.connect(func(value): Preferences.high_contrast = value; _refresh())
	get_node("Margin/Outer/UpperScroll/Column/Tools/Motion").set_pressed_no_signal(Preferences.reduced_motion)
	get_node("Margin/Outer/UpperScroll/Column/Tools/Motion").toggled.connect(func(value): Preferences.reduced_motion = value; _refresh())
	get_node("Margin/Outer/UpperScroll/Column/Actions/Empty").pressed.connect(func(): set_count(0))
	get_node("Margin/Outer/UpperScroll/Column/Actions/Restore").pressed.connect(func(): set_preset(preset_index))
	get_node("Margin/Outer/UpperScroll/Column/Actions/Rotate").pressed.connect(func(): _rotation += 1; _refresh())
	get_node("Margin/Outer/UpperScroll/Column/Actions/Remove").pressed.connect(remove_selected)
	get_node("Margin/Outer/UpperScroll/Column/Actions/Inspect").pressed.connect(inspect_selected)
	get_node("Margin/Outer/UpperScroll/Column/Actions/OpenArtEffectLab").pressed.connect(_open_art_effect_lab)
	get_node("Margin/Outer/UpperScroll/Column/Actions/Add").pressed.connect(func(): set_count(count + 1))
	get_node("Margin/Outer/UpperScroll/Column/Actions/Enrich").pressed.connect(enrich_selected)
	hand.card_activated.connect(select_card)
	hand.selection_requested.connect(select_card)
	hand.inspection_requested.connect(func(id): select_card(id); inspect_selected())
	hand.card_hover_entered.connect(func(_id): if not get_tree().paused: GameAudio.play_event("card_hover"))
	layout_picker.item_selected.connect(func(index): hand.mode_override = "reading" if index == 1 else "adaptive"; hand.request_layout())
	get_node("Margin/Outer/UpperScroll/Column/Actions/ReadOnly").toggled.connect(func(enabled): hand.set_read_only(enabled))
	motion_scenarios = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_motion_lab.json")).scenarios
	motion_lab = preload("res://scripts/labs/card_motion_lab.gd").new(hand, visual_context,
		func(): return hand.scroll.global_position + Vector2(30, hand.scroll.size.y * .5),
		func(): return hand.scroll.global_position + Vector2(hand.scroll.size.x - 30, hand.scroll.size.y * .5),
		func(): return preload("res://scripts/presentation/card_choreography.gd").stage_center(hand))
	add_child(motion_lab)
	animation_picker.setup(JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_animation_catalog.json")), motion_scenarios, I18n)
	animation_picker.animation_selected.connect(_animation_selected)
	_animation_selected(animation_picker.selected_id)
	get_node("Margin/Outer/AnimationControls/Instant").toggled.connect(func(value): motion_lab.preset = "instant" if value else "normal")
	get_node("Margin/Outer/AnimationControls/Manual").toggled.connect(func(value): motion_lab.options.auto_animations = not value)
	play_animation_button.pressed.connect(play_selected_animation)
	apply_animation_button.pressed.connect(apply_animation_to_combat)
	get_node("Margin/Outer/AnimationControls/Next").pressed.connect(func(): motion_lab.director.advance())
	get_node("Margin/Outer/AnimationControls/Skip").pressed.connect(func(): motion_lab.skip())
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
	var panel = preload("res://scripts/labs/art_effect_lab.gd").create()
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
	var result: Error = preload("res://scripts/presentation/card_animation_profile.gd").save(str(scenario.get("operation", "")), animation_cycle.value * policy.speed, str(scenario.get("direction", "center")), str(scenario.get("timing", "")))
	status.text = I18n.text("Animation applied to combat. Active animations keep their captured settings.") if result == OK else I18n.text("Could not save the animation profile.")

func enrich_selected() -> void:
	for data in models:
		if str(data.labId) == selected_id:
			data["previewNote"] = "Enriched visual sample — no API call"
	_render_hand()

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
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").create(visual_context)
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
