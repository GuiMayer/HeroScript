@tool
class_name StatusBadge
extends PanelContainer

const ContextType = preload("res://scripts/ui/visual_context.gd")

var visual_context: ContextType
var model: Dictionary = {}

@export var preview_status: Dictionary = {"statusId": "burn", "stacks": 4, "duration": 2}
var icon_rect: TextureRect
var glyph_label: Label
var stack_label: Label
var _panel_style: StyleBoxFlat

func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint() and model.is_empty():
		apply_status(preview_status)
	elif not model.is_empty():
		_apply_model()

func _bind_nodes() -> void:
	icon_rect = get_node("%IconRect")
	glyph_label = get_node("%GlyphLabel")
	stack_label = get_node("%StackLabel")
	if _panel_style == null:
		_panel_style = get_theme_stylebox("panel").duplicate()
		add_theme_stylebox_override("panel", _panel_style)

func presentation_model() -> Dictionary:
	return model.duplicate(true)

func reconcile_from(other) -> void:
	visual_context = other.visual_context
	apply_status(other.presentation_model(), visual_context)

func apply_status(status_data: Dictionary, context = null) -> void:
	model = status_data.duplicate(true)
	if context != null:
		visual_context = context
	_bind_nodes()
	_apply_model()

func _apply_model() -> void:
	var status_id := str(model.get("statusId", "status"))
	var stacks: float = float(model.get("stacks", 1))
	var duration: int = int(model.get("duration", 0))
	var tone := _status_tone(status_id)

	# Preserve the scene's editable border widths, padding and corner radii.
	_panel_style.bg_color = tone.bg
	_panel_style.border_color = tone.border

	if icon_rect == null or glyph_label == null or stack_label == null:
		return

	if tone.icon != null:
		icon_rect.texture = tone.icon
		icon_rect.modulate = tone.accent
		icon_rect.visible = true
		glyph_label.visible = false
	else:
		glyph_label.text = tone.glyph
		glyph_label.add_theme_color_override("font_color", tone.accent)
		glyph_label.visible = true
		icon_rect.visible = false

	if stacks > 1:
		var count_str := str(roundi(stacks))
		if visual_context != null and visual_context.translator != null:
			count_str = visual_context.translator.number(stacks)
		stack_label.text = count_str
		stack_label.visible = true
	else:
		stack_label.visible = false

	# Rich Tooltip
	var title_text := status_id.capitalize()
	if visual_context != null and visual_context.translator != null:
		title_text = visual_context.translator.content_name(status_id)

	var tooltip_lines: Array[String] = []
	if stacks > 1:
		tooltip_lines.append("%s (×%s)" % [title_text, stack_label.text])
	else:
		tooltip_lines.append(title_text)

	if duration > 0:
		var dur_text := "Duration: %s turns" % duration
		if visual_context != null and visual_context.translator != null:
			dur_text = visual_context.translator.text("Duration: %s turns") % duration
		tooltip_lines.append(dur_text)

	var desc := str(model.get("description", ""))
	if not desc.is_empty():
		tooltip_lines.append(desc)

	tooltip_text = "\n".join(tooltip_lines)

func _status_tone(status_id: String) -> Dictionary:
	var sid := status_id.to_lower()
	var border := Color(0.47, 0.56, 0.61)
	var bg := Color(0.10, 0.14, 0.16, 0.92)
	var accent := Color(0.75, 0.82, 0.85)
	var icon_path := ""
	var glyph := sid.substr(0, mini(3, sid.length())).to_upper()

	if sid.contains("burn") or sid.contains("fire") or sid.contains("flame") or sid.contains("heat") or sid.contains("calor") or sid.contains("ignit"):
		border = Color(1.0, 0.40, 0.10)
		bg = Color(0.22, 0.08, 0.02, 0.94)
		accent = Color(1.0, 0.70, 0.25)
		icon_path = "res://assets/ivory/flame.svg"
		glyph = "BRN"
	elif sid.contains("wet") or sid.contains("water") or sid.contains("molhad") or sid.contains("frost") or sid.contains("chill") or sid.contains("ice") or sid.contains("freeze") or sid.contains("congel"):
		border = Color(0.16, 0.75, 0.98)
		bg = Color(0.03, 0.15, 0.22, 0.94)
		accent = Color(0.55, 0.88, 1.0)
		glyph = "WET"
	elif sid.contains("poison") or sid.contains("toxic") or sid.contains("venom") or sid.contains("acid") or sid.contains("blight") or sid.contains("venen"):
		border = Color(0.38, 0.78, 0.40)
		bg = Color(0.05, 0.18, 0.07, 0.94)
		accent = Color(0.65, 0.92, 0.65)
		icon_path = "res://assets/ivory/poison.svg"
		glyph = "PSN"
	elif sid.contains("bleed") or sid.contains("blood") or sid.contains("hemorrhag") or sid.contains("sangu"):
		border = Color(0.92, 0.25, 0.25)
		bg = Color(0.24, 0.04, 0.04, 0.94)
		accent = Color(1.0, 0.55, 0.55)
		glyph = "BLD"
	elif sid.contains("shield") or sid.contains("guard") or sid.contains("armor") or sid.contains("barr") or sid.contains("block") or sid.contains("escud"):
		border = Color(1.0, 0.78, 0.18)
		bg = Color(0.22, 0.17, 0.03, 0.94)
		accent = Color(1.0, 0.90, 0.55)
		icon_path = "res://assets/ivory/guard.svg"
		glyph = "SHD"
	elif sid.contains("vuln") or sid.contains("fragil"):
		border = Color(0.75, 0.35, 0.85)
		bg = Color(0.18, 0.06, 0.22, 0.94)
		accent = Color(0.88, 0.62, 0.95)
		glyph = "VUL"
	elif sid.contains("weak") or sid.contains("fraq"):
		border = Color(0.60, 0.40, 0.75)
		bg = Color(0.15, 0.07, 0.20, 0.94)
		accent = Color(0.80, 0.65, 0.90)
		glyph = "WEK"
	elif sid.contains("strength") or sid.contains("power") or sid.contains("fury") or sid.contains("rage") or sid.contains("forc"):
		border = Color(1.0, 0.48, 0.28)
		bg = Color(0.24, 0.08, 0.05, 0.94)
		accent = Color(1.0, 0.72, 0.60)
		glyph = "STR"
	elif sid.contains("focus") or sid.contains("precis"):
		border = Color(0.20, 0.80, 0.85)
		bg = Color(0.04, 0.18, 0.19, 0.94)
		accent = Color(0.55, 0.90, 0.95)
		icon_path = "res://assets/ivory/precision.svg"
		glyph = "FOC"
	elif sid.contains("condens"):
		border = Color(1.0, 0.55, 0.15)
		bg = Color(0.22, 0.10, 0.03, 0.94)
		accent = Color(1.0, 0.80, 0.40)
		icon_path = "res://assets/ivory/condense.svg"
		glyph = "CND"
	elif sid.contains("spark") or sid.contains("shock") or sid.contains("elet"):
		border = Color(1.0, 0.88, 0.25)
		bg = Color(0.22, 0.19, 0.04, 0.94)
		accent = Color(1.0, 0.95, 0.60)
		icon_path = "res://assets/ivory/spark.svg"
		glyph = "SPK"

	var icon_tex: Texture2D = null
	if not icon_path.is_empty() and ResourceLoader.exists(icon_path):
		icon_tex = load(icon_path)

	return {"border": border, "bg": bg, "accent": accent, "icon": icon_tex, "glyph": glyph}
