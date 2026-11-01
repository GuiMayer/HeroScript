@tool
extends Control
## Fixed title slot. Native shaping chooses one fitted line, then at most two.
var visual_context
var label: Label
var maximum := 17
var minimum := 12
var two_line_minimum := 8
var fitting := false
func configure(value: String, context, policy: CardVisualStyle) -> void:
	name = "CardTitle"
	visual_context = context
	maximum = maxi(1, roundi(policy.title_font_size * context.text_scale))
	minimum = clampi(roundi(policy.title_min_font_size * context.text_scale), 1, maximum)
	two_line_minimum = clampi(roundi(policy.title_min_two_line_font_size * context.text_scale), 1, minimum)
	custom_minimum_size.y = ceilf(policy.title_line_height * context.text_scale)
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	clip_contents = true
	label = get_node("CardName")
	label.text = value.replace("\n", " ").replace("\r", " ")
	label.add_theme_color_override("font_color", context.styles.INK)
	if policy.printed_face and context.profile.title_font: label.add_theme_font_override("font", context.profile.title_font)
	_fit()
func _ready() -> void:
	label = get_node("CardName")
	resized.connect(_fit)
	label.theme_changed.connect(_fit)
	_fit()
func reconcile_from(other) -> void:
	visual_context = other.visual_context
	maximum = other.maximum; minimum = other.minimum
	two_line_minimum = other.two_line_minimum
	preload("res://scripts/ui/view_reconciler.gd").patch_contents(self, other)
	label = get_child(0)
	_fit()
func _fits(font: Font, point_size: int, lines: int) -> bool:
	if lines == 1:
		return font.get_string_size(label.text, HORIZONTAL_ALIGNMENT_LEFT, -1, point_size).x <= size.x and font.get_height(point_size) <= size.y
	var paragraph := TextParagraph.new()
	paragraph.width = size.x
	paragraph.break_flags = TextServer.BREAK_MANDATORY | TextServer.BREAK_WORD_BOUND | TextServer.BREAK_ADAPTIVE
	paragraph.add_string(label.text, font, point_size)
	var count := paragraph.get_line_count()
	return count <= 2 and paragraph.get_size().y + maxi(0, count - 1) * label.get_theme_constant("line_spacing") <= size.y
func _fit() -> void:
	if visual_context == null or fitting or not is_inside_tree() or size.x <= 0 or size.y <= 0: return
	fitting = true
	var font := label.get_theme_font("font")
	var chosen := two_line_minimum
	var lines := 2
	for allowed in [1, 2]:
		var found := false
		var floor_size := minimum if allowed == 1 else two_line_minimum
		for candidate in range(maximum, floor_size - 1, -1):
			if _fits(font, candidate, allowed):
				chosen = candidate; lines = allowed; found = true
				break
		if found: break
	label.max_lines_visible = lines
	label.autowrap_mode = TextServer.AUTOWRAP_OFF if lines == 1 else TextServer.AUTOWRAP_WORD_SMART
	# Preference restyling must not multiply a previously fitted font a second time.
	label.set_meta("base_font_size", float(chosen) / visual_context.text_scale)
	if label.get_theme_font_size("font_size") != chosen: label.add_theme_font_size_override("font_size", chosen)
	fitting = false
