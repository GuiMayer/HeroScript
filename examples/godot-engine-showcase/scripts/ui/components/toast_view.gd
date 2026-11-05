extends PanelContainer
var text: String:
	get: return get_node("Message").text
	set(value): get_node("Message").text = value
func present(message: String, error: bool, context) -> void:
	text = context.translator.text("Error") + ": " + message if error else message
	get_node("Message").add_theme_color_override("font_color", context.styles.BLOOD if error else context.styles.INK)
func _ready() -> void:
	get_viewport().size_changed.connect(_fit)
	_fit()
func _fit() -> void:
	var width := minf(520, maxf(1, get_viewport_rect().size.x - 32))
	offset_left = -width * .5; offset_right = width * .5
	get_node("Message").custom_minimum_size.x = maxf(1, width - 28)
	size.y = 0
