extends RefCounted
## Cosmetic mappings only. Unknown content remains visible and gets a neutral sigil.
const OFFENSE = ["attack", "damage", "critical", "precision", "fire", "poison", "residual", "multihit"]
const DEFENSE = ["defense", "armor", "block", "health", "dodge", "survival"]
const ICONS = {"critical": "precision", "precision": "precision", "attack": "strike", "damage": "strike", "fire": "fire", "poison": "poison", "residual": "condense", "defense": "guard", "armor": "guard", "block": "guard", "health": "guard", "energy": "energy", "multihit": "multihit"}
static func matches(item: Dictionary, filter: String) -> bool:
	if filter == "All": return true
	var tags: Array = item.get("tags", [])
	var offense := tags.any(func(tag): return tag in OFFENSE)
	var defense := tags.any(func(tag): return tag in DEFENSE)
	return offense if filter == "Offense" else (defense if filter == "Defense" else not offense and not defense)
static func texture(item: Dictionary) -> Texture2D:
	var authored := str(item.get("icon", ""))
	if authored.begins_with("res://") and ResourceLoader.exists(authored): return load(authored)
	for tag in item.get("tags", []):
		if ICONS.has(tag): return load("res://assets/programmer-art/icon-%s.svg" % ICONS[tag])
	return preload("res://assets/programmer-art/icon-inscription.svg")
