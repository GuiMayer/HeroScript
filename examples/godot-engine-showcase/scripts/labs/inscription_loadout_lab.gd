extends Control
## Composition root for isolated examples and an explicitly connected REST workspace.
signal back_requested
@export_file("*.json") var fixture_path := "res://data/labs/inscription_loadout_lab.json"
var draft = preload("res://scripts/labs/inscription_loadout_draft.gd").new()
var view
var visual_context
var fixture_error := ""
var remote := false
var _loading := false
var _ticket := 0
var _fixture: Dictionary = {}
var _visual_drafts := {}
var _actor_picker := OptionButton.new()
var _scope_picker := OptionButton.new()
var _connection_note := Label.new()
var _preview_button: Button
var _confirm_button: Button
var _discard_dialog := ConfirmationDialog.new()

func _ready() -> void:
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	size_flags_vertical = Control.SIZE_EXPAND_FILL
	visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(
		preload("res://data/themes/ivory_theme.tres"))
	var paper := ColorRect.new()
	paper.color = visual_context.styles.NIGHT
	paper.mouse_filter = Control.MOUSE_FILTER_IGNORE
	paper.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(paper)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 20)
	add_child(margin)
	var scroll := ScrollContainer.new()
	scroll.name = "LoadoutPageScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.follow_focus = true
	margin.add_child(scroll)
	var document = JSON.parse_string(FileAccess.get_file_as_string(fixture_path))
	fixture_error = draft.configure(document) if document is Dictionary else "Invalid visual fixture."
	if not fixture_error.is_empty():
		var message := Label.new()
		message.text = fixture_error
		scroll.add_child(message)
		return
	_fixture = document.duplicate(true)
	_visual_drafts["Actor:0"] = draft
	var page := VBoxContainer.new()
	page.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(page)
	var connection := HBoxContainer.new()
	page.add_child(connection)
	for entry in [["Example (offline)", _offline], ["Connect current run", _connect_run], ["Recover / refresh run", _recover]]:
		var button: Button = visual_context.styles.button(I18n.text(entry[0]))
		button.pressed.connect(entry[1]); connection.add_child(button)
	_actor_picker.name = "LoadoutActorPicker"
	_actor_picker.add_item("Example character 1"); _actor_picker.add_item("Example character 2")
	_actor_picker.item_selected.connect(_actor_selected)
	connection.add_child(_actor_picker)
	_scope_picker.add_item("Actor inventory"); _scope_picker.add_item("Run inventory")
	_scope_picker.item_selected.connect(func(_index): _select_visual())
	connection.add_child(_scope_picker)
	_connection_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	page.add_child(_connection_note)
	var actions := HBoxContainer.new()
	page.add_child(actions)
	_preview_button = visual_context.styles.button(I18n.text("Preview draft"))
	_preview_button.pressed.connect(_preview); actions.add_child(_preview_button)
	_confirm_button = visual_context.styles.button(I18n.text("Confirm equipment"))
	_confirm_button.pressed.connect(_confirm); actions.add_child(_confirm_button)
	_discard_dialog.dialog_text = I18n.text("Discard unconfirmed loadout changes?")
	add_child(_discard_dialog)
	view = preload("res://scripts/ui/loadout/loadout_view.gd").new()
	view.mount(visual_context)
	page.add_child(view)
	view.item_requested.connect(func(id): _edit("select_item", [id]))
	view.slot_requested.connect(func(id): _edit("select_slot", [id]))
	view.equip_requested.connect(func(): _edit("stage_selected"))
	view.drop_requested.connect(func(item, slot, source): _edit("stage_drop", [item, slot, source]))
	view.remove_requested.connect(func(): _edit("remove_selected"))
	view.clear_requested.connect(func(): _edit("clear"))
	view.restore_requested.connect(func(): _edit("restore"))
	view.preset_requested.connect(func(index): _edit("apply_preset", [index]))
	view.search_requested.connect(func(value): draft.search = value; _refresh())
	view.back_requested.connect(func(): _discard_then(back_requested.emit))
	_refresh()
func _edit(method: String, args: Array = []) -> void:
	if _loading: return
	draft.callv(method, args); _refresh()
func _exit_tree() -> void:
	_ticket += 1
	for control in [_actor_picker, _scope_picker, _connection_note, _preview_button, _confirm_button, _discard_dialog]:
		if is_instance_valid(control) and control.get_parent() == null: control.free()
func _discard_then(callback: Callable) -> void:
	if _loading: return
	if remote and draft.dirty():
		for binding in _discard_dialog.confirmed.get_connections(): _discard_dialog.confirmed.disconnect(binding.callable)
		_discard_dialog.confirmed.connect(callback, CONNECT_ONE_SHOT)
		_discard_dialog.popup_centered()
	else: callback.call()
