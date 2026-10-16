extends Control
## Run-only composition. Shares the passive view/workspace, never lab fixtures or actor selection.
signal back_requested
var session
var workspace
var view
var visual_context
var _identity: Array = []
var _loading := false
var _ticket := 0
var _message := ""
var _status := Label.new()
var _preview_button: Button
var _confirm_button: Button
var _retry_button: Button
var _discard := ConfirmationDialog.new()

func _context() -> Array:
	var run: Dictionary = session.run
	return [run.get("runId", ""), run.get("playerEntityId", ""), run.get("settingId", run.get("configName", ""))]
func _same_character() -> bool:
	return not _identity.is_empty() and not str(_identity[0]).is_empty() and not str(_identity[1]).is_empty() and _context() == _identity
func _usable() -> bool:
	return workspace != null and _same_character() and str(workspace.source.get("actorId", "")) == str(_identity[1]) and session.loadout_context_matches(workspace.source)

func _ready() -> void:
	if session == null: session = GameSession
	_identity = _context()
	visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(preload("res://data/themes/ivory_theme.tres"))
	theme = visual_context.theme
	var paper := ColorRect.new(); paper.color = visual_context.styles.NIGHT
	paper.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); paper.mouse_filter = Control.MOUSE_FILTER_IGNORE; add_child(paper)
	var margin := MarginContainer.new(); margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 20)
	add_child(margin)
	var scroll := ScrollContainer.new(); scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED; margin.add_child(scroll)
	var page := VBoxContainer.new(); page.size_flags_horizontal = Control.SIZE_EXPAND_FILL; scroll.add_child(page)
	var header := HBoxContainer.new(); page.add_child(header)
	var title := Label.new(); title.text = I18n.text("CHARACTER LOADOUT"); title.theme_type_variation = "SectionTitle"; title.size_flags_horizontal = Control.SIZE_EXPAND_FILL; header.add_child(title)
	header.add_child(_button("BACK", func(): _discard_then(back_requested.emit)))
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART; page.add_child(_status)
	var actions := HBoxContainer.new(); page.add_child(actions)
	_retry_button = _button("Recover / refresh run", func(): _discard_then(_recover)); actions.add_child(_retry_button)
	_preview_button = _button("Preview draft", _preview); actions.add_child(_preview_button)
	_confirm_button = _button("Confirm equipment", _confirm); actions.add_child(_confirm_button)
	_discard.dialog_text = I18n.text("Discard unconfirmed loadout changes?"); add_child(_discard)
	view = preload("res://scripts/ui/loadout/loadout_view.gd").new(); view.mount(visual_context); page.add_child(view)
	view.find_child("LoadoutHeader", true, false).hide()
	view.item_requested.connect(func(id): _edit("select_item", [id]))
	view.slot_requested.connect(func(id): _edit("select_slot", [id]))
	view.drop_requested.connect(func(item, slot, origin): _edit("stage_drop", [item, slot, origin]))
	view.equip_requested.connect(func(): _edit("stage_selected"))
	view.remove_requested.connect(func(): _edit("remove_selected"))
	view.clear_requested.connect(func(): _edit("clear"))
	view.restore_requested.connect(func(): _edit("restore"))
	view.search_requested.connect(func(value):
		if _usable(): workspace.search = value; _refresh())
	session.changed.connect(_refresh)
	_refresh()
	_read.call_deferred()

func _button(key: String, action: Callable) -> Button:
	var button: Button = visual_context.styles.button(I18n.text(key)); button.pressed.connect(action); return button
func _discard_then(action: Callable) -> void:
	if workspace != null and workspace.dirty():
		for binding in _discard.confirmed.get_connections(): _discard.confirmed.disconnect(binding.callable)
		_discard.confirmed.connect(action, CONNECT_ONE_SHOT); _discard.popup_centered()
	else: action.call()
func _edit(method: String, args: Array = []) -> void:
	if _loading or not _usable(): return
	workspace.callv(method, args); _refresh()
func _read() -> void:
	if _loading: return
	workspace = null
	if not _same_character(): _message = "Run or character changed. Reopen loadout."; _refresh(); return
	_loading = true; _ticket += 1
	var ticket := _ticket
	_message = "Loading authoritative loadout…"; _refresh()
	var response: Dictionary = await session.read_loadout(str(_identity[1]))
	if not is_inside_tree() or ticket != _ticket: return
	_loading = false
	if not _same_character(): _message = "Run or character changed. Reopen loadout."
	elif not response.ok: _message = str(response.get("error", response.get("errorKey", "Loadout unavailable.")))
	elif not response.get("data") is Dictionary or str(response.data.get("actorId", "")) != str(_identity[1]) or not session.loadout_context_matches(response.data): _message = "Loadout changed. Refresh before editing."
	else:
		var candidate = preload("res://scripts/application/loadout_workspace.gd").new()
		if candidate.accept(response.data): workspace = candidate; _message = str(_identity[1])
		else: _message = "Loadout unavailable."
	_refresh()
func _preview() -> void:
	if _loading or not _usable(): return
	_loading = true
	var captured = workspace
	var generation: int = captured.generation
	var assignments: Dictionary = captured.assignments.duplicate(true)
	_refresh()
	var response: Dictionary = await session.preview_loadout(captured.source, assignments)
	if not is_inside_tree(): return
	_loading = false
	if workspace == captured and _usable():
		if response.ok: captured.accept_preview(response.data, generation, assignments)
		else: captured.preview = {}; captured.message = str(response.get("error", response.get("errorKey", "Preview unavailable.")))
	_refresh()
func _confirm() -> void:
	if _loading or not _usable() or not workspace.can_confirm(): return
	_loading = true; _refresh()
	var success: bool = await session.apply_loadout(workspace.source, workspace.assignments)
	if not is_inside_tree(): return
	_loading = false
	if success: _read()
	else:
		workspace.preview = {}; _message = "Equipment not confirmed. Recover / refresh run before retrying."; _refresh()
func _recover() -> void:
	if _loading or not _same_character(): return
	_loading = true; _refresh()
	await session.recover_pending()
	if not is_inside_tree(): return
	_loading = false; _read()
func _refresh() -> void:
	if not is_instance_valid(view): return
	var usable := _usable()
	view.visible = usable
	if usable:
		var model: Dictionary = workspace.snapshot()
		model.canEdit = model.canEdit and not _loading
		view.present(model)
	_status.text = I18n.text("Run or character changed. Reopen loadout." if not _same_character() else _message)
	if workspace != null and _same_character() and not usable and not _loading:
		_status.text = I18n.text("Loadout changed. Refresh before editing.")
	_preview_button.disabled = _loading or not usable or not workspace.dirty()
	_confirm_button.disabled = _loading or not usable or not workspace.can_confirm()
	_retry_button.disabled = _loading or not _same_character()
func _exit_tree() -> void:
	_ticket += 1
	if session != null and session.changed.is_connected(_refresh): session.changed.disconnect(_refresh)
