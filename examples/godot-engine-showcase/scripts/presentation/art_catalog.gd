extends RefCounted
## Stable visual slots only. No rules, REST calls or mutable run state.
const MANIFEST := "res://data/art_manifest.json"
static var _manifests: Dictionary = {}
## Cosmetic only: one choice per visual slot for the lifetime of this process.
## Never use the engine's RNG or store this selection in authoritative snapshots.
static var _variant_choices: Dictionary = {}
static var _cosmetic_rng: RandomNumberGenerator

static func clear() -> void:
	_manifests.clear()

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
	var variants: Array = slot.get("variants", [])
	var available := variants.filter(func(path): return path is String and ResourceLoader.exists(path))
	if not available.is_empty():
		var choice_key := manifest_path + "::" + key
		if not _variant_choices.has(choice_key) or _variant_choices[choice_key] not in available:
			if _cosmetic_rng == null:
				_cosmetic_rng = RandomNumberGenerator.new()
				_cosmetic_rng.randomize()
			_variant_choices[choice_key] = available[_cosmetic_rng.randi_range(0, available.size() - 1)]
		slot.path = _variant_choices[choice_key]
	if not ResourceLoader.exists(str(slot.get("path", ""))):
		key = fallback
		slot = data.get("slots", {}).get(key, {}).duplicate(true)
	slot["key"] = key
	return slot

static func texture_for(slot: Dictionary) -> Texture2D:
	var path := str(slot.get("path", ""))
	if path.is_empty() or not ResourceLoader.exists(path): return null
	# ResourceLoader already reuses live textures by path. A second strong static
	# cache keeps GPU resources alive after the last view has gone away.
	return load(path) as Texture2D
