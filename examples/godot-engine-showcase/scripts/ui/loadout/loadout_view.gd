@tool
class_name InscriptionLoadoutView
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
@export var editorial_layout := false
@export var shared_attributes := false
var _filter := "All"
var _filter_buttons := {}
var _separators: Array[Control] = []
var _model: Dictionary = {}
var _inventory := {}
var _positions := {}
var _choices_built := false
var columns: BoxContainer
var inventory_grid: GridContainer
var inventory_scroll: ScrollContainer
var slot_grid: GridContainer
var preset_picker: OptionButton
var search_field: LineEdit
var capacity_label: Label
var capacity_meter: ProgressBar
var warning: Label
var details: RichTextLabel
var attributes_view
var equip_button: Button
var remove_button: Button
var _inventory_panel: PanelContainer
var _identity: Array = []
var _authority_note: Label
var _authored_font_sizes := {}

static func create(editorial := false) -> InscriptionLoadoutView:
	var path := "res://scenes/ui/components/loadout/inscriptions_" + ("editorial" if editorial else "workbench") + ".tscn"
	return (load(path) as PackedScene).instantiate()

func _ready() -> void:
	if visual_context == null:
		mount(preload("res://scripts/ui/visual_context.gd").new(null, {}, preload("res://data/themes/ivory_theme.tres")))

func mount(context) -> void:
	var first_mount := visual_context == null
	if first_mount and editorial_layout:
		find_child("ClearFilters", true, false).pressed.connect(func(): _filter = "All"; search_field.text = ""; search_requested.emit(""))
	visual_context = context; theme = context.theme
	columns = find_child("LoadoutColumns", true, false)
	inventory_grid = find_child("InscriptionInventory", true, false)
	inventory_scroll = find_child("InscriptionInventoryScroll", true, false)
	slot_grid = find_child("InscriptionSlots", true, false)
	preset_picker = find_child("LoadoutPresetPicker", true, false)
	search_field = find_child("InscriptionSearch", true, false)
	capacity_label = find_child("LoadoutCapacity", true, false)
	capacity_meter = find_child("LoadoutBudgetMeter", true, false)
	warning = find_child("LoadoutDraftWarning", true, false)
	details = find_child("InscriptionDetails", true, false)
	equip_button = find_child("LoadoutEquipButton", true, false)
	remove_button = find_child("LoadoutRemoveButton", true, false)
	_inventory_panel = find_child("InventoryPanel", true, false)
	_authority_note = find_child("AuthorityNote", true, false)
	_separators.assign(columns.find_children("*", "VSeparator", false, false))
	var character_panel = find_child("CharacterPanel", true, false)
	if shared_attributes:
		if character_panel != null:
			character_panel.hide()
			if not Engine.is_editor_hint():
				character_panel.get_parent().remove_child(character_panel); character_panel.free()
			if not _separators.is_empty():
				var rule = _separators.pop_back(); rule.hide()
				if not Engine.is_editor_hint():
					rule.get_parent().remove_child(rule); rule.free()
		attributes_view = null
	else:
		attributes_view = find_child("LoadoutCharacterAttributes", true, false)
		if attributes_view != null: attributes_view.mount(context)
	_translate(self)
	search_field.placeholder_text = context.translator.text("Search inscriptions")
	if first_mount:
		for style_name in ["background", "fill"]: capacity_meter.add_theme_stylebox_override(style_name, capacity_meter.get_theme_stylebox(style_name).duplicate())
		preset_picker.item_selected.connect(preset_requested.emit)
		search_field.text_changed.connect(search_requested.emit)
		equip_button.pressed.connect(equip_requested.emit)
		remove_button.pressed.connect(remove_requested.emit)
		find_child("LoadoutClearButton", true, false).pressed.connect(clear_requested.emit)
		find_child("LoadoutRestoreButton", true, false).pressed.connect(restore_requested.emit)
		var back = find_child("LoadoutBackButton", true, false)
		if back != null: back.pressed.connect(back_requested.emit)
		for key in ["All", "Offense", "Defense", "Utility"]:
			var filter = find_child("LoadoutFilter" + key, true, false)
			if filter == null: continue
			filter.pressed.connect(func(): _filter = key; present(_model))
			_filter_buttons[key] = filter
	if not resized.is_connected(_responsive): resized.connect(_responsive)
	_responsive.call_deferred()

