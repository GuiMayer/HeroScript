extends Control
## Passive menu composition. The router owns all actions, setting scope and resume.
signal action_requested(action: String)
signal setting_requested(index: int)
const Circuit = preload("res://scripts/ui/components/circuit_trace.gd")
@export var visual_style: Resource = preload("res://data/themes/ivory_menu_style.tres")
var visual_context
var setting_selector: OptionButton
var continue_button: Button
var _scroll: ScrollContainer
var _body: BoxContainer
var _hero: VBoxContainer
var _navigation: VBoxContainer
var _traces: Array[Control] = []
var model: Dictionary = {}

func mount(context) -> void:
	visual_context = context
	name = "MainMenuView"
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	resized.connect(_layout)

func apply_model(data: Dictionary) -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	_traces.clear()
	model = data.duplicate(true)
	var i18n = visual_context.translator
	var column := VBoxContainer.new()
	column.name = "MenuComposition"
	column.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	column.add_theme_constant_override("separation", 24)
	add_child(column)
	var masthead := HBoxContainer.new()
	masthead.custom_minimum_size.y = 86
	var brand := AppTheme.title("HEROSCRIPT", 18)
	brand.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	brand.theme_type_variation = "SectionTitle"
	masthead.add_child(brand)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	masthead.add_child(push)
	var status_key := "●  CONNECTING" if model.get("connecting", false) else ("●  ENGINE ONLINE" if model.available else "●  ENGINE OFFLINE")
	var status := AppTheme.title(i18n.text(status_key), 13,
		AppTheme.TEAL if model.available else AppTheme.GOLD)
	status.name = "EngineStatus"
	status.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	masthead.add_child(status)
	column.add_child(masthead)
	_scroll = ScrollContainer.new()
	_scroll.name = "MenuScroll"
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_scroll.follow_focus = true
	column.add_child(_scroll)
	var center := CenterContainer.new()
	center.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	center.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_scroll.add_child(center)
	_body = BoxContainer.new()
	_body.name = "MenuBody"
	_body.add_theme_constant_override("separation", visual_style.column_gap)
	center.add_child(_body)
	_hero = VBoxContainer.new()
	_hero.name = "JourneyColumn"
	_hero.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_hero.add_theme_constant_override("separation", 14)
	_body.add_child(_hero)
	_hero.add_child(AppTheme.title(i18n.text("A DATA-DRIVEN SHOWCASE"), 13, AppTheme.EMBER))
	var title := AppTheme.title(str(model.settingName).to_upper(), visual_style.title_size)
	title.name = "SettingTitle"
	title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	title.max_lines_visible = 2
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	title.tooltip_text = str(model.settingName)
	_hero.add_child(title)
	var description := AppTheme.muted(str(model.description), 17)
	description.name = "SettingDescription"
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_hero.add_child(description)
	_hero.add_child(_divider())
	_hero.add_child(AppTheme.title(i18n.text("GAME SETTING"), 13))
	setting_selector = OptionButton.new()
	setting_selector.name = "SettingSelector"
	setting_selector.custom_minimum_size.y = 46
	setting_selector.fit_to_longest_item = false
	setting_selector.clip_text = true
	setting_selector.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	for setting in model.settings:
		setting_selector.add_item(i18n.text(str(setting.get("displayName", setting.get("settingId", "Setting")))))
		setting_selector.set_item_metadata(setting_selector.item_count - 1, str(setting.get("settingId", "")))
		if str(setting.get("settingId", "")) == str(model.selectedSettingId): setting_selector.select(setting_selector.item_count - 1)
	setting_selector.disabled = not model.available or setting_selector.item_count < 2
	setting_selector.item_selected.connect(setting_requested.emit)
	_hero.add_child(setting_selector)
	var setting_hint := AppTheme.muted(i18n.text("A setting swaps the complete rules and content package used by a new journey."), 13)
	setting_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_hero.add_child(setting_hint)
	var launch := HBoxContainer.new()
	launch.add_theme_constant_override("separation", 12)
	var start := _button("NEW JOURNEY", "new", "NewJourneyButton")
	start.disabled = bool(model.get("connecting", false))
	start.theme_type_variation = "PrimaryAction"
	start.set_meta("initial_focus", true)
	start.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	launch.add_child(start)
	continue_button = _button("CONTINUE", "continue", "ContinueButton")
	continue_button.disabled = true
	continue_button.tooltip_text = i18n.text("Continue the selected setting's journey.")
	continue_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	launch.add_child(continue_button)
	_hero.add_child(launch)
	if model.pending or not model.available:
		var reconnect := _button("RECONNECT", "reconnect", "ReconnectButton")
		reconnect.disabled = bool(model.get("connecting", false))
		_hero.add_child(reconnect)
	_navigation = VBoxContainer.new()
	_navigation.name = "NavigationColumn"
	_navigation.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_navigation.add_theme_constant_override("separation", 8)
	_body.add_child(_navigation)
	_navigation.add_child(AppTheme.title(i18n.text("MAIN MENU"), 13, AppTheme.MUTED))
	_navigation.add_child(_divider())
	var history := _button("JOURNEY HISTORY", "history", "HistoryButton", true)
	history.disabled = not model.available
	_navigation.add_child(history)
	_navigation.add_child(_button("RULES LAB", "sandbox", "RulesLabButton", true))
	_navigation.add_child(_button("LOADOUT LAB", "loadout_lab", "LoadoutLabButton", true))
	_navigation.add_child(_button("CONTENT CODEX", "codex", "CodexButton", true))
	_navigation.add_child(_divider())
	_navigation.add_child(_button("SETTINGS", "settings", "SettingsButton", true))
	_navigation.add_child(_button("QUIT", "quit", "QuitButton", true))
	var footer := HBoxContainer.new()
	footer.custom_minimum_size.y = 90
	var footer_push := Control.new()
	footer_push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	footer.add_child(footer_push)
	var signature := AppTheme.muted(i18n.text("HeroScript decides  •  REST connects  •  Godot presents"), 13)
	signature.autowrap_mode = TextServer.AUTOWRAP_OFF
	signature.size_flags_vertical = Control.SIZE_SHRINK_END
	signature.name = "MenuSignature"
	footer.add_child(signature)
	column.add_child(footer)
	for index in 2:
		var trace = Circuit.new()
		trace.name = "MenuCircuit%s" % index
		trace.mirrored = index == 0
		_traces.append(trace)
		add_child(trace)
	apply_visual_profile(visual_context.profile)
	_scroll.resized.connect(_layout)
	_layout.call_deferred()

