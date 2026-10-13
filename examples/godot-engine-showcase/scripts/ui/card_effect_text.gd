extends RichTextLabel
var visual_context
## Typed text segments keep numeric comparisons separate from localized prose.
var rows: Array = []
var numeric_colors: Array[Color] = []
var visual_style: Resource
var _configuration_key := ""

func reconcile_from(other) -> void:
	visual_context = other.visual_context
	custom_minimum_size = other.custom_minimum_size
	size_flags_vertical = other.size_flags_vertical
	configure(other.rows, other.visual_style)
	scroll_active = other.scroll_active
	mouse_filter = other.mouse_filter

func configure(effect_rows: Array, style: Resource) -> void:
	var key: String = JSON.stringify(effect_rows) + style.presentation_key() + visual_context.presentation_key()
	if key == _configuration_key: return
	_configuration_key = key
	# Incoming text is canonical/unscaled; preferences are applied by CardView.
	if has_meta("visual_preferences_revision"): remove_meta("visual_preferences_revision")
	for meta in get_meta_list():
		if str(meta).begins_with("base_"): remove_meta(meta)
	rows = effect_rows.duplicate(true)
	visual_style = style
	bbcode_enabled = false
	selection_enabled = true
	meta_underlined = false
	add_theme_color_override("default_color", style.effect_ink if style.printed_face else AppTheme.INK)
	add_theme_font_size_override("normal_font_size", style.rules_font_size)
	add_theme_font_size_override("bold_font_size", style.rules_font_size)
	# Preserve the established font inspection contract, alongside the actual rich font.
	add_theme_font_size_override("font_size", style.rules_font_size)
	clear()
	numeric_colors.clear()
	var comparisons: Array[String] = []
	for i in rows.size():
		if i > 0: add_text("\n")
		for segment in rows[i].get("segments", []):
			if segment.has("value"):
				var color: Color = style.value_color(segment.value, segment.get("baseValue"))
				numeric_colors.append(color)
				push_color(color)
				push_bold()
				add_text(str(segment.get("text", "")))
				pop()
				pop()
				if (segment.get("baseValue") is float or segment.get("baseValue") is int) and (segment.value is float or segment.value is int):
					comparisons.append(visual_context.translator.text("Base: %s → Current: %s") % [visual_context.translator.number(float(segment.baseValue)), visual_context.translator.number(float(segment.value))])
			else:
				# add_text is literal: content cannot inject BBCode or style other numbers.
				add_text(str(segment.get("text", "")))
	tooltip_text = "\n".join(comparisons)

static func plain_rows(lines: Array) -> Array:
	var result: Array = []
	for line in lines: result.append({"segments": [{"text": str(line)}]})
	return result

static func plain_text(effect_rows: Array) -> String:
	var lines: Array[String] = []
	for row in effect_rows:
		var line := ""
		for segment in row.get("segments", []): line += str(segment.get("text", ""))
		lines.append(line)
	return "\n".join(lines)


func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
