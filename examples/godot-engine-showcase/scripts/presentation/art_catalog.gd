extends RefCounted
## Stable visual slots only. No rules, REST calls or mutable run state.
const MANIFEST := "res://data/art_manifest.json"
static var _manifest: Dictionary = {}
static var _textures: Dictionary = {}

static func manifest() -> Dictionary:
	if _manifest.is_empty() and FileAccess.file_exists(MANIFEST):
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(MANIFEST))
		if parsed is Dictionary: _manifest = parsed
	return _manifest.duplicate(true)

static func resolve(category: String, content_id: String) -> Dictionary:
	var data := manifest()
	var fallback := str(data.get("defaults", {}).get(category, "card_unknown"))
	var key := str(data.get("categories", {}).get(category, {}).get(content_id, fallback))
	var slot: Dictionary = data.get("slots", {}).get(key, {}).duplicate(true)
	if not ResourceLoader.exists(str(slot.get("path", ""))):
		key = fallback
		slot = data.get("slots", {}).get(key, {}).duplicate(true)
	slot["key"] = key
	return slot

static func texture_for(slot: Dictionary) -> Texture2D:
	var path := str(slot.get("path", ""))
	if path.is_empty() or not ResourceLoader.exists(path): return null
	if not _textures.has(path): _textures[path] = load(path) as Texture2D
	return _textures[path]
