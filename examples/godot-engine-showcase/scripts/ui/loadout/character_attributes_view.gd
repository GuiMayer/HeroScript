@tool
class_name CharacterAttributesView
extends VBoxContainer
const Context = preload("res://scripts/ui/visual_context.gd")
const RowScene = preload("res://scenes/ui/components/loadout/attribute_row.tscn")
const GroupScene = preload("res://scenes/ui/components/loadout/attribute_group.tscn")
var visual_context
@export var editorial_layout := false
@export var fill_available_height := false
@export var value_heading := "Equipped"
@export var preview_character: Dictionary = {"name":"Preview hero","attributes":[{"id":"health","group":"Resources","label":"Health","base":72,"value":80,"change":8,"format":"number","sources":[]}]}
var _rows := {}
var _character_name: Label
var _table: VBoxContainer
var _scroll: ScrollContainer
var _schema: Array = []
var _authored_font_sizes: Dictionary = {}
static func create() -> CharacterAttributesView:
	return (load("res://scenes/ui/components/loadout/character_attributes_view.tscn") as PackedScene).instantiate()
func _bind_nodes() -> void:
	_character_name = get_node("%CharacterName")
	_table = get_node("%Table"); _scroll = get_node("%LoadoutAttributeScroll")
func _ready() -> void:
	_bind_nodes()
	if visual_context == null and (Engine.is_editor_hint() or get_parent() == get_tree().root):
		mount(Context.new()); present(preview_character)
	_fit_scroll()
	if not resized.is_connected(_fit_scroll): resized.connect(_fit_scroll)
	var viewport := get_viewport()
	if viewport != null and not viewport.size_changed.is_connected(_fit_scroll): viewport.size_changed.connect(_fit_scroll)
func _fit_scroll() -> void:
	if _scroll == null: return
	if fill_available_height:
		_scroll.custom_minimum_size.y = 0; return
	if is_inside_tree(): _scroll.custom_minimum_size.y = clampf(get_viewport_rect().size.y - (450 if editorial_layout else 340),270 if editorial_layout else 300,650 if editorial_layout else 700)
func configure_text(label: Label, key: String, size: int) -> void:
	label.set_meta("attribute_translation_key",key)
	label.set_meta("attribute_base_font_size",size)
	label.text = visual_context.translator.text(key)
	label.add_theme_font_size_override("font_size",roundi(size * visual_context.text_scale))
	label.add_theme_color_override("font_color",visual_context.styles.color("text.primary"))
func mount(context) -> void:
	_bind_nodes(); visual_context = context; theme = context.theme
	name = "LoadoutCharacterAttributes"
	for label in find_children("*","Label",true,false):
		var base_size: float
		if label.has_meta("attribute_base_font_size"):
			base_size = float(label.get_meta("attribute_base_font_size"))
		else:
			var id := label.get_instance_id()
			if not _authored_font_sizes.has(id): _authored_font_sizes[id] = label.get_theme_font_size("font_size")
			base_size = float(_authored_font_sizes[id])
		label.add_theme_font_size_override("font_size",roundi(base_size * context.text_scale))
		label.add_theme_color_override("font_color",context.styles.color(str(label.get_meta("attribute_color_role","text.primary"))))
		if label.has_meta("attribute_translation_key"): label.text = context.translator.text(str(label.get_meta("attribute_translation_key")))
	get_node("%Portrait").visible = editorial_layout
	get_node("%Description").visible = not editorial_layout
	get_node("%Explanation").visible = not editorial_layout
	_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL if fill_available_height else Control.SIZE_FILL
	_scroll.custom_minimum_size.y = 0 if fill_available_height else 300
	get_node("%Base").visible = not editorial_layout
	get_node("%Equipped").set_meta("attribute_translation_key",value_heading)
	get_node("%Equipped").text = context.translator.text(value_heading)
	for caption in [get_node("%Base"),get_node("%Equipped")]: caption.custom_minimum_size.x = 65 * context.text_scale
func present(character: Dictionary) -> void:
	var translator = visual_context.translator
	for label in find_children("*", "Label", true, false):
		if label.has_meta("attribute_translation_key"): label.text = translator.text(str(label.get_meta("attribute_translation_key")))
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
				var heading = GroupScene.instantiate()
				configure_text(heading.get_node("Heading"), stat.group, 12)
				heading.get_node("Heading").set_meta("attribute_color_role","accent.primary")
				heading.get_node("Heading").add_theme_color_override("font_color", visual_context.styles.color("accent.primary"))
				_table.add_child(heading)
			var row = RowScene.instantiate()
			row.name = "Attribute_" + str(stat.id)
			var marker = row.get_node("Marker"); marker.color = visual_context.styles.color("accent.primary")
			configure_text(row.get_node("Label"), stat.label, 14)
			var base = row.get_node("Base"); var equipped = row.get_node("Equipped")
			for number in [base, equipped]:
				configure_text(number, "", 14)
				number.custom_minimum_size.x = 65 * visual_context.text_scale
			base.visible = not editorial_layout
			_table.add_child(row)
			_rows[stat.id] = {"row": row,"base": base,"equipped": equipped,"marker": marker}
		var controls: Dictionary = _rows[stat.id]
		controls.base.visible = not editorial_layout
		controls.marker.color = visual_context.styles.color("accent.primary")
		controls.marker.visible = editorial_layout and stat.id in character.get("highlightedAttributeIds", [])
		controls.base.text = preload("res://scripts/ui/loadout/loadout_stat_text.gd").value(stat, stat.base, translator)
		controls.equipped.text = preload("res://scripts/ui/loadout/loadout_stat_text.gd").value(stat, stat.value, translator)
		if stat.has("displayValue"): controls.equipped.text = str(stat.displayValue)
		var role := "text.primary" if is_zero_approx(float(stat.change)) else ("accent.positive" if float(stat.change) > 0 else "accent.danger")
		controls.equipped.add_theme_color_override("font_color", visual_context.styles.color(role))
		var sources: Array[String] = [translator.text(stat.label)]
		if editorial_layout:
			sources.append(translator.text("Base: %s → Current: %s") % [controls.base.text, controls.equipped.text])
		for source in stat.sources:
			sources.append(translator.text(source.name) + ": " + preload("res://scripts/ui/loadout/loadout_stat_text.gd").bonus(stat, source.amount, translator))
		for control in [controls.row, controls.base, controls.equipped]: control.tooltip_text = "\n".join(sources)
