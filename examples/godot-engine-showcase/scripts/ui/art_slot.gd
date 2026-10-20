extends TextureRect
var visual_context
## Replace art through the manifest, retaining anchors and interaction surfaces.
const Catalog = preload("res://scripts/presentation/art_catalog.gd")
var _model: Dictionary = {}
var model: Dictionary:
	get: return _model.duplicate(true)
var slot_key := ""
var is_placeholder := true
var _hit_tween: Tween
var art_effects

func effect_player():
	if art_effects == null: art_effects = preload("res://scripts/ui/art_effect_player.gd").new(self)
	return art_effects

func play_art_effect(effect: String, seconds: float, strength := .7, instant := false) -> Tween:
	return effect_player().play(effect, seconds, strength, visual_context.reduced_motion, instant)

func setup(category: String, content_id: String) -> void:
	apply_model({"category": category, "contentId": content_id})

func apply_model(data: Dictionary) -> void:
	if _model != data and art_effects != null: art_effects.clear()
	_model = data.duplicate(true)
	var slot: Dictionary = visual_context.resolve_art(str(_model.get("category", "")), str(_model.get("contentId", "")))
	slot_key = str(slot.get("key", ""))
	is_placeholder = bool(slot.get("placeholder", true))
	texture = Catalog.texture_for(slot)
	expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED if slot.get("fit") == "cover" else TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_meta("art_slot", slot_key)

func reconcile_from(other) -> void:
	rebind_context(other.visual_context)
	custom_minimum_size = other.custom_minimum_size
	size_flags_horizontal = other.size_flags_horizontal
	size_flags_vertical = other.size_flags_vertical
	apply_model(other.model)

func rebind_context(context) -> void:
	if visual_context == context: return
	dispose()
	visual_context = context
	if is_inside_tree(): _subscribe_context()
	_on_context_changed()

func flash_hit() -> Tween:
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	if policy.duration("impact", true) <= 0: return null
	if _hit_tween: _hit_tween.kill()
	modulate = Color(1.8, 1.4, 1.2)
	_hit_tween = create_tween()
	_hit_tween.tween_property(self, "modulate", Color.WHITE, policy.duration("impact", true))
	return _hit_tween
func settle_motion() -> void:
	if art_effects != null: art_effects.clear()
	if _hit_tween != null and _hit_tween.is_valid(): _hit_tween.kill()
	modulate = Color.WHITE


func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()


func _enter_tree() -> void:
	_subscribe_context()
func _subscribe_context() -> void:
	if not visual_context.changed.is_connected(_on_context_changed): visual_context.changed.connect(_on_context_changed)
func _on_context_changed() -> void:
	if not _model.is_empty(): apply_model(_model)

func dispose() -> void:
	if art_effects != null: art_effects.clear()
	if visual_context.changed.is_connected(_on_context_changed): visual_context.changed.disconnect(_on_context_changed)
	if _hit_tween: _hit_tween.kill()
	_hit_tween = null

func _exit_tree() -> void: dispose()
