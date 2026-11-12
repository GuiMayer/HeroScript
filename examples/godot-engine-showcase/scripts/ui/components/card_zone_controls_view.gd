@tool
class_name CardZoneControlsView
extends ScrollContainer
signal zone_requested(address: Dictionary)
const PILE_ICONS := {"draw": preload("res://assets/game-icons/faithtoken/card-draw.svg"), "discard": preload("res://assets/game-icons/faithtoken/card-discard.svg"), "exile": preload("res://assets/game-icons/delapouite/card-burn.svg"), "pile": preload("res://assets/game-icons/delapouite/stack.svg")}
var _row: VBoxContainer
var _buttons := {}
static func create() -> CardZoneControlsView:
	return (load("res://scenes/ui/components/card_zone_controls.tscn") as PackedScene).instantiate()
func apply_model(zones: Array, context) -> void:
	theme = context.theme; _row = get_node("Zones")
	var live: Array = []
	for index in zones.size():
		var zone: Dictionary = zones[index]
		var address := {"zoneId": zone.get("zoneId", ""), "ownerId": zone.get("ownerId", ""), "scopeId": zone.get("scopeId", null)}
		var key := JSON.stringify([address.zoneId, address.ownerId, address.scopeId]); live.append(key)
		var button: Button = _buttons.get(key)
		if button == null:
			button = preload("res://scenes/ui/components/pile_button.tscn").instantiate(); button.name = "Pile_" + key.sha256_text().substr(0, 12)
			button.pressed.connect(func(): zone_requested.emit(button.get_meta("zone_address").duplicate(true)))
			_row.add_child(button); _buttons[key] = button
		_row.move_child(button, index)
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.set_meta("zone_address", address)
		var presentation: Dictionary = zone.get("presentation", {}) if zone.get("presentation") is Dictionary else {}
		var slot := str(presentation.get("slot", ""))
		var label_key := str(presentation.get("labelKey", presentation.get("displayName", address.zoneId)))
		var label: String = context.translator.text(label_key)
		var default_icon := "exile" if slot == "exile_pile" else str({"draw": "draw", "discard": "discard", "exile": "exile", "banish": "exile"}.get(address.zoneId, "pile"))
		var icon_id: String = str(presentation.get("iconId", default_icon))
		var default_short := "Exile" if slot == "exile_pile" else str({"draw": "Draw", "discard": "Discard", "exile": "Exile", "banish": "Exile", "owned": "Deck"}.get(address.zoneId, label_key))
		var short_label: String = context.translator.text(str(presentation.get("shortLabelKey", default_short)))
		var count := int(zone.get("count", 0))
		preload("res://scripts/ui/combat_chrome.gd").apply(button, icon_id, "%s\n%s" % [short_label, count], context)
		button.tooltip_text = "%s · %s" % [label, count]; button.accessibility_name = button.tooltip_text
		button.expand_icon = false; button.clip_text = true; button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		_apply_pile_style(button, default_icon, short_label, count, context)
	for key in _buttons.keys():
		if key not in live:
			_row.remove_child(_buttons[key]); _buttons[key].queue_free(); _buttons.erase(key)

func _apply_pile_style(button: Button, icon_id: String, label: String, count: int, context) -> void:
	button.icon = null
	button.text = ""
	var icon := button.get_node_or_null("PileIcon") as TextureRect
	if icon == null:
		icon = TextureRect.new(); icon.name = "PileIcon"
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		button.add_child(icon)
		icon.anchor_top = 0.5; icon.anchor_bottom = 0.5
		icon.offset_left = 8; icon.offset_right = 50; icon.offset_top = -21; icon.offset_bottom = 21
		var badge := PanelContainer.new(); badge.name = "CountBadge"
		badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
		badge.add_theme_stylebox_override("panel", StyleBoxEmpty.new())
		icon.add_child(badge)
		badge.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
		badge.offset_left = -21; badge.offset_right = 21; badge.offset_top = -21; badge.offset_bottom = 21
		var number := Label.new(); number.name = "Count"
		number.mouse_filter = Control.MOUSE_FILTER_IGNORE
		number.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		number.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		number.add_theme_font_size_override("font_size", 32)
		number.add_theme_color_override("font_color", Color.WHITE)
		number.add_theme_color_override("font_outline_color", Color.BLACK)
		number.add_theme_constant_override("outline_size", 5)
		badge.add_child(number)
		var title := Label.new(); title.name = "PileLabel"
		title.mouse_filter = Control.MOUSE_FILTER_IGNORE
		title.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		title.clip_text = true
		button.add_child(title)
		title.anchor_right = 1.0; title.anchor_bottom = 1.0
		title.offset_left = 56; title.offset_right = -6
	icon.texture = PILE_ICONS.get(icon_id, PILE_ICONS.pile)
	icon.self_modulate = context.styles.color("text.primary")
	button.get_node("PileLabel").add_theme_color_override("font_color", context.styles.color("text.primary"))
	icon.get_node("CountBadge/Count").text = str(count)
	button.get_node("PileLabel").text = label
