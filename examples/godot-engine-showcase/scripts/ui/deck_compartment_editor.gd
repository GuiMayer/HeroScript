extends Control
## Reusable build editor. Uses the combat CardView and passive facts presenter; no game rules.
signal back_requested
var service
var visual_context
var appearance: Dictionary = {}
var workspace = preload("res://scripts/application/deck_compartment_workspace.gd").new()
var selected_id := ""
var busy := false
var uncertain := false
var _page: VBoxContainer
var _body: HBoxContainer
var _status: Label
var _apply: Button
var _target: OptionButton
var _discard: ConfirmationDialog
var _alive := true

func _ready() -> void:
	if visual_context == null: visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(preload("res://data/themes/ivory_theme.tres"))
	theme = visual_context.theme
	if appearance.is_empty():
		for path in ["res://data/presentation.json", "res://data/volatile_core_presentation.json", "res://data/world_one_presentation.json"]:
			var overlay = JSON.parse_string(FileAccess.get_file_as_string(path))
			if not overlay is Dictionary: continue
			for category in overlay:
				if not appearance.has(category): appearance[category] = {}
				if overlay[category] is Dictionary: appearance[category].merge(overlay[category], true)
	var background := ColorRect.new(); background.color = visual_context.styles.PANEL
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); background.mouse_filter = Control.MOUSE_FILTER_IGNORE; add_child(background)
	var margin := MarginContainer.new(); margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 24)
	add_child(margin)
	_page = VBoxContainer.new(); _page.add_theme_constant_override("separation", 12); margin.add_child(_page)
	var header := HBoxContainer.new(); _page.add_child(header)
	var title: Label = visual_context.styles.title(_text("DECK COMPARTMENTS"), 28); title.size_flags_horizontal = Control.SIZE_EXPAND_FILL; header.add_child(title)
	header.add_child(_button("BACK", _back))
	var actions := HBoxContainer.new(); _page.add_child(actions)
	_target = OptionButton.new(); _target.custom_minimum_size.x = 180; actions.add_child(_target)
	actions.add_child(_button("MOVE SELECTED", func():
		if _target.selected >= 0: _stage(selected_id, str(_target.get_item_metadata(_target.selected)))))
	actions.add_child(_button("CLEAR DRAFT", func():
		if not busy and not uncertain: workspace.clear(); _render(); _request_preview()))
	actions.add_child(_button("RESTORE", func():
		if not busy and not uncertain: workspace.load_snapshot(workspace.source); _render()))
	_apply = _button("APPLY", _commit); actions.add_child(_apply)
	_status = Label.new(); _status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART; _page.add_child(_status)
	var scroll := ScrollContainer.new(); scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL; _page.add_child(scroll)
	_body = HBoxContainer.new(); _body.add_theme_constant_override("separation", 18); scroll.add_child(_body)
	_discard = ConfirmationDialog.new(); _discard.dialog_text = _text("Discard unconfirmed compartment changes?")
	_discard.confirmed.connect(func(): back_requested.emit()); add_child(_discard)
	if service != null: read_state.call_deferred()

func _text(key: String) -> String: return visual_context.translator.text(key)
func _button(key: String, action: Callable) -> Button:
	var button: Button = visual_context.styles.button(_text(key)); button.pressed.connect(action); return button
func _back() -> void:
	if busy or uncertain: return
	if workspace.dirty(): _discard.popup_centered()
	else: back_requested.emit()
func _exit_tree() -> void: _alive = false

func read_state() -> void:
	busy = true; _status.text = _text("Loading compartments…")
	var response: Dictionary = await service.read()
	if not _alive: return
	busy = false
	if not response.get("ok", false) or not response.get("data") is Dictionary: _status.text = _error(response); return
	workspace.load_snapshot(response.data); selected_id = ""; _render()

func _stage(id: String, target: String) -> void:
	if busy or uncertain or not workspace.stage(id, target): return
	_render(); _request_preview()

func _request_preview() -> void:
	var ticket: int = workspace.revision
	_status.text = _text("Pending changes — checking components…"); _apply.disabled = true
	await get_tree().create_timer(.15).timeout
	if not _alive or ticket != workspace.revision: return
	var response: Dictionary = await service.preview(workspace.source.duplicate(true), workspace.allocations())
	if not _alive or ticket != workspace.revision: return
	var payload = response.get("data")
	if response.get("ok", false) and payload is Dictionary and payload.get("after") is Dictionary and workspace.accept_preview(payload.after, ticket): _render()
	else: _status.text = _error(response); _apply.disabled = true

