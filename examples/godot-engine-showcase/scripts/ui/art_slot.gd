extends TextureRect
## Replace art through the manifest, retaining anchors and interaction surfaces.
const Catalog = preload("res://scripts/presentation/art_catalog.gd")
var slot_key := ""

func setup(category: String, content_id: String) -> void:
	var slot := Catalog.resolve(category, content_id)
	slot_key = str(slot.get("key", ""))
	texture = Catalog.texture_for(slot)
	expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED if slot.get("fit") == "cover" else TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_meta("art_slot", slot_key)

func flash_hit() -> void:
	modulate = Color(1.8, 1.4, 1.2)
	create_tween().tween_property(self, "modulate", Color.WHITE, .23 / maxf(Preferences.animation_speed, .25))
