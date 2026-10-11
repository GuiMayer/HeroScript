extends PanelContainer
signal collapse_requested
var content_scroll: ScrollContainer
var title_label: Label
var toggle_button: Button
var translator
var presentation: Dictionary
var _run: Dictionary
var _combat: Dictionary
var _actors: Array
var _zones
var _input_actor: String
var _setting: Dictionary
var _setting_id: String
var _projection := {}
func apply_model(model: Dictionary, i18n) -> void:
	var projection := model.duplicate(true)
	projection["locale"] = i18n.locale
	if _projection == projection: return
	_projection = projection
	if get_child_count() > 0:
		var desired = get_script().new()
		desired.apply_model(model, i18n)
		preload("res://scripts/ui/view_reconciler.gd").patch(self, desired)
		desired.free()
		return
	translator = i18n
	presentation = model.presentation.duplicate(true)
	_run = model.run.duplicate(true); _combat = model.combat.duplicate(true)
	_actors = model.actors.duplicate(true); _input_actor = str(model.input_actor)
	_setting = model.setting.duplicate(true); _setting_id = str(model.setting_id)
	_zones = preload("res://scripts/presentation/card_zone_presenter.gd").new(model.zones, translator)
	name = "CharacterSidebar"
	mouse_filter = Control.MOUSE_FILTER_STOP
	add_theme_stylebox_override("panel", AppTheme.box(AppTheme.PANEL, 12, AppTheme.MUTED.darkened(.4), 1))
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 8)
	add_child(column)
	var toolbar := HBoxContainer.new()
	title_label = AppTheme.title(translator.text("CHARACTER"), 16, AppTheme.GOLD)
	title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	toolbar.add_child(title_label)
	toggle_button = AppTheme.button("›", 36)
	toggle_button.name = "CharacterSidebarToggle"
	toggle_button.custom_minimum_size = Vector2(36, 36)
	toggle_button.pressed.connect(func(): collapse_requested.emit())
	toolbar.add_child(toggle_button)
	column.add_child(toolbar)
	content_scroll = ScrollContainer.new()
	content_scroll.name = "CharacterSidebarContent"
	content_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var details := VBoxContainer.new()
	details.name = "CharacterSidebarDetails"
	details.custom_minimum_size.x = 272
	details.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	details.add_theme_constant_override("separation", 7)
	_populate_character_sidebar(details)
	content_scroll.add_child(details)
	column.add_child(content_scroll)

