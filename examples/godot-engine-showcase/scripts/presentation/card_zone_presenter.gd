extends RefCounted
## Turns revisioned zone presentation metadata into UI groups, without card rules.

var _zones: Array = []
var _i18n

func _init(snapshot: Dictionary, translator) -> void:
	_i18n = translator
	_zones = snapshot.get("zones", []).duplicate(true)

func playable_cards() -> Array:
	var result: Array = []
	for zone in _zones:
		if str(zone.get("presentation", {}).get("slot", "")) == "playable_cards" and bool(zone.get("contentsVisible", false)):
			result.append_array(zone.get("cards", []))
	return result

func playable_label() -> String:
	for zone in _zones:
		if str(zone.get("presentation", {}).get("slot", "")) == "playable_cards":
			return zone_label(zone)
	return _i18n.text("HAND")

func auxiliary_zones() -> Array:
	var result: Array = []
	for zone in _zones:
		if str(zone.get("presentation", {}).get("slot", "")) != "playable_cards":
			result.append(zone.duplicate(true))
	return result

func zone_label(zone: Dictionary) -> String:
	var label_key := str(zone.get("presentation", {}).get("labelKey", zone.get("zoneId", "")))
	return _i18n.text(label_key)

func zone_cards(zone_id: String) -> Array:
	for zone in _zones:
		if str(zone.get("zoneId", "")) == zone_id:
			return zone.get("cards", []).duplicate(true) if bool(zone.get("contentsVisible", false)) else []
	return []