func _offline() -> void: _discard_then(_open_offline)
func _open_offline() -> void:
	_ticket += 1; remote = false
	_actor_picker.clear(); _actor_picker.add_item("Example character 1"); _actor_picker.add_item("Example character 2")
	_scope_picker.show(); _select_visual()
func _select_visual() -> void:
	if remote: return
	var key := "%s:%s" % ["Actor" if _scope_picker.selected == 0 else "Run", _actor_picker.selected]
	if not _visual_drafts.has(key):
		var model = preload("res://scripts/labs/inscription_loadout_draft.gd").new()
		var document: Dictionary = _fixture.duplicate(true)
		document.character.name = "Example character %s" % (_actor_picker.selected + 1)
		if _actor_picker.selected == 1 and _scope_picker.selected == 0:
			for item in document.inscriptions: item.instanceId = "second_" + str(item.instanceId)
			for preset in document.presets:
				for slot in preset.assignments: preset.assignments[slot] = "second_" + str(preset.assignments[slot])
		model.configure(document)
		if _actor_picker.selected == 1 and _scope_picker.selected == 1: model.clear()
		_visual_drafts[key] = model
	draft = _visual_drafts[key]
	_connection_note.text = I18n.text("Offline examples. No engine state is changed.")
	_refresh()
func _actor_selected(index: int) -> void:
	if not remote: _select_visual(); return
	var actor: String = _actor_picker.get_item_text(index)
	_discard_then(func(): _read_actor(actor))
	_actor_picker.select(draft.source.actorIds.find(draft.source.actorId))
func _connect_run() -> void:
	_discard_then(func(): _read_actor(str(GameSession.run.get("playerEntityId", ""))))
func _read_actor(actor: String) -> void:
	_loading = true; _ticket += 1
	var ticket := _ticket
	_connection_note.text = I18n.text("Loading authoritative loadout…")
	_refresh()
	var response: Dictionary = await GameSession.read_loadout(actor)
	if not is_inside_tree() or ticket != _ticket: return
	_loading = false
	if not response.ok:
		_connection_note.text = I18n.text(str(response.get("error", response.get("errorKey", "Loadout unavailable."))))
	else:
		var workspace = preload("res://scripts/application/loadout_workspace.gd").new()
		if workspace.accept(response.data):
			draft = workspace; remote = true; _scope_picker.hide(); _actor_picker.clear()
			for id in draft.source.actorIds: _actor_picker.add_item(id)
			_actor_picker.select(draft.source.actorIds.find(actor))
			_connection_note.text = "%s · %s · %s" % [draft.source.actorId, draft.source.policy.inventoryScope, draft.source.inventoryId]
	_refresh()
func _preview() -> void:
	if not remote or _loading: return
	_loading = true
	var workspace = draft
	var version: int = workspace.generation
	var captured: Dictionary = workspace.assignments.duplicate(true)
	_refresh()
	var response: Dictionary = await GameSession.preview_loadout(workspace.source, captured)
	if not is_inside_tree(): return
	_loading = false
	if draft == workspace:
		if response.ok: workspace.accept_preview(response.data, version, captured)
		else: workspace.preview = {}; workspace.message = str(response.get("error", response.get("errorKey", "Preview unavailable.")))
	_refresh()
func _confirm() -> void:
	if not remote or _loading or not draft.can_confirm(): return
	_loading = true; _refresh()
	var success: bool = await GameSession.apply_loadout(draft.source, draft.assignments)
	if not is_inside_tree(): return
	_loading = false
	if success and GameSession.synchronized: _read_actor(draft.source.actorId)
	else: draft.preview = {}; draft.message = "Equipment not confirmed. Recover / refresh run before retrying."; _refresh()
func _recover() -> void:
	if _loading: return
	_loading = true; _refresh()
	await GameSession.recover_pending()
	if not is_inside_tree(): return
	_loading = false
	if remote and GameSession.synchronized: _read_actor(draft.source.actorId)
	else: _refresh()
func _refresh() -> void:
	if not is_instance_valid(view): return
	var model: Dictionary = draft.snapshot()
	if not remote: model.contextKey = ["visual", _scope_picker.selected, _actor_picker.selected]
	if _loading: model.canEdit = false
	view.present(model)
	_preview_button.visible = remote; _confirm_button.visible = remote
	_preview_button.disabled = _loading or (remote and not draft.dirty())
	_confirm_button.disabled = _loading or not remote or not draft.can_confirm()
	preload("res://scripts/ui/focus_navigation.gd").wire(view)