func _button(key: String, action: String, node_name: String, link := false) -> Button:
	var button := AppTheme.button(visual_context.translator.text(key))
	button.name = node_name
	button.custom_minimum_size.y = visual_style.link_height if link else visual_style.action_height
	if link:
		button.theme_type_variation = "MenuLink"
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
	button.tooltip_text = button.text
	button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	button.pressed.connect(func(): action_requested.emit(action))
	return button

func _divider() -> HSeparator:
	var divider := HSeparator.new()
	var line := StyleBoxLine.new()
	line.color = AppTheme.color("stroke.control")
	line.thickness = 1
	divider.add_theme_stylebox_override("separator", line)
	return divider

func _layout() -> void:
	if not is_instance_valid(_scroll): return
	var width := maxf(240, _scroll.size.x - 16)
	var compact: bool = width < visual_style.compact_width
	_body.vertical = compact
	_body.custom_minimum_size.x = minf(width, visual_style.content_width)
	_body.add_theme_constant_override("separation", 28 if compact else visual_style.column_gap)
	_hero.custom_minimum_size.x = minf(visual_style.hero_min_width, width) if compact else visual_style.hero_min_width
	_navigation.custom_minimum_size.x = minf(visual_style.navigation_width, width)
	for index in _traces.size():
		var trace := _traces[index]
		trace.visible = size.x >= visual_style.compact_width
		trace.size = Vector2(minf(visual_style.circuit_width, size.x * .45), 80)
		trace.position = Vector2(size.x - trace.size.x, 30) if index == 0 else Vector2(0, size.y - 82)

func apply_visual_profile(_profile) -> void:
	for divider in find_children("*", "HSeparator", true, false):
		var line: StyleBoxLine = divider.get_theme_stylebox("separator").duplicate()
		line.color = visual_context.profile.color("stroke.control")
		divider.add_theme_stylebox_override("separator", line)
	for trace in _traces:
		trace.ink = visual_context.profile.color("text.primary")
		trace.terminal = visual_context.profile.color("accent.primary")
		trace.simplified = visual_context.high_contrast
		trace.queue_redraw()
