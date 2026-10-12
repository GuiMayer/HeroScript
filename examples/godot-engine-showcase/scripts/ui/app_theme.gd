class_name AppTheme
extends RefCounted
## Composition facade. All construction is implemented by the context-bound factory.
const Factory = preload("res://scripts/ui/styles/view_style_factory.gd")
const MIN_READABLE_TEXT_WIDTH = Factory.MIN_READABLE_TEXT_WIDTH
static var _styles = Factory.new()
static var INK: Color:
	get: return _styles.INK
static var MUTED: Color:
	get: return _styles.MUTED
static var EMBER: Color:
	get: return _styles.EMBER
static var GOLD: Color:
	get: return _styles.GOLD
static var TEAL: Color:
	get: return _styles.TEAL
static var BLOOD: Color:
	get: return _styles.BLOOD
static var NIGHT: Color:
	get: return _styles.NIGHT
static var PANEL: Color:
	get: return _styles.PANEL
static var PANEL_LIGHT: Color:
	get: return _styles.PANEL_LIGHT
static var profile: Resource:
	get: return _styles.profile
	set(value): _styles.profile = value
static var generation: int:
	get: return _styles.generation
	set(value): _styles.generation = value
static var text_scale: float:
	get: return _styles.text_scale
	set(value): _styles.text_scale = value
static var high_contrast_enabled: bool:
	get: return _styles.high_contrast_enabled
	set(value): _styles.high_contrast_enabled = value

static func configure_preferences(scale: float, contrast: bool) -> void:
	_styles.configure_preferences(scale, contrast)

static func set_profile(value: Resource) -> void:
	_styles.set_profile(value)

static func color(role: String) -> Color:
	return _styles.color(role)

static func scoped_profile(value: Resource, construct: Callable):
	return _styles.scoped_profile(value, construct)

static func color_tag(role: String) -> String:
	return _styles.color_tag(role)

static func build(high_contrast := false) -> Theme:
	return _styles.build(high_contrast)

static func box(color: Color, radius := 8, border := Color.TRANSPARENT, width := 0) -> StyleBoxFlat:
	return _styles.box(color, radius, border, width)

static func title(text: String, size := 34, color = null) -> Label:
	return _styles.title(text, size, color)

static func muted(text: String, size := 15, min_width := 0.0) -> Label:
	return _styles.muted(text, size, min_width)

static func caption(text: String, size := 15, min_width := 0) -> Label:
	return _styles.caption(text, size, min_width)

static func wrap(label: Label, min_width := MIN_READABLE_TEXT_WIDTH) -> Label:
	return _styles.wrap(label, min_width)

static func button(text: String, min_width := 0) -> Button:
	return _styles.button(text, min_width)

static func panel(content: Control, color = null) -> PanelContainer:
	return _styles.panel(content, color)

static func apply_view_preferences(node: Node, scale_override := -1.0, contrast_override = null, revision: Array = []) -> void:
	_styles.apply_view_preferences(node, scale_override, contrast_override, revision)
