extends VBoxContainer
## Passive reusable view. Emits intents; never equips, validates rules or calculates attributes.
signal item_requested(id: String)
signal slot_requested(id: String)
signal drop_requested(item: String, slot: String, source: String)
@export var empty_slot_color := Color("d3ddd5")
@export var empty_slot_border := Color("718278")
signal equip_requested
signal remove_requested
signal clear_requested
signal restore_requested
signal preset_requested(index: int)
signal search_requested(value: String)
signal back_requested
var visual_context
var _model: Dictionary = {}
var _inventory := {}
var _positions := {}
var _choices_built := false
var columns := BoxContainer.new()
var inventory_grid := GridContainer.new()
var inventory_scroll := ScrollContainer.new()
var slot_grid := GridContainer.new()
var preset_picker := OptionButton.new()
var search_field := LineEdit.new()
var capacity_label: Label
var capacity_meter := ProgressBar.new()
var warning: Label
var details: RichTextLabel
var attributes_view
var equip_button: Button
var remove_button: Button
var _inventory_panel: PanelContainer

func mount(context) -> void:
	visual_context = context
	name = "LoadoutView"
	theme = context.theme
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_theme_constant_override("separation", 14)
	_build()
	resized.connect(_responsive)
	_responsive.call_deferred()

func _text(key: String, font_size := 16, role := "text.primary") -> Label:
	var label := Label.new()
	label.text = visual_context.translator.text(key)
	label.add_theme_font_size_override("font_size", roundi(font_size * visual_context.text_scale))
	label.add_theme_color_override("font_color", visual_context.styles.color(role))
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return label
func _button(key: String, node_name: String, callback: Callable) -> Button:
	var button: Button = visual_context.styles.button(visual_context.translator.text(key))
	button.name = node_name
	button.pressed.connect(callback)
	return button
func _panel(title: String, content: Control, width: float) -> PanelContainer:
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 12)
	column.add_child(_text(title, 18))
	column.add_child(HSeparator.new())
	column.add_child(content)
	var panel: PanelContainer = visual_context.styles.panel(column)
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	panel.custom_minimum_size.x = width
	return panel
func _rich(node_name: String) -> RichTextLabel:
	var label := RichTextLabel.new()
	label.name = node_name
	label.fit_content = true
	label.scroll_active = false
	label.selection_enabled = true
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.custom_minimum_size.y = 80
	return label
func _build() -> void:
	var header := HBoxContainer.new()
	var heading := _text("INSCRIPTION WORKBENCH", 27)
	heading.theme_type_variation = "SectionTitle"
	header.add_child(heading)
	header.add_child(_button("BACK", "LoadoutBackButton", back_requested.emit))
	add_child(header)
	add_child(_text("Visual prototype only. No items, attributes or runs are changed.", 14, "accent.warm"))
	var tools := HBoxContainer.new()
	tools.add_child(_text("Example layout", 14))
	preset_picker.name = "LoadoutPresetPicker"
	preset_picker.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	preset_picker.item_selected.connect(preset_requested.emit)
	tools.add_child(preset_picker)
	tools.add_child(_button("Restore example", "LoadoutRestoreButton", restore_requested.emit))
	tools.add_child(_button("Clear draft", "LoadoutClearButton", clear_requested.emit))
	add_child(tools)
	columns.add_theme_constant_override("separation", 16)
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(columns)
	var inventory := VBoxContainer.new()
	inventory.add_theme_constant_override("separation", 10)
	search_field.name = "InscriptionSearch"
	search_field.placeholder_text = visual_context.translator.text("Search inscriptions")
	search_field.text_changed.connect(search_requested.emit)
	inventory.add_child(search_field)
	inventory_grid.name = "InscriptionInventory"
	inventory_grid.add_theme_constant_override("h_separation", 8)
	inventory_grid.add_theme_constant_override("v_separation", 8)
	inventory_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inventory_scroll.name = "InscriptionInventoryScroll"
	inventory_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	inventory_scroll.follow_focus = true
	inventory_scroll.add_child(inventory_grid)
	inventory.add_child(inventory_scroll)
	_inventory_panel = _panel("INSCRIPTIONS", inventory, 240)
	columns.add_child(_inventory_panel)
	var board := VBoxContainer.new()
	board.add_theme_constant_override("separation", 12)
	capacity_label = _text("", 20)
	capacity_label.name = "LoadoutCapacity"
	board.add_child(capacity_label)
	capacity_meter.name = "LoadoutBudgetMeter"
	capacity_meter.show_percentage = false
	capacity_meter.custom_minimum_size.y = 8
	capacity_meter.mouse_filter = Control.MOUSE_FILTER_IGNORE
	capacity_meter.add_theme_stylebox_override("background", visual_context.styles.box(empty_slot_color, 0, empty_slot_border, 0))
	capacity_meter.add_theme_stylebox_override("fill", visual_context.styles.box(visual_context.styles.color("accent.positive"), 0, Color.TRANSPARENT, 0))
	board.add_child(capacity_meter)
	var trace = preload("res://scripts/ui/components/circuit_trace.gd").new()
	trace.custom_minimum_size.y = 24
	trace.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	board.add_child(trace)
	slot_grid.name = "InscriptionSlots"
	slot_grid.columns = 2
	slot_grid.add_theme_constant_override("h_separation", 10)
	slot_grid.add_theme_constant_override("v_separation", 10)
	board.add_child(slot_grid)
	warning = _text("", 14, "accent.warm")
	warning.name = "LoadoutDraftWarning"
	board.add_child(warning)
	var buttons := HBoxContainer.new()
	equip_button = _button("Stage equip", "LoadoutEquipButton", equip_requested.emit)
	equip_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	buttons.add_child(equip_button)
	remove_button = _button("Remove", "LoadoutRemoveButton", remove_requested.emit)
	buttons.add_child(remove_button)
	board.add_child(buttons)
	board.add_child(_text("Drag an inscription into a position. Drag between occupied positions to swap. Or select an item and a position, then stage equip.", 14))
	board.add_child(HSeparator.new())
	board.add_child(_text("INSCRIPTION DETAILS", 16))
	details = _rich("InscriptionDetails")
	board.add_child(details)
	var board_panel := _panel("LOCAL LOADOUT DRAFT", board, 340)
	board_panel.size_flags_stretch_ratio = 1.3
	columns.add_child(board_panel)
	attributes_view = preload("res://scripts/ui/loadout/character_attributes_view.gd").new()
	attributes_view.mount(visual_context)
	columns.add_child(_panel("CHARACTER ATTRIBUTES", attributes_view, 300))

