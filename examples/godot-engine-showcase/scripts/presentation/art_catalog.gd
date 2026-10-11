extends RefCounted
## Stable visual slots only. No rules, REST calls or mutable run state.
const MANIFEST := "res://data/art_manifest.json"
static var _manifests: Dictionary = {}
static var _textures: Dictionary = {}

static func invalidate(path := MANIFEST) -> void:
	_manifests.erase(path)
static func manifest(path := MANIFEST) -> Dictionary:
	if not _manifests.has(path):
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(path)) if FileAccess.file_exists(path) else {}
		_manifests[path] = parsed if parsed is Dictionary else {}
	return _manifests[path].duplicate(true)

static func resolve(category: String, content_id: String, manifest_path := MANIFEST) -> Dictionary:
	var data := manifest(manifest_path)
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
