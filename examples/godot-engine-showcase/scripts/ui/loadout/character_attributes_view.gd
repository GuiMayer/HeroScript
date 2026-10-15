extends VBoxContainer
## Passive attribute projection, independent from cards, REST and the lab adapter.
var visual_context
var _rows := {}
var _character_name := Label.new()
var _table := VBoxContainer.new()
var _scroll := ScrollContainer.new()
var _schema: Array = []

func _ready() -> void:
	_fit_scroll()
	resized.connect(_fit_scroll)
	get_viewport().size_changed.connect(_fit_scroll)

func _fit_scroll() -> void:
	if is_inside_tree(): _scroll.custom_minimum_size.y = clampf(get_viewport_rect().size.y - 340, 300, 700)

func mount(context) -> void:
	visual_context = context
	name = "LoadoutCharacterAttributes"
	add_theme_constant_override("separation", 10)
	_character_name.add_theme_font_size_override("font_size", roundi(19 * context.text_scale))
	_character_name.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(_character_name)
	add_child(_label("Base character → with equipped inscriptions.", 13))
	var scroll := _scroll
	scroll.name = "LoadoutAttributeScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.custom_minimum_size.y = 300
	_table.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_table.add_theme_constant_override("separation", 7)
	scroll.add_child(_table)
	add_child(scroll)
	var header := HBoxContainer.new()
	var title := _label("Attribute", 12)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(title)
	for key in ["Base", "Equipped"]:
		var caption := _label(key, 12)
		caption.custom_minimum_size.x = 65 * context.text_scale
		caption.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		header.add_child(caption)
	_table.add_child(header)
	add_child(_label("Critical damage is the bonus per tier. Armor mitigation and dodge rolls are not simulated here.", 12))

func _label(key: String, font_size: int) -> Label:
	var label := Label.new()
	label.text = visual_context.translator.text(key)
	label.add_theme_font_size_override("font_size", roundi(font_size * visual_context.text_scale))
	label.add_theme_color_override("font_color", visual_context.styles.color("text.primary"))
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	return label

func present(character: Dictionary) -> void:
	var translator = visual_context.translator
	_character_name.text = translator.text(character.name)
	var schema: Array = character.attributes.map(func(stat): return [stat.id, stat.group, stat.label])
	if schema != _schema:
		# Keep the table header, but never retain rows from a different actor/setting.
		for index in range(_table.get_child_count() - 1, 0, -1):
			var child := _table.get_child(index)
			_table.remove_child(child)
			child.queue_free()
		_rows.clear()
		_schema = schema
	var group := ""
	for stat in character.attributes:
		if not _rows.has(stat.id):
			if stat.group != group:
				group = stat.group
				_table.add_child(HSeparator.new())
				var heading := _label(stat.group, 12)
				heading.add_theme_color_override("font_color", visual_context.styles.color("accent.primary"))
				_table.add_child(heading)
			var row := HBoxContainer.new()
			row.name = "Attribute_" + str(stat.id)
			var label := _label(stat.label, 14)
			label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			row.add_child(label)
			var base := _label("", 14)
			var equipped := _label("", 14)
			for number in [base, equipped]:
				number.custom_minimum_size.x = 65 * visual_context.text_scale
				number.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
				row.add_child(number)
			_table.add_child(row)
			_rows[stat.id] = {"row": row, "base": base, "equipped": equipped}
		var controls: Dictionary = _rows[stat.id]
		controls.base.text = preload("res://scripts/ui/loadout/loadout_stat_text.gd").value(stat, stat.base, translator)
		controls.equipped.text = preload("res://scripts/ui/loadout/loadout_stat_text.gd").value(stat, stat.value, translator)
		var role := "text.primary" if is_zero_approx(float(stat.change)) else ("accent.positive" if float(stat.change) > 0 else "accent.danger")
		controls.equipped.add_theme_color_override("font_color", visual_context.styles.color(role))
		var sources: Array[String] = [translator.text(stat.label)]
		for source in stat.sources:
			sources.append(translator.text(source.name) + ": " + preload("res://scripts/ui/loadout/loadout_stat_text.gd").bonus(stat, source.amount, translator))
		for control in [controls.row, controls.base, controls.equipped]: control.tooltip_text = "\n".join(sources)