func _translate(node: Node) -> void:
	if node.has_meta("source_text"):
		node.text = visual_context.translator.text(str(node.get_meta("source_text")))
	if node is Label and not node.has_meta("font_role") and node.has_theme_font_size_override("font_size"):
		var id := node.get_instance_id()
		if not _authored_font_sizes.has(id): _authored_font_sizes[id] = node.get_theme_font_size("font_size")
		node.add_theme_font_size_override("font_size", roundi(_authored_font_sizes[id] * visual_context.text_scale))
	for child in node.get_children():
		if child.scene_file_path.is_empty(): _translate(child)

func _tile(node_name: String, minimum_height: float, callback: Callable) -> Button:
	var button = preload("res://scripts/ui/loadout/inscription_tile.gd").create(editorial_layout)
	button.drag_scope = get_instance_id()
	button.inscription_dropped.connect(drop_requested.emit)
	button.name = node_name
	button.custom_minimum_size = Vector2(90 if editorial_layout else 130, 168 * visual_context.text_scale if editorial_layout else minimum_height)
	button.pressed.connect(callback)
	for entry in [["TileTitle", "text.primary"], ["TileSubtitle", "text.secondary"], ["InscriptionCost", "text.primary"]]:
		var label = button.find_child(entry[0], true, false)
		if label != null: label.add_theme_color_override("font_color", visual_context.styles.color(entry[1]))
	visual_context.styles.apply_view_preferences(button)
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
	if editorial_layout:
		button.add_theme_stylebox_override("normal", visual_context.styles.box(empty_slot_color if empty else (visual_context.styles.PANEL_LIGHT if active else Color.TRANSPARENT), 0, visual_context.styles.color("accent.primary") if active else empty_slot_border, 2 if active else 1))
		button.find_child("InscriptionSigil", true, false).visible = not empty
		button.find_child("InscriptionCost", true, false).visible = not empty

