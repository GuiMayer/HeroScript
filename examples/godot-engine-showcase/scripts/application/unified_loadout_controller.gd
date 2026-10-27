extends RefCounted
## One owner for page operations. Workspaces stage intentions; the session owns all commits/retries.
signal changed
var session
var identity: Array
var inscriptions
var busy := false
var uncertain := false
var message := "Loading equipment…"
var _alive := true
var _reading := false
var compartments
var deck_cards: Array = []
var modifier_items := {}
var choices: Array = []
var _deck_version: Array = []
var _deck_reading := false
var deck_requested := false
var relics: Array = []
var attributes := {}
var info_ready := false

func _init(owner) -> void:
	session = owner; identity = context()
	session.changed.connect(_session_changed)
func context() -> Array:
	var run: Dictionary = session.run
	return [run.get("runId", ""), run.get("playerEntityId", ""), run.get("settingId", run.get("configName", "")), run.get("branchId", "")]
func current() -> bool: return _alive and not str(identity[0]).is_empty() and context() == identity
func can_leave() -> bool: return not busy and not uncertain and not session.has_pending_command
func _session_changed() -> void:
	if not busy and not _reading: refresh()
func refresh() -> void:
	if _reading or busy or not current(): return
	_reading = true
	var response: Dictionary = await session.read_loadout(str(identity[1]))
	if not current(): _reading = false; changed.emit(); return
	if response.get("ok", false) and response.get("data") is Dictionary and valid(response.data):
		var candidate = preload("res://scripts/application/loadout_workspace.gd").new()
		if candidate.accept(response.data):
			if inscriptions != null:
				candidate.selected_item = inscriptions.selected_item; candidate.selected_slot = inscriptions.selected_slot; candidate.search = inscriptions.search
			inscriptions = candidate
	else: inscriptions = null
	var captured := version()
	var info: Dictionary = await session.read_loadout_info()
	if current() and captured == version() and info.get("ok", false):
		relics = info.data.relics.get("relics", []).duplicate(true); attributes = info.data.attributes.duplicate(true); info_ready = true
	elif captured != version() or not current():
		inscriptions = null; attributes = {}; relics = []; info_ready = false
	_reading = false; changed.emit()
	if deck_requested: await ensure_deck()
func valid(snapshot: Dictionary) -> bool:
	return current() and str(snapshot.get("actorId", "")) == str(identity[1]) and session.loadout_context_matches(snapshot)
func editable() -> bool:
	return current() and not busy and not _reading and not _deck_reading and not uncertain and not session.has_pending_command
func edit_inscription(method: String, args: Array = []) -> void:
	if not editable() or inscriptions == null: return
	busy = true; message = "Validating equipment…"; changed.emit()
	# Refresh version before constructing the candidate. A previous module may have committed.
	var response: Dictionary = await session.read_loadout(str(identity[1]))
	if not current(): _finish("Run or character changed. Reopen loadout."); return
	if not response.get("ok", false) or not response.get("data") is Dictionary or not valid(response.data):
		_finish("Equipment changed. Synchronizing…"); return
	var candidate = preload("res://scripts/application/loadout_workspace.gd").new()
	if not candidate.accept(response.data): _finish("Loadout unavailable."); return
	candidate.selected_slot = inscriptions.selected_slot; candidate.selected_item = inscriptions.selected_item
	candidate.callv(method, args)
	if not candidate.dirty(): _finish("No equipment changes."); return
	var draft: Dictionary = candidate.assignments.duplicate(true)
	response = await session.preview_loadout(candidate.source, draft)
	if not current(): _finish("Run or character changed. Reopen loadout."); return
	if not response.get("ok", false) or not response.get("data") is Dictionary or not candidate.accept_preview(response.data, candidate.generation, draft):
		_finish(str(response.get("error", "Equipment was not changed."))); return
	message = "Saving equipment…"; changed.emit()
	var ok: bool = await session.apply_loadout(candidate.source, draft)
	uncertain = session.has_pending_command
	_finish("Equipment saved automatically." if ok else "Equipment not saved. Recover or retry the operation.")
