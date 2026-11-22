extends RefCounted
## Identity-only drag; legal options always come from the published snapshot.
static var _icons: Dictionary = {}

static func icon(definition_id: String) -> Texture2D:
	var kind := definition_id.trim_prefix("obor_overwrite_").trim_prefix("prototype_rune_").trim_suffix("_item")
	if kind == "frost_magma": kind = "frost"
	if kind == "prototype_split": kind = "fragment"
	var shapes := {
		"ember": ["#ec9860", "M32 15 L47 33 L32 49 L17 33 Z M32 22 V42"],
		"frost": ["#92c9ed", "M32 14 V50 M17 23 L47 41 M17 41 L47 23 M25 18 L32 25 L39 18 M25 46 L32 39 L39 46"],
		"fragment": ["#e7c080", "M17 17 H28 L47 32 L28 47 H17 M28 17 V47 M17 32 H47"],
		"rupture": ["#f0a45a", "M32 13 L36 27 L50 32 L36 37 L32 51 L28 37 L14 32 L28 27 Z"],
		"overflow": ["#a0c9ac", "M17 43 L31 29 L31 41 L48 19 M34 19 H48 V33"],
		"resonance": ["#d5b6ec", "M32 15 L49 32 L32 49 L15 32 Z M32 24 L40 32 L32 40 L24 32 Z"],
		"catalyst": ["#e6d577", "M32 14 L38 26 L50 32 L38 38 L32 50 L26 38 L14 32 L26 26 Z M32 24 V40 M24 32 H40"],
		"wet": ["#80ced4", "M16 26 Q24 18 32 26 T48 26 M16 38 Q24 30 32 38 T48 38"]}
	if not shapes.has(kind): return preload("res://assets/ui/rune_token.svg")
	if not _icons.has(kind):
		var spec: Array = shapes[kind]
		var svg := '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64"><path d="M32 2 L56 16 L61 40 L44 59 L20 59 L3 40 L8 16 Z" fill="#272b2e" stroke="%s" stroke-width="2"/><path d="%s" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/></svg>' % [spec[0], spec[1], spec[0]]
		var bitmap := Image.new()
		if bitmap.load_svg_from_string(svg) != OK: return preload("res://assets/ui/rune_token.svg")
		_icons[kind] = ImageTexture.create_from_image(bitmap)
	return _icons[kind]
static func payload(scope: int, generation: int, snapshot: Dictionary, item: Dictionary) -> Dictionary:
	return {"kind": "card_rune", "scope": scope, "generation": generation, "runId": snapshot.get("runId", ""), "sequence": snapshot.get("runSequence", -1), "revision": snapshot.get("contentRevision", ""), "itemInstanceId": item.get("instance", {}).get("itemInstanceId", "")}

static func matches(data: Variant, scope: int, generation: int, snapshot: Dictionary, item: Dictionary) -> bool:
	return data is Dictionary and not str(data.get("itemInstanceId", "")).is_empty() and data == payload(scope, generation, snapshot, item) and str(item.get("instance", {}).get("state", "")) == "Available"

static func preview(title: String, theme: Theme, definition_id := "") -> Control:
	var panel := PanelContainer.new()
	panel.theme = theme; panel.custom_minimum_size = Vector2(180, 62); panel.mouse_filter = Control.MOUSE_FILTER_IGNORE; panel.z_index = 100
	var style := StyleBoxFlat.new(); style.bg_color = Color("272b2e"); style.border_color = Color("d89e57")
	style.set_border_width_all(2); style.set_corner_radius_all(5); style.content_margin_left = 15; style.content_margin_right = 15
	panel.add_theme_stylebox_override("panel", style)
	var row := HBoxContainer.new(); row.mouse_filter = Control.MOUSE_FILTER_IGNORE; row.add_theme_constant_override("separation", 8); panel.add_child(row)
	var token := TextureRect.new(); token.texture = icon(definition_id); token.custom_minimum_size = Vector2(48, 48); token.expand_mode = TextureRect.EXPAND_IGNORE_SIZE; token.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED; token.mouse_filter = Control.MOUSE_FILTER_IGNORE; row.add_child(token)
	var label := Label.new(); label.text = title; label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER; label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.add_theme_color_override("font_color", Color("f9e6c5")); label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(label)
	return panel

static func highlight(card: Control, enabled: bool) -> void:
	var outline := card.get_node_or_null("RuneDropOutline") as Panel
	if outline == null:
		outline = Panel.new(); outline.name = "RuneDropOutline"; outline.mouse_filter = Control.MOUSE_FILTER_IGNORE
		card.add_child(outline); outline.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		var style := StyleBoxFlat.new(); style.bg_color = Color.TRANSPARENT; style.border_color = Color("b78342"); style.set_border_width_all(3)
		outline.add_theme_stylebox_override("panel", style)
	outline.visible = enabled
