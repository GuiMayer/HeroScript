extends PanelContainer
## Passive reusable actor view, shared by live and historical combat.
signal target_selected(actor_id: String)
var portrait_view: Control
var target_button: Button

func setup(actor: Dictionary, hostile: bool, presentation: Dictionary, intent: String) -> void:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(290, 240)
	content.add_theme_constant_override("separation", 4)
	var name := Label.new()
	name.text = I18n.content_name(str(actor.get("definitionId", "")), str(actor.get("name", ""))).to_upper()
	name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name.add_theme_font_size_override("font_size", 19)
	name.add_theme_color_override("font_color", AppTheme.BLOOD if hostile else AppTheme.TEAL)
	content.add_child(name)
	var side := AppTheme.muted(str(actor.get("sideId", "")) + " • " + str(actor.get("controllerBinding", {}).get("kind", "")), 12)
	side.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(side)
	var portrait := ActorPortrait.new()
	portrait.custom_minimum_size = Vector2(260, 64)
	portrait.configure(hostile, str(actor.get("definitionId", "")).contains("sentinel"))
	portrait_view = portrait
	content.add_child(portrait)
	var resources: Dictionary = actor.get("resources", {})
	var ordered: Array = presentation.get("resource_order", []).duplicate()
	for id in resources:
		if id not in ordered:
			ordered.append(id)
	for resource_id in ordered:
		if resources.has(resource_id):
			var resource := preload("res://scripts/ui/resource_view.gd").new()
			resource.setup(resource_id, resources[resource_id], presentation.get("resources", {}).get(resource_id, {}))
			content.add_child(resource)
	var statuses: Array = actor.get("statuses", [])
	if not statuses.is_empty():
		var status_text := statuses.map(func(status): return "%s ×%s" % [
			I18n.content_name(str(status.get("statusId", "status"))), status.get("stacks", 1)] )
		var status := AppTheme.muted("  •  ".join(status_text), 13)
		status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		status.add_theme_color_override("font_color", AppTheme.EMBER)
		content.add_child(status)
	if not intent.is_empty():
		var intent_label := Label.new()
		intent_label.text = I18n.text("INTENT  •  %s") % intent
		intent_label.add_theme_font_size_override("font_size", 14)
		intent_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		intent_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		intent_label.add_theme_color_override("font_color", AppTheme.GOLD)
		content.add_child(intent_label)
	var actor_id := str(actor.get("instanceId", ""))
	var select := AppTheme.button(I18n.text("TARGET"))
	select.pressed.connect(func(): target_selected.emit(actor_id))
	select.toggle_mode = true
	target_button = select
	content.add_child(select)
	add_theme_stylebox_override("panel", AppTheme.box(Color("#261923e8") if hostile else Color("#15252ce8"), 12))
	add_child(content)