func _finish(status: String) -> void:
	busy = false; message = status
	_deck_version = []
	if current() and not uncertain: await refresh()
	changed.emit()
func version() -> Array:
	var run: Dictionary = session.run
	return [run.get("sequence"), run.get("step"), run.get("contentRevision")]
func ensure_deck() -> void:
	deck_requested = true
	if not current() or busy or _reading or _deck_reading or uncertain or _deck_version == version(): return
	_deck_reading = true
	var captured := version()
	var response: Dictionary = await session.load_collection_presentations()
	var zones: Dictionary = await session.read_deck_compartments()
	if current() and captured == version():
		if response.get("ok", false) and response.get("data") is Dictionary:
			deck_cards = response.data.get("cards", []).duplicate(true)
		if zones.get("ok", false) and zones.get("data") is Dictionary and valid(zones.data):
			compartments = preload("res://scripts/application/deck_compartment_workspace.gd").new(); compartments.load_snapshot(zones.data)
		else: compartments = null
		modifier_items = session.modifier_items.duplicate(true); choices = session.activity_choices().duplicate(true)
		if response.get("ok", false): _deck_version = captured
		else: message = "Deck unavailable. Retry the read."
	_deck_reading = false; changed.emit()
func move_card(card_id: String, target: String, clear := false) -> void:
	if not editable() or compartments == null: return
	busy = true; message = "Validating equipment…"; changed.emit()
	var response: Dictionary = await session.read_deck_compartments()
	if not current() or not response.get("ok", false) or not response.get("data") is Dictionary or not valid(response.data):
		_finish("Equipment changed. Synchronizing…"); return
	var candidate = preload("res://scripts/application/deck_compartment_workspace.gd").new(); candidate.load_snapshot(response.data)
	if not candidate.source.get("canChange", false): _finish("Equipment cannot be changed here."); return
	if clear: candidate.clear()
	elif not candidate.stage(card_id, target): _finish("Equipment was not changed."); return
	if not candidate.dirty(): _finish("No equipment changes."); return
	var allocation: Array = candidate.allocations()
	response = await session.preview_deck_compartments(candidate.source, allocation)
	if not current() or not response.get("ok", false) or not response.get("data") is Dictionary or not response.data.get("after") is Dictionary or not candidate.accept_preview(response.data.after, candidate.revision):
		_finish(str(response.get("error", "Equipment was not changed."))); return
	message = "Saving equipment…"; changed.emit()
	var ok: bool = await session.apply_deck_compartments(candidate.source, allocation)
	uncertain = session.has_pending_command
	_finish("Equipment saved automatically." if ok else "Equipment not saved. Recover or retry the operation.")
func apply_modifier(reference: String) -> void:
	if not editable(): return
	var matches: Array = session.activity_choices().filter(func(choice): return str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM" and preload("res://scripts/presentation/activity_presenter.gd").option_reference(choice) == reference)
	if matches.size() != 1: return
	busy = true; message = "Validating equipment…"; changed.emit()
	var preview: Dictionary = await session.preview_transformation(matches[0])
	if not current() or preview.is_empty() or not preview.get("isCompatible", false): _finish("Equipment was not changed."); return
	var ok: bool = await session.submit_activity(matches[0])
	uncertain = session.has_pending_command
	_finish("Equipment saved automatically." if ok else "Equipment not saved. Recover or retry the operation.")
func recover() -> void:
	if busy or not current(): return
	busy = true; message = "Recovering equipment…"; changed.emit()
	var ok: bool = await session.recover_pending()
	uncertain = session.has_pending_command
	_finish("Equipment synchronized." if ok else "Equipment recovery failed. Retry pending operation.")
func dispose() -> void:
	_alive = false
	if session.changed.is_connected(_session_changed): session.changed.disconnect(_session_changed)

