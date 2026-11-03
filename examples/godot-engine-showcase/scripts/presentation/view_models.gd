extends RefCounted
## Read-only projection boundary. Data is never exposed by reference.

class Record extends RefCounted:
	var _data: Dictionary
	var id: String:
		get: return str(_data.get("id", ""))
	func _init(data = {}) -> void:
		_data = data.duplicate(true) if data is Dictionary else {}
	func to_dictionary() -> Dictionary:
		return _data.duplicate(true)
	func read(key: String, fallback = null):
		var value = _data.get(key, fallback)
		return value.duplicate(true) if value is Dictionary or value is Array else value
	func has_field(key: String) -> bool:
		return _data.has(key) and _data[key] != null
	func extensions(known_keys: Array[String]) -> Dictionary:
		var extra := {}
		for key in _data.keys():
			if str(key) not in known_keys:
				var val = _data[key]
				extra[key] = val.duplicate(true) if val is Dictionary or val is Array else val
		return extra

class SessionIdentity extends Record:
	const KNOWN_KEYS: Array[String] = ["runId", "branchId", "runSequence", "sequence", "combatId", "combatStep", "step", "contentRevision", "settingId"]
	var run_id: String:
		get: return str(_data.get("runId", "")) if _data.get("runId") is String else ""
	var branch_id: String:
		get: return str(_data.get("branchId", "")) if _data.get("branchId") is String else ""
	var run_sequence: int:
		get:
			var raw = _data.get("runSequence", _data.get("sequence", -1))
			return int(raw) if (raw is int or raw is float) else -1
	var combat_id: String:
		get: return str(_data.get("combatId", "")) if _data.get("combatId") is String else ""
	var combat_step: int:
		get:
			var raw = _data.get("combatStep", _data.get("step", 0))
			return int(raw) if (raw is int or raw is float) else -1
	var content_revision: String:
		get: return str(_data.get("contentRevision", "")) if _data.get("contentRevision") is String else ""
	var setting_id: String:
		get: return str(_data.get("settingId", "")) if _data.get("settingId") is String else ""
	func is_valid_run() -> bool:
		return not run_id.is_empty() and run_sequence >= 0
	func is_valid_combat() -> bool:
		return is_valid_run() and not combat_id.is_empty() and combat_step >= 0
	func context_key() -> String:
		return JSON.stringify([run_id, branch_id, maxi(0, run_sequence), combat_id, maxi(0, combat_step)])
	func matches_version(version_dto: Dictionary) -> bool:
		if not (version_dto is Dictionary):
			return false
		if version_dto.has("runId") and not run_id.is_empty():
			if not (version_dto.get("runId") is String) or str(version_dto.get("runId")) != run_id:
				return false
		if version_dto.has("runSequence") or version_dto.has("sequence"):
			var incoming_seq = version_dto.get("runSequence", version_dto.get("sequence"))
			if not (incoming_seq is int or incoming_seq is float) or int(incoming_seq) != run_sequence:
				return false
		if version_dto.has("combatId") and not combat_id.is_empty():
			if not (version_dto.get("combatId") is String) or str(version_dto.get("combatId")) != combat_id:
				return false
		if version_dto.has("combatStep") and not combat_id.is_empty():
			var incoming_step = version_dto.get("combatStep")
			if not (incoming_step is int or incoming_step is float) or int(incoming_step) != combat_step:
				return false
		if version_dto.has("contentRevision") and not content_revision.is_empty():
			if not (version_dto.get("contentRevision") is String) or str(version_dto.get("contentRevision")) != content_revision:
				return false
		return true
	static func from_snapshots(run_dto: Dictionary, combat_dto: Dictionary = {}, fallback_revision := "", fallback_setting := "") -> SessionIdentity:
		var r := run_dto if run_dto is Dictionary else {}
		var c := combat_dto if combat_dto is Dictionary else {}
		return SessionIdentity.new({
			"runId": r.get("runId", c.get("runId", "")),
			"branchId": r.get("branchId", ""),
			"runSequence": r.get("sequence", r.get("runSequence", 0 if r.is_empty() else -1)),
			"combatId": c.get("combatId", ""),
			"combatStep": c.get("step", c.get("combatStep", 0)),
			"contentRevision": r.get("contentRevision", fallback_revision),
			"settingId": r.get("settingId", r.get("configName", fallback_setting))
		})

class Card extends Record:
	const KNOWN_KEYS: Array[String] = ["cardInstanceId", "definitionId", "name", "costs", "upgrades", "tags", "effects", "requirements"]
	var instance_id: String:
		get: return str(_data.get("cardInstanceId", "")) if _data.get("cardInstanceId") is String else ""
	var definition_id: String:
		get: return str(_data.get("definitionId", "")) if _data.get("definitionId") is String else ""
	var costs: Array:
		get: return _data.get("costs", []).duplicate(true) if _data.get("costs") is Array else []
	var upgrades: Array:
		get: return _data.get("upgrades", []).duplicate(true) if _data.get("upgrades") is Array else []
	var tags: Array:
		get: return _data.get("tags", []).duplicate(true) if _data.get("tags") is Array else []
	func is_valid() -> bool:
		return not instance_id.is_empty()

