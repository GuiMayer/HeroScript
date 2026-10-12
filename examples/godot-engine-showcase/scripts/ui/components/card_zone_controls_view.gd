extends ScrollContainer
signal zone_requested(address: Dictionary)
var _row := HBoxContainer.new()
var _buttons := {}
func _init() -> void:
	name = "PileControls"; horizontal_scroll_mode = SCROLL_MODE_AUTO; vertical_scroll_mode = SCROLL_MODE_DISABLED
	custom_minimum_size = Vector2(140, 64); size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_row.add_theme_constant_override("separation", 8); add_child(_row)
func apply_model(zones: Array, context) -> void:
	theme = context.theme
	var live: Array = []
	for zone in zones:
		var address := {"zoneId": zone.get("zoneId", ""), "ownerId": zone.get("ownerId", ""), "scopeId": zone.get("scopeId", null)}
		var key := JSON.stringify([address.zoneId, address.ownerId, address.scopeId]); live.append(key)
		var button: Button = _buttons.get(key)
		if button == null:
			button = Button.new(); button.name = "Pile_" + key.sha256_text().substr(0, 12)
			button.custom_minimum_size = Vector2(120, 60)
			button.add_theme_font_size_override("font_size", 17)
			button.pressed.connect(func(): zone_requested.emit(button.get_meta("zone_address").duplicate(true)))
			_row.add_child(button); _buttons[key] = button
		button.set_meta("zone_address", address)
		var label: String = context.translator.text(str(zone.get("presentation", {}).get("labelKey", address.zoneId)))
		var icon_id: String = str(zone.get("presentation", {}).get("iconId", {"draw": "draw", "discard": "discard", "exile": "exile"}.get(address.zoneId, "pile")))
		var short_label: String = context.translator.text(str(zone.get("presentation", {}).get("shortLabelKey", {"draw": "Draw", "discard": "Discard", "exile": "Exile"}.get(address.zoneId, label))))
		preload("res://scripts/ui/combat_chrome.gd").apply(button, icon_id, "%s\n%s" % [short_label, zone.get("count", 0)], context)
		button.tooltip_text = "%s · %s" % [label, zone.get("count", 0)]; button.accessibility_name = button.tooltip_text
		button.expand_icon = false; button.clip_text = true; button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	for key in _buttons.keys():
		if key not in live:
			_row.remove_child(_buttons[key]); _buttons[key].queue_free(); _buttons.erase(key)
