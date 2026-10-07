class_name CardVisualStyle
extends Resource
## Shared visual policy. Never encodes resource semantics, game rules or scaling.
@export_group("Rarity")
@export var rarity_colors: Dictionary = {
	"common": Color("#c9cbd3"), "uncommon": Color("#79d58c"),
	"rare": Color("#62a7f1"), "legendary": Color("#e7b75d")
}
@export var unknown_rarity_color := Color("#969aaa")
@export_group("Effect values")
@export var base_value_color := Color.WHITE
@export var lower_value_color := Color("#ff7373")
@export var higher_value_color := Color("#72b7ff")
@export_group("Layout")
@export var card_size := Vector2(248, 328)
@export var text_scale_height_margin := 120.0
@export var rules_font_size := 14
@export var art_height := 72.0
@export_group("Tooltip summary")
@export var tooltip_width := 410.0
@export_range(1, 8) var tooltip_effect_rows := 3
@export_range(1, 12) var tooltip_effect_lines := 6
@export_range(1, 3) var tooltip_label_lines := 2
@export_group("Inspection")
@export var inspection_height := 380.0
@export_group("Changes")
@export var visible_change_badges := 2

func presentation_key() -> String:
	# In-place editor/runtime changes must invalidate an otherwise identical face.
	var values := {}
	for property in get_property_list():
		if int(property.usage) & PROPERTY_USAGE_SCRIPT_VARIABLE:
			values[str(property.name)] = get(property.name)
	return JSON.stringify(values)

func effective_size(text_scale: float) -> Vector2:
	return card_size + Vector2(0, maxf(0, text_scale - 1.0) * text_scale_height_margin)

func rarity_color(id: String) -> Color:
	return rarity_colors.get(id.to_lower(), unknown_rarity_color)

func value_color(current, baseline) -> Color:
	# Missing comparison facts stay neutral; never guess from a buff or from text.
	if not (current is float or current is int) or not (baseline is float or baseline is int): return base_value_color
	if not is_finite(float(current)) or not is_finite(float(baseline)): return base_value_color
	if is_equal_approx(float(current), float(baseline)): return base_value_color
	return lower_value_color if float(current) < float(baseline) else higher_value_color
