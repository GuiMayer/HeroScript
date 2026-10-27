extends VBoxContainer
## Passive attribute projection, independent from cards, REST and the lab adapter.
var visual_context
var editorial_layout := false
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
	if is_inside_tree(): _scroll.custom_minimum_size.y = clampf(get_viewport_rect().size.y - (450 if editorial_layout else 340), 270 if editorial_layout else 300, 650 if editorial_layout else 700)

func mount(context) -> void:
	visual_context = context
	name = "LoadoutCharacterAttributes"
	add_theme_constant_override("separation", 10)
	_character_name.add_theme_font_size_override("font_size", roundi(19 * context.text_scale))
	_character_name.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	if editorial_layout:
		var identity := HBoxContainer.new(); identity.add_theme_constant_override("separation", 16)
		var portrait := TextureRect.new(); portrait.texture = preload("res://assets/ivory/actor-adept.svg")
		portrait.custom_minimum_size = Vector2(64, 86); portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE; portrait.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		identity.add_child(portrait); _character_name.size_flags_horizontal = Control.SIZE_EXPAND_FILL; identity.add_child(_character_name); add_child(identity)
	else:
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
		if editorial_layout and key == "Base": caption.hide()
		caption.custom_minimum_size.x = 65 * context.text_scale
		caption.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		header.add_child(caption)
	_table.add_child(header)
	if not editorial_layout: add_child(_label("Critical damage is the bonus per tier. Armor mitigation and dodge rolls are not simulated here.", 12))

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
			var marker := ColorRect.new(); marker.color = visual_context.styles.color("accent.primary"); marker.custom_minimum_size.x = 3
			marker.mouse_filter = Control.MOUSE_FILTER_IGNORE; row.add_child(marker)
			var label := _label(stat.label, 14)
			label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			row.add_child(label)
			var base := _label("", 14)
			var equipped := _label("", 14)
			for number in [base, equipped]:
				number.custom_minimum_size.x = 65 * visual_context.text_scale
				number.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
				row.add_child(number)
			if editorial_layout: base.hide()
			_table.add_child(row)
			_rows[stat.id] = {"row": row, "base": base, "equipped": equipped, "marker": marker}
		var controls: Dictionary = _rows[stat.id]
		controls.marker.visible = editorial_layout and stat.id in character.get("highlightedAttributeIds", [])
		controls.base.text = preload("res://scripts/ui/loadout/loadout_stat_text.gd").value(stat, stat.base, translator)
		controls.equipped.text = preload("res://scripts/ui/loadout/loadout_stat_text.gd").value(stat, stat.value, translator)
		var role := "text.primary" if is_zero_approx(float(stat.change)) else ("accent.positive" if float(stat.change) > 0 else "accent.danger")
		controls.equipped.add_theme_color_override("font_color", visual_context.styles.color(role))
		var sources: Array[String] = [translator.text(stat.label)]
		if editorial_layout:
			sources.append(translator.text("Base: %s → Current: %s") % [controls.base.text, controls.equipped.text])
		for source in stat.sources:
			sources.append(translator.text(source.name) + ": " + preload("res://scripts/ui/loadout/loadout_stat_text.gd").bonus(stat, source.amount, translator))
		for control in [controls.row, controls.base, controls.equipped]: control.tooltip_text = "\n".join(sources)