func _commit() -> void:
	if busy or (not uncertain and (not workspace.dirty() or workspace.preview.is_empty())): return
	busy = true; _apply.disabled = true
	var response: Dictionary = await service.apply(workspace.source.duplicate(true), workspace.allocations())
	if not _alive: return
	busy = false
	if response.get("ok", false): uncertain = false; await read_state(); return
	uncertain = int(response.get("status", 0)) < 400 or int(response.get("status", 0)) >= 500
	_status.text = _error(response)
	_apply.text = _text("RETRY SAME COMMAND") if uncertain else _text("APPLY")
	_apply.disabled = not uncertain
	if int(response.get("status", 0)) == 409:
		workspace.preview = {}; _status.text += " " + _text("Version changed. Close and reopen to review the current build.")

func _render() -> void:
	for child in _body.get_children(): _body.remove_child(child); child.queue_free()
	_target.clear(); _target.add_item(_text("Neutral")); _target.set_item_metadata(0, "")
	var source: Dictionary = workspace.source
	var snapshot: Dictionary = workspace.preview if not workspace.preview.is_empty() else source
	var compartments: Array = source.get("profile", {}).get("compartments", []) if source.get("profile") is Dictionary else []
	var columns: Dictionary = {"": _column(_text("Neutral"), _text("No compartment contribution"), "")}
	for compartment in compartments:
		var id := str(compartment.compartmentId)
		var used: int = workspace.draft.values().count(id)
		columns[id] = _column("%s · %d/%d" % [_text(str(compartment.get("nameKey", id))), used, int(compartment.capacity)], _text(str(compartment.get("descriptionKey", ""))), id)
		_target.add_item(_text(str(compartment.get("nameKey", id)))); _target.set_item_metadata(_target.item_count - 1, id)
	var presenter = preload("res://scripts/presentation/card_facts_presenter.gd").new(visual_context.translator, appearance)
	for entry in snapshot.get("cards", []):
		var id := str(entry.cardInstanceId)
		var target := str(workspace.draft.get(id, ""))
		if not columns.has(target): continue
		var card = preload("res://scripts/ui/card_view.gd").new(visual_context)
		card.animate_entry = false; card.configure(presenter.project(entry.presentation)); card.select_card(selected_id == id)
		for compartment in compartments:
			if str(compartment.compartmentId) == target:
				card.tooltip_text += "\n" + _text("COMPARTMENT CONTRIBUTION") + ": " + _text(str(compartment.get("descriptionKey", "")))
		card.pressed.connect(func(): selected_id = id; _render())
		card.set_drag_forwarding(func(_at):
			if busy or uncertain or not source.get("canChange", false): return null
			return {"kind": "deck_compartment_card", "runId": source.get("runId", ""), "cardInstanceId": id}, Callable(), Callable())
		columns[target].add_child(card)
	_apply.text = _text("APPLY"); _apply.disabled = busy or uncertain or not source.get("canChange", false) or not workspace.dirty() or workspace.preview.is_empty()
	_status.text = _text("Pending changes — not applied") if workspace.dirty() else _text("Current build — all changes saved")
	if not source.get("canChange", false): _status.text = _text(str(source.get("unavailableReason", "Read only")))

func _column(title: String, description: String, id: String) -> VBoxContainer:
	var panel := PanelContainer.new(); panel.custom_minimum_size.x = 260; _body.add_child(panel)
	var column := VBoxContainer.new(); column.add_theme_constant_override("separation", 8); panel.add_child(column)
	column.add_child(visual_context.styles.title(title, 18))
	var details := Label.new(); details.text = description; details.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART; details.custom_minimum_size.x = 250; column.add_child(details)
	panel.set_drag_forwarding(Callable(), func(_at, data):
		return not busy and not uncertain and data is Dictionary and data.get("kind") == "deck_compartment_card" and data.get("runId") == workspace.source.get("runId"),
		func(_at, data): _stage(str(data.cardInstanceId), id))
	return column

func _error(response: Dictionary) -> String:
	if response.get("error") != null: return str(response.error)
	var data = response.get("data")
	return str(data.get("detail", _text("Unable to update compartments"))) if data is Dictionary else _text("Unable to update compartments")
