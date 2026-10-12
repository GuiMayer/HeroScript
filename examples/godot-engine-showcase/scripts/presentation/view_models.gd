extends RefCounted
## Read-only projection boundary. Data is never exposed by reference.
class Record extends RefCounted:
	var _data: Dictionary
	var id: String:
		get: return str(_data.get("id", ""))
	func _init(data: Dictionary) -> void: _data = data.duplicate(true)
	func to_dictionary() -> Dictionary: return _data.duplicate(true)
	func read(key: String, fallback = null):
		var value = _data.get(key, fallback)
		return value.duplicate(true) if value is Dictionary or value is Array else value

class Card extends Record:
	var instance_id: String:
		get: return str(_data.get("cardInstanceId", ""))
class Actor extends Record:
	var instance_id: String:
		get: return str(_data.get("instanceId", ""))
	var relationship: String:
		get: return str(_data.get("relationship", "Neutral"))
	var side_id: String:
		get: return str(_data.get("sideId", ""))
class ResourceValue extends Record:
	var current: float:
		get: return float(_data.get("current", 0))
	var has_bounds: bool:
		get: return _data.has("minimum") and _data.has("maximum") and float(_data.maximum) > float(_data.minimum)
class ActionOption extends Record:
	var reference: String:
		get: return str(_data.get("reference", ""))
class Combat extends Record:
	var context: String:
		get: return str(_data.get("context", ""))
class Header extends Record: pass
class Hud extends Record: pass

class Map extends Record: pass
class Activity extends Record: pass
class Offer extends Record: pass
class Scenery extends Record: pass
