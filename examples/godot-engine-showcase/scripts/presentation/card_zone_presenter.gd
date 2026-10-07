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
		if bool(zone.get("allowsCardPlay", false)) and bool(zone.get("contentsVisible", false)):
			result.append_array(zone.get("cards", []))
	return result

func playable_label() -> String:
	var labels: Array[String] = []
	for zone in _zones:
		if bool(zone.get("allowsCardPlay", false)):
			labels.append(zone_label(zone))
	return " / ".join(labels) if not labels.is_empty() else _i18n.text("PLAYABLE CARDS")

func total_cards() -> int:
	var result := 0
	for zone in _zones:
		# Count is authoritative even when a zone deliberately hides its contents.
		result += maxi(0, int(zone.get("count", 0)))
	return result

func auxiliary_zones() -> Array:
	var result: Array = []
	for zone in _zones:
		if not bool(zone.get("allowsCardPlay", false)):
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

static func tool_flows(run: Dictionary) -> Array:
	var mode: Dictionary = run.get("resolvedMode", {})
	if not bool(mode.get("capabilityPolicy", {}).get("allowCardZoneCheats", false)):
		return []
	var system: Dictionary = mode.get("cardZoneSystem", {})
	var result: Array = []
	for flow in system.get("flows", []):
		if flow is Dictionary and "Tool" in flow.get("allowedInvocations", []):
			result.append(flow.duplicate(true))
	result.sort_custom(func(a, b): return str(a.get("flowId", "")) < str(b.get("flowId", "")))
	return result

static func tool_payload(flow: Dictionary, definition_text: String, instance_text: String, actor_id: String) -> Dictionary:
	var payload := {"flowId": str(flow.get("flowId", ""))}
	var definitions := _split_ids(definition_text)
	var instances := _split_ids(instance_text)
	if not definitions.is_empty(): payload["cardDefinitionIds"] = definitions
	if not instances.is_empty(): payload["cardInstanceIds"] = instances
	if not actor_id.strip_edges().is_empty(): payload["actorId"] = actor_id.strip_edges()
	return payload

static func _split_ids(value: String) -> Array[String]:
	var result: Array[String] = []
	for token in value.replace("\n", ",").split(",", false):
		var id := token.strip_edges()
		if not id.is_empty() and id not in result: result.append(id)
	return result
