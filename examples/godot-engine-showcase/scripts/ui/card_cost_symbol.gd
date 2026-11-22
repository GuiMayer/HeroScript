@tool
extends MarginContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
const INK := Color("#20201c")
const NEUTRAL = preload("res://assets/ivory/cost-neutral.svg")
var amount_label: Label
var icon: TextureRect
static func create(cost: Dictionary, appearance: Dictionary, context: ContextType, policy: CardVisualStyle) -> Control:
    var chip = (load("res://scenes/ui/components/card_cost_symbol.tscn") as PackedScene).instantiate()
    chip.configure(cost, appearance, context, policy)
    return chip
func configure(cost: Dictionary, appearance: Dictionary, context: ContextType, policy: CardVisualStyle) -> void:
    icon = get_node("%ResourceSymbol")
    amount_label = get_node("%CostAmount")
    var mnemonic: Label = get_node("%ResourceMnemonic")
    set_meta("resource_id", str(cost.get("resourceId", "")))
    var dimension := maxf(policy.cost_symbol_size.x, policy.cost_symbol_size.y)
    custom_minimum_size = Vector2(dimension, dimension) * context.text_scale
    icon.self_modulate = policy.cost_symbol_fill_color
    var path := str(appearance.get("cost_icon", ""))
    icon.texture = load(path) if path.begins_with("res://") and ResourceLoader.exists(path, "Texture2D") else NEUTRAL
    amount_label.text = context.translator.number(float(cost.get("amount", 0)))
    amount_label.add_theme_font_size_override("font_size", policy.cost_symbol_font_size)
    amount_label.add_theme_color_override("font_color", policy.cost_symbol_number_color)
    tooltip_text = "%s: %s" % [cost.get("name", cost.get("resourceId", "")), amount_label.text]
    mnemonic.visible = icon.texture == NEUTRAL
    mnemonic.text = str(cost.get("symbol", appearance.get("symbol", "•")))
    # Keep a custom-resource abbreviation outside the numbered silhouette.
    # The complete resource name remains available in the tooltip.
    var mnemonic_size := roundi(13.0 * context.text_scale)
    var mnemonic_width := ceilf(mnemonic.get_theme_font("font").get_string_size(mnemonic.text, HORIZONTAL_ALIGNMENT_LEFT, -1, mnemonic_size).x) + 4.0
    if mnemonic.visible: custom_minimum_size.x += mnemonic_width
    add_theme_constant_override("margin_right", int(mnemonic_width) if mnemonic.visible else 0)
    mnemonic.offset_left = 2.0
    mnemonic.offset_right = mnemonic_width
    mnemonic.add_theme_color_override("font_color", context.styles.INK)
    mnemonic.add_theme_font_size_override("font_size", mnemonic_size)
    mnemonic.set_meta("base_font_size", 13.0)
func reconcile_from(other) -> void:
    preload("res://scripts/ui/view_reconciler.gd").patch_contents(self, other)
