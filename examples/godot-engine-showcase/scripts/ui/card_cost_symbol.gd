extends MarginContainer
## Passive printed cost: presentation chooses the art, engine publishes the amount.
## Native text stays separate from the vector so translation and text scaling work.
const ContextType = preload("res://scripts/ui/visual_context.gd")
const NEUTRAL = preload("res://assets/ivory/cost-neutral.svg")
const PAPER := Color("#faf5eb")
const INK := Color("#20201c")
var amount_label: Label
var icon: TextureRect

func _init(cost: Dictionary, appearance: Dictionary, context: ContextType, policy: CardVisualStyle) -> void:
	name = "CostSymbol"
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	set_meta("resource_id", str(cost.get("resourceId", "")))
	custom_minimum_size = policy.cost_symbol_size
	icon = TextureRect.new()
	icon.name = "ResourceSymbol"
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_SCALE
	var path := str(appearance.get("cost_icon", ""))
	icon.texture = load(path) if path.begins_with("res://") and ResourceLoader.exists(path, "Texture2D") else NEUTRAL
	add_child(icon)
	var inset := MarginContainer.new()
	inset.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for side in ["left", "right"]: inset.add_theme_constant_override("margin_" + side, 13)
	for side in ["top", "bottom"]: inset.add_theme_constant_override("margin_" + side, 10)
	add_child(inset)
	amount_label = context.styles.title(context.translator.number(float(cost.get("amount", 0))), policy.cost_symbol_font_size)
	amount_label.name = "CostAmount"
	amount_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	amount_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	amount_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	amount_label.autowrap_mode = TextServer.AUTOWRAP_OFF
	amount_label.add_theme_color_override("font_color", INK)
	inset.add_child(amount_label)
	tooltip_text = "%s: %s" % [cost.get("name", cost.get("resourceId", "")), amount_label.text]
	# Unknown resources still expose their configured mnemonic, not a guessed rule.
	if icon.texture == NEUTRAL:
		var legend := MarginContainer.new()
		legend.mouse_filter = Control.MOUSE_FILTER_IGNORE
		for side in ["left", "right"]: legend.add_theme_constant_override("margin_" + side, 3)
		for side in ["top", "bottom"]: legend.add_theme_constant_override("margin_" + side, 1)
		add_child(legend)
		var mnemonic := context.styles.muted(str(cost.get("symbol", appearance.get("symbol", "•"))), 9)
		mnemonic.name = "ResourceMnemonic"
		mnemonic.mouse_filter = Control.MOUSE_FILTER_IGNORE
		mnemonic.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		mnemonic.vertical_alignment = VERTICAL_ALIGNMENT_TOP
		mnemonic.autowrap_mode = TextServer.AUTOWRAP_OFF
		mnemonic.add_theme_color_override("font_color", INK)
		legend.add_child(mnemonic)
