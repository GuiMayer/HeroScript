@tool
class_name MainMenuView
extends Control
## The scene owns chrome; the router owns actions, settings scope and resume.
signal action_requested(action: String)
signal setting_requested(index: int)
@export var use_profile_metrics := true
@export var visual_style: Resource = preload("res://data/themes/ivory_menu_style.tres")
@export var preview_model: Dictionary = {"settingName":"Ember Archive","description":"Cards, enemies, resources, map and flow live in JSON.","settings":[],"selectedSettingId":"","available":false,"pending":false,"connecting":false}
var visual_context
var setting_selector: OptionButton
var continue_button: Button
var _scroll: ScrollContainer
var _body: BoxContainer
var _hero: VBoxContainer
var _navigation: VBoxContainer
var model: Dictionary = {}
var _settings_key := ""
func _ready() -> void:
	_bind_nodes()
	if visual_context == null and (Engine.is_editor_hint() or get_parent() == get_tree().root):
		mount(preload("res://scripts/ui/visual_context.gd").new(null,{},preload("res://data/themes/ivory_theme.tres")))
		apply_model(preview_model)
func _bind_nodes() -> void:
	setting_selector = get_node("%SettingSelector"); continue_button = get_node("%ContinueButton")
	_scroll = get_node("%MenuScroll"); _body = get_node("%MenuBody")
	_hero = get_node("%JourneyColumn"); _navigation = get_node("%NavigationColumn")
	if not resized.is_connected(_layout): resized.connect(_layout)
	if not _scroll.resized.is_connected(_layout): _scroll.resized.connect(_layout)
	if not setting_selector.item_selected.is_connected(setting_requested.emit): setting_selector.item_selected.connect(setting_requested.emit)
	for button in find_children("*","Button",true,false):
		if not button.has_meta("action"): continue
		var callback := _action.bind(str(button.get_meta("action")))
		if not button.pressed.is_connected(callback): button.pressed.connect(callback)
func _action(action: String) -> void: action_requested.emit(action)
func mount(context) -> void:
	_bind_nodes(); visual_context = context; theme = context.theme
func apply_model(data: Dictionary) -> void:
	_bind_nodes(); model = data.duplicate(true)
	var i18n = visual_context.translator
	for control in find_children("*","Control",true,false):
		if control.has_meta("translation_key"): control.text = i18n.text(str(control.get_meta("translation_key")))
		if control is Button and control.has_meta("action"):
			if use_profile_metrics and visual_style != null:
				control.custom_minimum_size.y = visual_style.link_height if control.get_meta("menu_link",false) else visual_style.action_height
			control.tooltip_text = control.text
	var status = get_node("%EngineStatus")
	var phase_keys := {"preparing_data": "●  PREPARING DATA", "checking_build": "●  CHECKING BUILD", "building_server": "●  BUILDING SERVER",
		"starting_server": "●  STARTING SERVER", "loading_settings": "●  LOADING SETTINGS",
		"connecting": "●  CONNECTING"}
	var status_key: String = phase_keys.get(str(model.get("serverPhase", "connecting")), "●  CONNECTING") if model.get("connecting", false) else ("●  ENGINE ONLINE" if model.available else "●  ENGINE OFFLINE")
	status.text = i18n.text(status_key)
	status.add_theme_color_override("font_color",visual_context.styles.TEAL if model.available else visual_context.styles.GOLD)
	status.set_meta("text_role","accent.positive" if model.available else "accent.primary")
	var title = get_node("%SettingTitle")
	title.text = str(model.settingName).to_upper(); title.tooltip_text = str(model.settingName)
	if use_profile_metrics and visual_style != null:
		title.add_theme_font_size_override("font_size",visual_style.title_size)
		if title.has_meta("base_font_size"): title.remove_meta("base_font_size")
		if title.has_meta("visual_preferences_revision"): title.remove_meta("visual_preferences_revision")
	get_node("%SettingDescription").text = str(model.description)
	var key := JSON.stringify(model.settings.map(func(item): return item.get("settingId","")))
	if key != _settings_key:
		setting_selector.get_popup().hide()
		setting_selector.clear()
		for entry in model.settings: setting_selector.add_item("")
		_settings_key = key
	for index in model.settings.size():
		var setting: Dictionary = model.settings[index]
		setting_selector.set_item_text(index,i18n.text(str(setting.get("displayName",setting.get("settingId","Setting")))))
		setting_selector.set_item_metadata(index,str(setting.get("settingId","")))
		if str(setting.get("settingId","")) == str(model.selectedSettingId): setting_selector.select(index)
	setting_selector.disabled = not model.available or setting_selector.item_count < 2
	get_node("%NewJourneyButton").disabled = bool(model.get("connecting",false))
	continue_button.disabled = true
	continue_button.tooltip_text = i18n.text("Continue the selected setting's journey.")
	get_node("%ReconnectButton").visible = model.pending or not model.available
	get_node("%ReconnectButton").disabled = bool(model.get("connecting",false))
	get_node("%HistoryButton").disabled = not model.available
	apply_visual_profile(visual_context.profile)
	_layout.call_deferred()

func _layout() -> void:
	if not is_instance_valid(_scroll) or not use_profile_metrics or visual_style == null: return
	var width := maxf(visual_style.minimum_scroll_width, _scroll.size.x - visual_style.scroll_padding)
	var compact: bool = width < visual_style.compact_width
	_body.vertical = compact
	_body.custom_minimum_size.x = minf(width, visual_style.content_width)
	_body.add_theme_constant_override("separation", visual_style.compact_gap if compact else visual_style.column_gap)
	_hero.custom_minimum_size.x = minf(visual_style.hero_min_width, width) if compact else visual_style.hero_min_width
	_navigation.custom_minimum_size.x = minf(visual_style.navigation_width, width)

func apply_visual_profile(_profile) -> void:
	theme = visual_context.theme
	for label in find_children("*","Label",true,false):
		var role := str(label.get_meta("text_role",""))
		if not role.is_empty(): label.add_theme_color_override("font_color",visual_context.styles.color(role))
	if not Engine.is_editor_hint(): visual_context.styles.apply_view_preferences(self)
	for divider in find_children("*", "HSeparator", true, false):
		var line: StyleBoxLine = divider.get_theme_stylebox("separator").duplicate()
		line.color = visual_context.profile.color("stroke.control")
		divider.add_theme_stylebox_override("separator", line)