func present(model: Dictionary) -> void:
	_model = model.duplicate(true)
	var t: Callable = visual_context.translator.text
	var identity: Array = [model.items.map(func(item): return item.instanceId), model.slots, model.get("contextKey", [])]
	if _choices_built and identity != _identity:
		for child in inventory_grid.get_children(): inventory_grid.remove_child(child); child.queue_free()
		for child in slot_grid.get_children(): slot_grid.remove_child(child); child.queue_free()
		_inventory.clear(); _positions.clear(); preset_picker.clear(); _choices_built = false
	_identity = identity
	var remote: bool = model.get("authority") == "REST"
	var live: bool = model.get("liveEquipment", false)
	for key in _filter_buttons: _filter_buttons[key].set_pressed_no_signal(key == _filter)
	_authority_note.text = t.call("Engine loadout. Changes require preview and confirmation.") if remote else t.call("Visual prototype only. No items, attributes or runs are changed.")
	if live: _authority_note.text = t.call("Equipment and character attributes update automatically.")
	find_child("LoadoutRestoreButton", true, false).visible = not live
	find_child("LoadoutBoardTitle", true, false).text = t.call("EQUIPPED INSCRIPTIONS" if live else "LOCAL LOADOUT DRAFT")
	find_child("LoadoutInteractionHint", true, false).text = t.call("Drag to equip, or select an inscription then a position. Changes are saved automatically." if live else "Drag an inscription into a position. Drag between occupied positions to swap. Or select an item and a position, then stage equip.")
	equip_button.text = t.call("Equip" if live else "Stage equip")
	find_child("LoadoutClearButton", true, false).disabled = not model.get("canEdit", true)
	preset_picker.visible = not remote
	find_child("LoadoutLayoutLabel", true, false).visible = not remote
	find_child("LoadoutRestoreButton", true, false).text = t.call("Discard draft" if remote else "Restore example")
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
	if int(model.presetIndex) >= 0: preset_picker.select(int(model.presetIndex))
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
		if item.instanceId in model.assignments.values(): subtitle += " · " + t.call("Equipped" if live else "In draft")
		_tile_text(_inventory[item.instanceId], title, subtitle, model.selectedItem == item.instanceId)
		if editorial_layout:
			_inventory[item.instanceId].find_child("InscriptionSigil", true, false).texture = preload("res://scripts/ui/loadout/inscription_visual_catalog.gd").texture(item)
			_inventory[item.instanceId].find_child("InscriptionCost", true, false).text = visual_context.translator.number(item.cost)
			_inventory[item.instanceId].find_child("TileSubtitle", true, false).text = t.call("Equipped") if item.instanceId in model.assignments.values() else ""
		_inventory[item.instanceId].tooltip_text = title + "\n" + t.call("Character attribute bonuses") + "\n" + "\n".join(_bonus_lines(item, stats))
		_inventory[item.instanceId].item_id = item.instanceId
		_inventory[item.instanceId].source_slot = str(model.assignments.find_key(item.instanceId)) if item.instanceId in model.assignments.values() else ""
		_inventory[item.instanceId].visible = str(model.search).is_empty() or (title + " " + t.call(item.family) + " " + " ".join(item.tags)).to_lower().contains(str(model.search).to_lower())
		if editorial_layout: _inventory[item.instanceId].visible = _inventory[item.instanceId].visible and preload("res://scripts/ui/loadout/inscription_visual_catalog.gd").matches(item, _filter)
	if editorial_layout:
		find_child("EmptyResults", true, false).visible = not _inventory.values().any(func(tile): return tile.visible)
		find_child("EmptyResults", true, false).text = t.call("No inscriptions match. Clear filters to see your inventory.")
		find_child("ClearFilters", true, false).text = t.call("Clear filters")
	for index in model.slots.size():
		var id: String = model.slots[index]
		var item: Dictionary = items.get(model.assignments.get(id, ""), {})
		var title: String = "+  " + t.call("Empty position") if item.is_empty() else t.call(item.name)
		var subtitle: String = t.call("Position %s") % (index + 1)
		subtitle += " · " + t.call("Drop here") if item.is_empty() else " · %s %s" % [visual_context.translator.number(item.cost), t.call("points")]
		_tile_text(_positions[id], title, subtitle, model.selectedSlot == id, item.is_empty())
		if editorial_layout:
			_positions[id].find_child("TileSubtitle", true, false).text = t.call("Drop here") if item.is_empty() else t.call("Position %s") % (index + 1)
			if not item.is_empty():
				_positions[id].find_child("InscriptionSigil", true, false).texture = preload("res://scripts/ui/loadout/inscription_visual_catalog.gd").texture(item)
				_positions[id].find_child("InscriptionCost", true, false).text = visual_context.translator.number(item.cost)
		if not item.is_empty(): _positions[id].tooltip_text += "\n" + "\n".join(_bonus_lines(item, stats))
		_positions[id].slot_id = id
		_positions[id].item_id = model.assignments.get(id, "")
		_positions[id].source_slot = id
	for tile in _inventory.values() + _positions.values():
		tile.drag_scope = hash(model.get("contextKey", get_instance_id()))
		tile.revision = JSON.stringify(model.assignments)
		tile.known_items = items.keys()
	capacity_label.text = t.call("Equipment budget: %s / %s" if remote else "Display budget: %s / %s") % [visual_context.translator.number(model.usedCapacity), visual_context.translator.number(model.capacity)]
	capacity_meter.max_value = maxf(1, float(model.capacity))
	capacity_meter.value = float(model.usedCapacity)
	var meter_fill: StyleBoxFlat = capacity_meter.get_theme_stylebox("fill")
	meter_fill.bg_color = visual_context.styles.color("accent.danger" if model.overBudget else ("accent.primary" if editorial_layout else "accent.positive"))
	capacity_meter.tooltip_text = capacity_label.text
	if editorial_layout: capacity_label.text = "%s / %s" % [visual_context.translator.number(model.usedCapacity), visual_context.translator.number(model.capacity)]
	warning.text = t.call("Over the example budget. This draft is not authoritative.") if model.overBudget else t.call("Illustrative costs only. Engine validation is not connected yet.")
	if remote: warning.text = t.call(model.get("status", ""))
	equip_button.disabled = str(model.selectedItem).is_empty() or str(model.selectedSlot).is_empty()
	remove_button.disabled = not model.assignments.has(model.selectedSlot)
	if not model.get("canEdit", true): equip_button.disabled = true; remove_button.disabled = true
	var selected: Dictionary = items.get(model.selectedItem, {})
	if editorial_layout:
		equip_button.visible = not str(model.selectedItem).is_empty() and not str(model.selectedSlot).is_empty()
		remove_button.visible = model.assignments.has(model.selectedSlot)
		warning.visible = not warning.text.is_empty()
	details.text = t.call("Choose an inscription to inspect its benefits and tradeoffs.")
	if not selected.is_empty():
		var lines: Array[String] = [t.call(selected.name), t.call("Character attribute bonuses")]
		lines.append_array(_bonus_lines(selected, stats))
		if not str(selected.get("description", "")).is_empty(): lines.append(t.call(selected.description))
		lines.append(t.call("Changes the character's attributes, not a card's base values."))
		if not selected.tradeoffs.is_empty():
			lines.append(""); lines.append(t.call("SCOPE"))
			for value in selected.tradeoffs: lines.append(t.call(value))
		details.text = "\n".join(lines)
	var character: Dictionary = model.character.duplicate(true)
	if editorial_layout:
		for stat in character.attributes:
			if str(stat.id) in ["fire_bonus", "poison_bonus", "cold_bonus", "lightning_bonus"] or stat.get("contextTags", []).any(func(tag): return tag in ["fire", "poison", "cold", "lightning"]): stat.group = "Tag bonuses"
	character["highlightedAttributeIds"] = selected.get("attributeIds", selected.get("attributeBonuses", []).map(func(bonus): return str(bonus.get("attributeId", ""))))
	if attributes_view != null:
		attributes_view.present(character)
		attributes_view.visible = model.get("attributesReady", true)
	_responsive.call_deferred()
func _bonus_lines(item: Dictionary, stats: Dictionary) -> Array[String]:
	var rows: Array[String] = []
	for bonus in item.attributeBonuses:
		var stat: Dictionary = stats[bonus.attributeId]
		rows.append(preload("res://scripts/ui/loadout/loadout_stat_text.gd").bonus(stat, bonus.amount, visual_context.translator) + " " + visual_context.translator.text(stat.label))
	return rows
func _responsive() -> void:
	if visual_context == null or columns == null: return
	if editorial_layout:
		columns.vertical = size.x < 980 * minf(visual_context.text_scale, 1.15)
		for rule in _separators: rule.visible = not columns.vertical
		inventory_scroll.custom_minimum_size.y = clampf(get_viewport_rect().size.y - 490, 200, 650)
		inventory_grid.columns = 3 if _inventory_panel.size.x >= 315 else 2
		slot_grid.columns = 3 if size.x >= 1050 else 2
		return
	columns.vertical = size.x < 1060
	inventory_scroll.custom_minimum_size.y = clampf(get_viewport_rect().size.y - 340, 280, 500)
	inventory_grid.columns = 2 if is_instance_valid(_inventory_panel) and _inventory_panel.size.x >= 400 else 1
