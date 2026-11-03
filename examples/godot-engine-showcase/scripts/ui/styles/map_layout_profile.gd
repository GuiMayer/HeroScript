extends Resource
## Visual geometry and camera timing; availability comes exclusively from engine choices.
@export var node_size := Vector2(112, 104)
@export var hit_size := Vector2(72, 64)
@export var symbol_size := 42.0
@export var caption_height := 38.0
@export var caption_gap := 2.0
@export var node_font_size := 14
@export var compact_auxiliary := true
@export var auxiliary_kinds := PackedStringArray(["CardSelection", "RelicSelection", "CardUpgrade", "InscriptionSelection"])
@export var auxiliary_symbol_size := 30.0
@export var padding := Vector2(24, 20)
@export var lane_gap := 16.0
@export var layer_gap := 32.0
@export var maximum_panel_width := 620.0
@export var panel_width_ratio := .4
@export var inspector_compact_breakpoint := 1000.0
@export var inspector_min_width := 280.0
@export var inspector_max_width := 320.0
@export var inspector_width_ratio := 0.26
@export var inspector_compact_height := 202.0
@export var inspector_compact_bottom_offset := 210.0
@export var inspector_side_min_height := 120.0
@export var inspector_side_max_height := 420.0
@export var inspector_toggle_size := Vector2(44, 44)
@export var inspector_margin := 8.0
@export_range(0.0, 1.0, .01) var camera_transition_seconds := .28
func presentation_key() -> String:
	return JSON.stringify([node_size, hit_size, symbol_size, caption_height, caption_gap, node_font_size, padding, lane_gap, layer_gap, inspector_compact_breakpoint, inspector_min_width, inspector_max_width, inspector_width_ratio, inspector_compact_height, inspector_compact_bottom_offset, inspector_side_min_height, inspector_side_max_height, inspector_toggle_size, inspector_margin])
