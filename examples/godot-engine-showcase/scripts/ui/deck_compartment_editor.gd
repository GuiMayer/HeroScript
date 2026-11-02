@tool
class_name DeckCompartmentEditor
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

static func create() -> DeckCompartmentEditor:
	return (load("res://scenes/ui/screens/deck_compartment_editor.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null:
		if Engine.is_editor_hint(): visual_context = preload("res://scripts/ui/visual_context.gd").new(null, {}, preload("res://data/themes/ivory_theme.tres"))
		else: visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(preload("res://data/themes/ivory_theme.tres"))
	theme = visual_context.theme
	if appearance.is_empty():
		for path in ["res://data/presentation.json", "res://data/volatile_core_presentation.json", "res://data/world_one_presentation.json"]:
			var overlay = JSON.parse_string(FileAccess.get_file_as_string(path))
			if not overlay is Dictionary: continue
			for category in overlay:
				if not appearance.has(category): appearance[category] = {}
				if overlay[category] is Dictionary: appearance[category].merge(overlay[category], true)
	find_child("ColorRect0", true, false).color = visual_context.styles.NIGHT
	_page = find_child("Page", true, false); _body = find_child("Compartments", true, false)
	_status = find_child("Status", true, false); _apply = find_child("Apply", true, false)
	_target = find_child("Destination", true, false); _discard = get_node("Discard")
	_translate(self)
	_page.get_node("Header/Back").pressed.connect(_back)
	_page.get_node("Actions/Move").pressed.connect(func():
		if _target.selected >= 0: _stage(selected_id, str(_target.get_item_metadata(_target.selected))))
	_page.get_node("Actions/Clear").pressed.connect(func():
		if not busy and not uncertain: workspace.clear(); _render(); _request_preview())
	_page.get_node("Actions/Restore").pressed.connect(func():
		if not busy and not uncertain: workspace.load_snapshot(workspace.source); _render())
	_apply.pressed.connect(_commit)
	_discard.dialog_text = _text("Discard unconfirmed compartment changes?")
	_discard.confirmed.connect(func(): back_requested.emit())
	if service != null: read_state.call_deferred()

func _text(key: String) -> String: return visual_context.translator.text(key)
func _translate(node: Node) -> void:
	if node.has_meta("source_text"): node.text = _text(str(node.get_meta("source_text")))
	for child in node.get_children(): _translate(child)
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
		var card = preload("res://scripts/ui/card_view.gd").create(visual_context)
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
	var panel = preload("res://scenes/ui/components/loadout/compartment_column.tscn").instantiate(); _body.add_child(panel)
	var column: VBoxContainer = panel.get_node("Body")
	column.get_node("Title").text = title
	column.get_node("Title").add_theme_color_override("font_color", visual_context.styles.INK)
	column.get_node("Description").text = description
	panel.set_drag_forwarding(Callable(), func(_at, data):
		return not busy and not uncertain and data is Dictionary and data.get("kind") == "deck_compartment_card" and data.get("runId") == workspace.source.get("runId"),
		func(_at, data): _stage(str(data.cardInstanceId), id))
	return column

func _error(response: Dictionary) -> String:
	if response.get("error") != null: return str(response.error)
	var data = response.get("data")
	return str(data.get("detail", _text("Unable to update compartments"))) if data is Dictionary else _text("Unable to update compartments")