class Actor extends Record:
	const KNOWN_KEYS: Array[String] = ["instanceId", "definitionId", "sideId", "relationship", "resources", "statuses", "name"]
	var instance_id: String:
		get: return str(_data.get("instanceId", "")) if _data.get("instanceId") is String else ""
	var definition_id: String:
		get: return str(_data.get("definitionId", "")) if _data.get("definitionId") is String else ""
	var relationship: String:
		get: return str(_data.get("relationship", "Neutral")) if _data.get("relationship", "Neutral") is String else "Neutral"
	var side_id: String:
		get: return str(_data.get("sideId", "")) if _data.get("sideId") is String else ""
	var resources: Dictionary:
		get: return _data.get("resources", {}).duplicate(true) if _data.get("resources") is Dictionary else {}
	var statuses: Array:
		get: return _data.get("statuses", []).duplicate(true) if _data.get("statuses") is Array else []
	func is_valid() -> bool:
		return not instance_id.is_empty()

class ResourceValue extends Record:
	var current: float:
		get:
			var raw = _data.get("current", 0)
			return float(raw) if (raw is int or raw is float) else 0.0
	var has_bounds: bool:
		get:
			var min_v = _data.get("minimum")
			var max_v = _data.get("maximum")
			return (min_v is int or min_v is float) and (max_v is int or max_v is float) and float(max_v) > float(min_v)

class LegalAction extends Record:
	## Typed presentation model for a legal action candidate. Never invents synthetic command IDs or types.
	var command: Dictionary:
		get: return _data.get("command", {}).duplicate(true) if _data.get("command") is Dictionary else {}
	var action_type: String:
		get:
			var cmd := command
			var raw = cmd.get("actionType", cmd.get("type", _data.get("type", "")))
			return str(raw) if raw is String else ""
	var card_instance_id: String:
		get:
			var cmd := command
			var raw = cmd.get("cardInstanceId", "")
			return str(raw) if raw is String else ""
	var source: String:
		get:
			var raw = _data.get("source", "")
			return str(raw) if raw is String else ""
	var target_ids: Array:
		get:
			var cmd := command
			var result: Array = []
			if cmd.get("targetIds") is Array:
				for t in cmd.get("targetIds"):
					if t is String and not str(t).is_empty():
						result.append(str(t))
			elif cmd.get("targetId") is String and not str(cmd.get("targetId")).is_empty():
				result.append(str(cmd.get("targetId")))
			return result
	var costs: Array:
		get: return _data.get("costs", []).duplicate(true) if _data.get("costs") is Array else []
	var applications: Array:
		get: return _data.get("applications", []).duplicate(true) if _data.get("applications") is Array else []
	func is_valid() -> bool:
		if not (_data.get("command") is Dictionary):
			return false
		var cmd: Dictionary = _data.get("command")
		if cmd.has("cardInstanceId") and cmd.get("cardInstanceId") != null and not (cmd.get("cardInstanceId") is String):
			return false
		if cmd.has("actionType") and cmd.get("actionType") != null and not (cmd.get("actionType") is String):
			return false
		return not card_instance_id.is_empty() or not action_type.is_empty()

class ActionOption extends Record:
	var reference: String:
		get: return str(_data.get("reference", "")) if _data.get("reference") is String else ""
	var candidate: Dictionary:
		get: return _data.get("candidate", {}).duplicate(true) if _data.get("candidate") is Dictionary else {}

class Zone extends Record:
	var zone_id: String:
		get: return str(_data.get("zoneId", "")) if _data.get("zoneId") is String else ""
	var count: int:
		get:
			var raw = _data.get("count", 0)
			return maxi(0, int(raw)) if (raw is int or raw is float) else -1
	var contents_visible: bool:
		get: return bool(_data.get("contentsVisible", false)) if _data.get("contentsVisible", false) is bool else false
	var allows_card_play: bool:
		get:
			var raw = _data.get("playerCanPlayFrom", _data.get("allowsCardPlay", false))
			return bool(raw) if raw is bool else false
	var cards: Array:
		get: return _data.get("cards", []).duplicate(true) if _data.get("cards") is Array else []
	func is_valid() -> bool:
		return not zone_id.is_empty() and count >= 0

class InteractionState extends Record:
	var selected_card_id: String:
		get: return str(_data.get("selectedCardId", "")) if _data.get("selectedCardId") is String else ""
	var hovered_card_id: String:
		get: return str(_data.get("hoveredCardId", "")) if _data.get("hoveredCardId") is String else ""
	var selected_target_id: String:
		get: return str(_data.get("selectedTargetId", "")) if _data.get("selectedTargetId") is String else ""
	var submitting: bool:
		get: return bool(_data.get("submitting", false))
	var scope_key: String:
		get: return str(_data.get("scopeKey", "")) if _data.get("scopeKey") is String else ""

class Combat extends Record:
	var context: String:
		get: return str(_data.get("context", ""))
class Header extends Record: pass
class Hud extends Record: pass

class Map extends Record: pass
class Activity extends Record: pass
class Offer extends Record: pass
class Scenery extends Record: pass