func _populate_character_sidebar(details: VBoxContainer) -> void:
	var actor := _player_actor()
	if actor.is_empty():
		details.add_child(AppTheme.muted(translator.text("Character data is unavailable."), 13, 260))
		return
	var definition_id := str(actor.get("definitionId", ""))
	var actor_name: String = translator.content_name(definition_id, str(actor.get("name", actor.get("instanceId", ""))))
	var name_label := AppTheme.title(actor_name, 20)
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.custom_minimum_size.x = 260
	details.add_child(name_label)
	if not definition_id.is_empty():
		details.add_child(AppTheme.caption(definition_id, 11, 260))
	_add_sidebar_section(details, translator.text("Resources"))
	var resources: Dictionary = actor.get("resources", {}) if actor.get("resources", {}) is Dictionary else {}
	var resource_projection: Dictionary = _run.get("characterResources", {})
	if str(resource_projection.get("ownerId", "")) == str(actor.get("instanceId", "")) and str(resource_projection.get("authority", "")) == "ActiveEncounter":
		resources = resource_projection.get("resources", {}).duplicate(true)
	var ordered_resources: Array = presentation.get("resource_order", []).duplicate()
	for resource_id in resources:
		if resource_id not in ordered_resources:
			ordered_resources.append(resource_id)
	for resource_id in ordered_resources:
		if not resources.has(resource_id) or not (resources[resource_id] is Dictionary):
			continue
		var resource_view := preload("res://scripts/ui/resource_view.gd").new()
		resource_view.name = "CharacterResource_" + str(resource_id).validate_node_name()
		resource_view.set_meta("resource_id", str(resource_id))
		resource_view.custom_minimum_size.x = 260
		resource_view.setup(str(resource_id), resources[resource_id], presentation.get("resources", {}).get(resource_id, {}))
		details.add_child(resource_view)
	_add_sidebar_section(details, translator.text("Statuses"))
	var statuses: Array = actor.get("statuses", []).filter(func(status): return status is Dictionary and bool(status.get("isActive", true)))
	if statuses.is_empty():
		details.add_child(AppTheme.muted(translator.text("No active statuses"), 12, 260))
	else:
		for status in statuses:
			var status_text := "%s ×%s" % [translator.content_name(str(status.get("statusId", "status"))), int(status.get("stacks", 1))]
			var duration := int(status.get("duration", -1))
			if duration >= 0:
				status_text += "  ·  " + translator.text("Duration: %s") % duration
			details.add_child(_sidebar_item(status_text, AppTheme.EMBER))
	_add_sidebar_section(details, translator.text("Relics"))
	var relics: Array = _run.get("relics", []) if _run.get("relics", []) is Array else []
	if relics.is_empty():
		details.add_child(AppTheme.muted(translator.text("No relics"), 12, 260))
	else:
		for relic in relics:
			details.add_child(_sidebar_item(_owned_content_label(relic, ["definitionId", "relicId", "id"]), AppTheme.GOLD))
	var modifiers: Array = _run.get("modifiers", []) if _run.get("modifiers", []) is Array else []
	if not modifiers.is_empty():
		_add_sidebar_section(details, translator.text("Modifiers"))
		for modifier in modifiers:
			details.add_child(_sidebar_item(_owned_content_label(modifier, ["modifierId", "definitionId", "id"]), AppTheme.TEAL))
	_add_sidebar_section(details, translator.text("RUN INFO"))
	var activation: Dictionary = _combat.get("activation", {}) if _combat.get("activation", {}) is Dictionary else {}
	_add_sidebar_value(details, translator.text("Round"), str(int(activation.get("round", _combat.get("currentTurn", 1)))))
	_add_sidebar_value(details, translator.text("Deck"), translator.text("%s cards") % _zones.total_cards())
	_add_sidebar_value(details, translator.text("Playable"), str(_zones.playable_cards().size()))
	var setting := _setting
	_add_sidebar_value(details, translator.text("Setting"), translator.text(str(setting.get("displayName", _setting_id))))

func _player_actor() -> Dictionary:
	var actors: Array = _actors.duplicate(true)
	for actor in actors:
		var binding: Dictionary = actor.get("controllerBinding", {}) if actor.get("controllerBinding", {}) is Dictionary else {}
		if str(binding.get("kind", "")).to_lower() == "player":
			return actor
	var input_actor_id := _input_actor
	for actor in actors:
		if str(actor.get("instanceId", "")) == input_actor_id:
			return actor
	return actors[0] if not actors.is_empty() else {}

func _add_sidebar_section(parent: VBoxContainer, text: String) -> void:
	var title := AppTheme.title(text.to_upper(), 12, AppTheme.GOLD)
	title.custom_minimum_size = Vector2(260, 24)
	title.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	parent.add_child(title)

func _sidebar_item(text: String, color: Color) -> Label:
	var label := AppTheme.muted(text, 12, 260)
	label.add_theme_color_override("font_color", color)
	label.tooltip_text = text
	return label

func _owned_content_label(item, id_keys: Array) -> String:
	if not (item is Dictionary):
		return translator.content_name(str(item))
	var id := ""
	for key in id_keys:
		if item.has(key) and not str(item[key]).is_empty():
			id = str(item[key])
			break
	var result: String = translator.content_name(id, id)
	var stacks := int(item.get("stacks", 1))
	if stacks > 1:
		result += " ×%s" % stacks
	return result

func _add_sidebar_value(parent: VBoxContainer, label_text: String, value_text: String) -> void:
	var row := HBoxContainer.new()
	row.custom_minimum_size.x = 260
	var label := AppTheme.muted(label_text, 12, 112)
	label.autowrap_mode = TextServer.AUTOWRAP_OFF
	row.add_child(label)
	var value := AppTheme.caption(value_text, 12, 140)
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	value.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	value.tooltip_text = value_text
	row.add_child(value)
	parent.add_child(row)