func _tile(node_name: String, minimum_height: float, callback: Callable) -> Button:
	var button := preload("res://scripts/ui/loadout/inscription_tile.gd").new()
	button.drag_scope = get_instance_id()
	button.inscription_dropped.connect(drop_requested.emit)
	button.name = node_name
	button.custom_minimum_size = Vector2(130, minimum_height)
	button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	button.pressed.connect(callback)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 10)
	margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var content := VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 5)
	var title := _text("", 16)
	title.name = "TileTitle"
	content.add_child(title)
	var subtitle := _text("", 13, "text.secondary")
	subtitle.name = "TileSubtitle"
	content.add_child(subtitle)
	margin.add_child(content)
	button.add_child(margin)
	return button
func _tile_text(button: Button, title: String, subtitle: String, active: bool, empty := false) -> void:
	button.find_child("TileTitle", true, false).text = title
	button.find_child("TileSubtitle", true, false).text = subtitle
	button.tooltip_text = title + "\n" + subtitle
	var style: StyleBoxFlat = visual_context.styles.box(
		empty_slot_color if empty else (visual_context.styles.PANEL_LIGHT if active else visual_context.styles.PANEL), 2,
		visual_context.styles.GOLD if active else (empty_slot_border if empty else visual_context.styles.MUTED), 2 if active else 1)
	button.add_theme_stylebox_override("normal", style)
	button.add_theme_stylebox_override("hover", visual_context.styles.box(empty_slot_color if empty else visual_context.styles.PANEL_LIGHT, 2, visual_context.styles.GOLD, 2))

