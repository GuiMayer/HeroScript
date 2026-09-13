extends PanelContainer
## Passive actor view: the portrait is a target, not just a small text button.
signal target_selected(actor_id: String)
var portrait_view: Control
var target_button: Button

func setup(actor: Dictionary, hostile: bool, presentation: Dictionary, intent: String) -> void:
	var content := VBoxContainer.new()
	content.custom_minimum_size.x = 258
	content.add_theme_constant_override("separation", 5)
	var intention := Label.new()
	intention.text = I18n.text("INTENT  •  %s") % intent if not intent.is_empty() else (I18n.text("YOUR SIDE") if not hostile else I18n.text("No intent revealed"))
	intention.tooltip_text = intention.text
	intention.custom_minimum_size.y = 48
	intention.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intention.max_lines_visible = 3
	intention.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	intention.add_theme_font_size_override("font_size", 14)
	intention.add_theme_color_override("font_color", AppTheme.GOLD if hostile else AppTheme.TEAL)
	content.add_child(intention)
	target_button = Button.new()
	target_button.custom_minimum_size.y = 136
	target_button.toggle_mode = true
	target_button.tooltip_text = I18n.text("Choose a highlighted target.")
	for state in ["normal", "disabled"]: target_button.add_theme_stylebox_override(state, AppTheme.box(Color.TRANSPARENT, 12))
	for state in ["hover", "focus", "pressed"]: target_button.add_theme_stylebox_override(state, AppTheme.box(Color("#e7b75d18"), 12, AppTheme.GOLD, 2))
	target_button.pressed.connect(func(): target_selected.emit(str(actor.get("instanceId", ""))))
	content.add_child(target_button)
	var portrait := preload("res://scripts/ui/art_slot.gd").new()
	portrait.setup("actors", str(actor.get("definitionId", "")))
	target_button.add_child(portrait)
	portrait.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	portrait_view = portrait
	var title := AppTheme.title(I18n.content_name(str(actor.get("definitionId", "")), str(actor.get("name", ""))), 18)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	title.add_theme_color_override("font_color", AppTheme.INK)
	content.add_child(title)
	var counters := HFlowContainer.new()
	counters.alignment = FlowContainer.ALIGNMENT_CENTER
	counters.add_theme_constant_override("h_separation", 12)
	var resources: Dictionary = actor.get("resources", {})
	var ordered: Array = presentation.get("resource_order", []).duplicate()
	for id in resources:
		if id not in ordered: ordered.append(id)
	for id in ordered:
		if not resources.has(id): continue
		var appearance: Dictionary = presentation.get("resources", {}).get(id, {})
		if str(appearance.get("display", "bar")) == "bar":
			var resource := preload("res://scripts/ui/resource_view.gd").new()
			resource.setup(id, resources[id], appearance)
			content.add_child(resource)
		else:
			var amount := AppTheme.muted("%s %s" % [I18n.number(float(resources[id].get("current", 0))), I18n.content_name(id)], 14)
			amount.autowrap_mode = TextServer.AUTOWRAP_OFF
			amount.add_theme_color_override("font_color", Color(str(appearance.get("color", "#e7b75d"))))
			amount.tooltip_text = "%s: %s / %s" % [I18n.content_name(id), resources[id].get("current", 0), resources[id].get("maximum", 0)]
			counters.add_child(amount)
	content.add_child(counters)
	var statuses: Array = actor.get("statuses", []).filter(func(status): return status.get("isActive", true))
	var status_text := statuses.map(func(status): return "%s ×%s" % [I18n.content_name(str(status.get("statusId", "status"))), status.get("stacks", 1)])
	var status := AppTheme.muted(" · ".join(status_text) if not statuses.is_empty() else I18n.text("No active statuses"), 12)
	status.autowrap_mode = TextServer.AUTOWRAP_OFF
	status.tooltip_text = status.text
	status.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	status.add_theme_color_override("font_color", AppTheme.EMBER if not statuses.is_empty() else AppTheme.MUTED)
	content.add_child(status)
	add_theme_stylebox_override("panel", AppTheme.box(Color("#161723bc"), 16))
	add_child(content)
