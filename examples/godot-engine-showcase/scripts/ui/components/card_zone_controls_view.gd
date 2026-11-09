@tool
class_name CardZoneControlsView
extends ScrollContainer
signal zone_requested(address: Dictionary)
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
		preload("res://scripts/ui/combat_chrome.gd").apply(button, icon_id, "%s\n%s" % [short_label, zone.get("count", 0)], context)
		button.tooltip_text = "%s · %s" % [label, zone.get("count", 0)]; button.accessibility_name = button.tooltip_text
		button.expand_icon = false; button.clip_text = true; button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	for key in _buttons.keys():
		if key not in live:
			_row.remove_child(_buttons[key]); _buttons[key].queue_free(); _buttons.erase(key)
