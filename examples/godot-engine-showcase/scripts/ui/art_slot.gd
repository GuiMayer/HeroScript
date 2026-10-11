extends TextureRect
var visual_context
## Replace art through the manifest, retaining anchors and interaction surfaces.
const Catalog = preload("res://scripts/presentation/art_catalog.gd")
var slot_key := ""
var is_placeholder := true

func setup(category: String, content_id: String) -> void:
	var slot: Dictionary = visual_context.resolve_art(category, content_id)
	slot_key = str(slot.get("key", ""))
	is_placeholder = bool(slot.get("placeholder", true))
	texture = Catalog.texture_for(slot)
	expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED if slot.get("fit") == "cover" else TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_meta("art_slot", slot_key)

func flash_hit() -> void:
	modulate = Color(1.8, 1.4, 1.2)
	create_tween().tween_property(self, "modulate", Color.WHITE, .23 / maxf(visual_context.animation_speed, .25))


func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