func present(model: Dictionary) -> void:
	_model = model.duplicate(true)
	var t: Callable = visual_context.translator.text
	if not _choices_built:
		_choices_built = true
		for preset in model.presets: preset_picker.add_item(t.call(preset.name))
		for item in model.items:
			var id: String = item.instanceId
			var button := _tile("Inscription_" + id, 124, func(): item_requested.emit(id))
			inventory_grid.add_child(button)
			_inventory[id] = button
		for slot in model.slots:
			var id: String = slot
			var button := _tile("LoadoutSlot_" + id, 104, func(): slot_requested.emit(id))
			slot_grid.add_child(button)
			_positions[id] = button
	preset_picker.select(int(model.presetIndex))
	var items := {}
	var stats := {}
	for stat in model.character.attributes: stats[stat.id] = stat
	for item in model.items:
		items[item.instanceId] = item
		var title: String = t.call(item.name)
		var bonuses: Array[String] = _bonus_lines(item, stats)
		var subtitle: String = (bonuses[0] + "\n" if not bonuses.is_empty() else "") + "%s %s" % [visual_context.translator.number(item.cost), t.call("points")]
		for bonus in item.attributeBonuses:
			if float(bonus.amount) < 0:
				subtitle += " · " + t.call("Tradeoff")
				break
		if item.instanceId in model.assignments.values(): subtitle += " · " + t.call("In draft")
		_tile_text(_inventory[item.instanceId], title, subtitle, model.selectedItem == item.instanceId)
		_inventory[item.instanceId].tooltip_text = title + "\n" + t.call("Character attribute bonuses") + "\n" + "\n".join(_bonus_lines(item, stats))
		_inventory[item.instanceId].item_id = item.instanceId
		_inventory[item.instanceId].source_slot = str(model.assignments.find_key(item.instanceId)) if item.instanceId in model.assignments.values() else ""
		_inventory[item.instanceId].visible = str(model.search).is_empty() or (title + " " + t.call(item.family) + " " + " ".join(item.tags)).to_lower().contains(str(model.search).to_lower())
	for index in model.slots.size():
		var id: String = model.slots[index]
		var item: Dictionary = items.get(model.assignments.get(id, ""), {})
		var title: String = "+  " + t.call("Empty position") if item.is_empty() else t.call(item.name)
		var subtitle: String = t.call("Position %s") % (index + 1)
		subtitle += " · " + t.call("Drop here") if item.is_empty() else " · %s %s" % [visual_context.translator.number(item.cost), t.call("points")]
		_tile_text(_positions[id], title, subtitle, model.selectedSlot == id, item.is_empty())
		if not item.is_empty(): _positions[id].tooltip_text += "\n" + "\n".join(_bonus_lines(item, stats))
		_positions[id].slot_id = id
		_positions[id].item_id = model.assignments.get(id, "")
		_positions[id].source_slot = id
	for tile in _inventory.values() + _positions.values():
		tile.revision = JSON.stringify(model.assignments)
		tile.known_items = items.keys()
	capacity_label.text = t.call("Display budget: %s / %s") % [visual_context.translator.number(model.usedCapacity), visual_context.translator.number(model.capacity)]
	capacity_meter.max_value = maxf(1, float(model.capacity))
	capacity_meter.value = float(model.usedCapacity)
	var meter_fill: StyleBoxFlat = capacity_meter.get_theme_stylebox("fill")
	meter_fill.bg_color = visual_context.styles.color("accent.danger" if model.overBudget else "accent.positive")
	capacity_meter.tooltip_text = capacity_label.text
	warning.text = t.call("Over the example budget. This draft is not authoritative.") if model.overBudget else t.call("Illustrative costs only. Engine validation is not connected yet.")
	equip_button.disabled = str(model.selectedItem).is_empty() or str(model.selectedSlot).is_empty()
	remove_button.disabled = not model.assignments.has(model.selectedSlot)
	var selected: Dictionary = items.get(model.selectedItem, {})
	details.text = t.call("Choose an inscription to inspect its benefits and tradeoffs.")
	if not selected.is_empty():
		var lines: Array[String] = [t.call(selected.name), t.call("Character attribute bonuses")]
		lines.append_array(_bonus_lines(selected, stats))
		lines.append(t.call("Changes the character's attributes, not a card's base values."))
		if not selected.tradeoffs.is_empty():
			lines.append(""); lines.append(t.call("SCOPE"))
			for value in selected.tradeoffs: lines.append(t.call(value))
		details.text = "\n".join(lines)
	attributes_view.present(model.character)
	_responsive.call_deferred()
func _bonus_lines(item: Dictionary, stats: Dictionary) -> Array[String]:
	var rows: Array[String] = []
	for bonus in item.attributeBonuses:
		var stat: Dictionary = stats[bonus.attributeId]
		rows.append(preload("res://scripts/ui/loadout/loadout_stat_text.gd").bonus(stat, bonus.amount, visual_context.translator) + " " + visual_context.translator.text(stat.label))
	return rows
func _responsive() -> void:
	columns.vertical = size.x < 1060
	inventory_scroll.custom_minimum_size.y = clampf(get_viewport_rect().size.y - 340, 280, 500)
	inventory_grid.columns = 2 if is_instance_valid(_inventory_panel) and _inventory_panel.size.x >= 400 else 1
