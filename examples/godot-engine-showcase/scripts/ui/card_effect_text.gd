extends RichTextLabel
## Typed text segments keep numeric comparisons separate from localized prose.
var rows: Array = []
var numeric_colors: Array[Color] = []
var visual_style: Resource

func configure(effect_rows: Array, style: Resource) -> void:
	rows = effect_rows.duplicate(true)
	visual_style = style
	bbcode_enabled = false
	selection_enabled = true
	meta_underlined = false
	add_theme_color_override("default_color", AppTheme.INK)
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
					comparisons.append(I18n.text("Base: %s → Current: %s") % [I18n.number(float(segment.baseValue)), I18n.number(float(segment.value))])
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
