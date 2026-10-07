extends PanelContainer
## Shared visual policy with explicit passive-summary and persistent-inspection modes.
const EffectText = preload("res://scripts/ui/card_effect_text.gd")
enum DisplayMode { INSPECTION, TOOLTIP }
var visual_style: Resource
var display_mode := DisplayMode.INSPECTION

func configure(model: Dictionary, style: Resource, mode := DisplayMode.INSPECTION) -> void:
	visual_style = style
	display_mode = mode
	name = "CardTooltip" if mode == DisplayMode.TOOLTIP else "CardDetails"
	var border: Color = style.rarity_color(str(model.get("rarityId", "")))
	set_meta("semantic_border_color", border)
	add_theme_stylebox_override("panel", AppTheme.box(AppTheme.PANEL, 10, border, 2))
	custom_minimum_size = Vector2(style.tooltip_width, 0 if mode == DisplayMode.TOOLTIP else style.inspection_height)
	var body := VBoxContainer.new()
	body.name = "CardDetailContent"
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 8)
	if mode == DisplayMode.TOOLTIP:
		# A native tooltip is transient. It must not offer scrollbars, selection or
		# focusable controls that cannot be reached while leaving the hovered card.
		add_child(body)
	else:
		var scroll := ScrollContainer.new()
		scroll.name = "CardDetailsScroll"
		scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
		scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
		scroll.focus_mode = Control.FOCUS_ALL
		scroll.follow_focus = true
		add_child(scroll)
		scroll.add_child(body)
	body.add_child(AppTheme.caption(str(model.get("name", "")), 20, 260))
	body.add_child(AppTheme.caption(" · ".join([str(model.get("cardType", "")), str(model.get("rarity", ""))]), 13, 260))
	var options: Array = model.get("costOptions", [])
	if options.is_empty(): options = [model.get("costs", [])]
	var payments: Array[String] = []
	for option in options:
		var parts: Array[String] = []
		for cost in option: parts.append("%s %s" % [I18n.number(float(cost.get("amount", 0))), str(cost.get("name", cost.get("resourceId", "")))])
		payments.append(" + ".join(parts) if not parts.is_empty() else (str(model.get("cost", "0")) if model.get("costsKnown", true) else I18n.text("Alternative cost — inspect")))
	body.add_child(AppTheme.caption(I18n.text("Cost: %s") % (" " + I18n.text("OR") + " ").join(payments), 14, 260))
	if not model.get("costsKnown", true): body.add_child(AppTheme.caption(I18n.text("Alternative cost — inspect"), 14, 260))
	var requirements: Array = model.get("requirements", [])
	if mode == DisplayMode.TOOLTIP: requirements = requirements.slice(0, 1)
	for requirement in requirements: body.add_child(AppTheme.caption(I18n.text("Requires: %s") % str(requirement.get("text", "")), 14, 260))
	var rules := EffectText.new()
	rules.name = "DetailedEffects"
	rules.fit_content = mode == DisplayMode.INSPECTION
	rules.scroll_active = false
	rules.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var rows: Array = model.get("effectRows", [])
	if rows.is_empty(): rows = EffectText.plain_rows(Array(model.get("effects", str(model.get("summary", "")).split("\n"))))
	if mode == DisplayMode.TOOLTIP: rows = rows.slice(0, maxi(1, style.tooltip_effect_rows))
	rules.configure(rows, style)
	body.add_child(rules)
	if mode == DisplayMode.TOOLTIP:
		_build_tooltip_summary(body, rules, model)
		return
	for row in rows:
		for segment in row.get("segments", []):
			if (segment.get("value") is float or segment.get("value") is int) and (segment.get("baseValue") is float or segment.get("baseValue") is int):
				body.add_child(AppTheme.caption(I18n.text("Base: %s → Current: %s") % [I18n.number(float(segment.baseValue)), I18n.number(float(segment.value))], 12, 260))
	for behavior in model.get("behaviors", []): body.add_child(AppTheme.caption(str(behavior), 13, 260))
	for badge in model.get("changeBadges", []):
		body.add_child(AppTheme.caption(str(badge.get("label", "")) + ": " + ", ".join(badge.get("sources", [])), 13, 260))
	for field in ["previewNote", "availability", "inspectionText"]:
		if not str(model.get(field, "")).is_empty(): body.add_child(AppTheme.caption(str(model[field]), 13, 260))
	# Detailed inspection must wrap rather than silently ellipsize descriptions.
	for child in body.get_children():
		if child is Label: AppTheme.wrap(child, 260)
	AppTheme.apply_view_preferences(self)

func _build_tooltip_summary(body: VBoxContainer, rules: RichTextLabel, model: Dictionary) -> void:
	# Logs, contribution sources, comparisons and technical data belong only to
	# the persistent inspector. Large content cannot make this tooltip grow forever.
	if not model.get("behaviors", []).is_empty(): body.add_child(AppTheme.caption(" · ".join(model.behaviors), 13, 260))
	var changes: Array[String] = []
	for badge in model.get("changeBadges", []).slice(0, maxi(0, visual_style.visible_change_badges)):
		changes.append(str(badge.get("label", "")))
	if not changes.is_empty(): body.add_child(AppTheme.caption(" · ".join(changes), 13, 260))
	for field in ["previewNote", "availability"]:
		if not str(model.get(field, "")).is_empty(): body.add_child(AppTheme.caption(str(model[field]), 13, 260))
	var hint := AppTheme.caption(I18n.text("Summary — open card inspection for full details."), 12, 260)
	hint.name = "InspectionHint"
	body.add_child(hint)
	for child in body.get_children():
		if child is Label:
			AppTheme.wrap(child, 260)
			child.max_lines_visible = maxi(1, visual_style.tooltip_label_lines)
			child.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	AppTheme.apply_view_preferences(self)
	var font := rules.get_theme_font("normal_font")
	var font_size := rules.get_theme_font_size("normal_font_size")
	rules.custom_minimum_size.y = ceilf(font.get_height(font_size)) * maxi(1, visual_style.tooltip_effect_lines)
	_disable_tooltip_input(self)

func _disable_tooltip_input(node: Node) -> void:
	# RichTextLabel adjusts its focus mode when selection_enabled changes. Apply
	# the passive focus policy AFTER that setter, not before it.
	if node is RichTextLabel: node.selection_enabled = false
	if node is Control:
		node.mouse_filter = Control.MOUSE_FILTER_IGNORE
		node.focus_mode = Control.FOCUS_NONE
		node.tooltip_text = ""
	for child in node.get_children(): _disable_tooltip_input(child)
