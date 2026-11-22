extends RefCounted
## Identity-only drag; legal options always come from the published snapshot.
static func payload(scope: int, generation: int, snapshot: Dictionary, item: Dictionary) -> Dictionary:
	return {"kind": "card_rune", "scope": scope, "generation": generation, "runId": snapshot.get("runId", ""), "sequence": snapshot.get("runSequence", -1), "revision": snapshot.get("contentRevision", ""), "itemInstanceId": item.get("instance", {}).get("itemInstanceId", "")}

static func matches(data: Variant, scope: int, generation: int, snapshot: Dictionary, item: Dictionary) -> bool:
	return data is Dictionary and not str(data.get("itemInstanceId", "")).is_empty() and data == payload(scope, generation, snapshot, item) and str(item.get("instance", {}).get("state", "")) == "Available"

static func preview(title: String, theme: Theme) -> Control:
	var panel := PanelContainer.new()
	panel.theme = theme; panel.custom_minimum_size = Vector2(180, 62); panel.mouse_filter = Control.MOUSE_FILTER_IGNORE; panel.z_index = 100
	var style := StyleBoxFlat.new(); style.bg_color = Color("272b2e"); style.border_color = Color("d89e57")
	style.set_border_width_all(2); style.set_corner_radius_all(5); style.content_margin_left = 15; style.content_margin_right = 15
	panel.add_theme_stylebox_override("panel", style)
	var label := Label.new(); label.text = "◆  " + title; label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_color_override("font_color", Color("f9e6c5")); label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.add_child(label)
	return panel
