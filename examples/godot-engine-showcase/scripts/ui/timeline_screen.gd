@tool
extends "res://scripts/ui/screen_view.gd"

const TimelinePresenter = preload("res://scripts/presentation/timeline_presenter.gd")
var router
var model := TimelinePresenter.new()
var entries: Array = []
var list: ItemList
var details: RichTextLabel
var selected_sequence := -1
var branch_key: LineEdit
var tree_view: Tree
var scrubber: HSlider
var history_view
var historical_cache := {}
var request_epoch := 0
var screen_run_id := ""
var page_loading := false
var working := false
var more_button: Button
var branch_button: Button
var activate_button: Button
var playback
var next_button: Button
var frame_text: RichTextLabel
var history_allowed := false
var fork_allowed := false
var branch_read_allowed := false
var simulation_allowed := false
var selected_entry: Dictionary = {}
var _history_ticket
var _history_widget
var _history_commit: Dictionary = {}
var _causal_view

var _connected := false
func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint():
		mount(ContextType.new()); theme = visual_context.theme
func _bind_nodes() -> void:
	activate_button = get_node("Body/ArchivePanel/Archive/ActivateButton")
	branch_button = get_node("Body/DetailsPanel/Information/BranchControls/BranchButton")
	branch_key = get_node("Body/DetailsPanel/Information/BranchControls/BranchKey")
	details = get_node("Body/DetailsPanel/Information/Details")
	frame_text = get_node("Body/DetailsPanel/Information/FrameText")
	history_view = get_node("Body/DetailsPanel/Information/HistoryView")
	list = get_node("Body/ArchivePanel/Archive/List")
	more_button = get_node("Body/ArchivePanel/Archive/MoreButton")
	next_button = get_node("Body/DetailsPanel/Information/PlaybackControls/NextButton")
	scrubber = get_node("Body/DetailsPanel/Information/Scrubber")
	tree_view = get_node("Body/ArchivePanel/Archive/TreeView")
func setup(owner) -> void:
	router = owner
	mount(owner.visual_context)
	screen_run_id = str(GameSession.run.get("runId", ""))
	history_allowed = GameSession.allows_tool("timeline.inspect_state")
	fork_allowed = GameSession.allows_tool("timeline.branch.create")
	branch_read_allowed = GameSession.allows_tool("timeline.branch.read")
	simulation_allowed = GameSession.allows_tool("simulation.run")
	_bind_nodes()
	theme = visual_context.theme
	get_node("Header/Back").text = I18n.text("BACK TO COMBAT")
	get_node("Header/Title").text = I18n.text("DETERMINISTIC TIMELINE")
	get_node("Body/DetailsPanel/Information/PlaybackControls/Restart").text = I18n.text("RESTART ANIMATIONS")
	get_node("Body/DetailsPanel/Information/PlaybackControls/Inspect").text = I18n.text("Show engine details")
	get_node("Body/DetailsPanel/Information/PlaybackControls/NextButton").text = I18n.text("NEXT FRAME")
	get_node("Body/DetailsPanel/Information/BranchControls/BranchButton").text = I18n.text("BRANCH HERE")
	get_node("Body/DetailsPanel/Information/BranchControls/Simulate").text = I18n.text("SIMULATE CURRENT STATE")
	get_node("Body/DetailsPanel/Information/BranchControls/Verify").text = I18n.text("VERIFY REPLAY")
	get_node("Body/ArchivePanel/Archive/ActivateButton").text = I18n.text("ACTIVATE BRANCH")
	get_node("Body/ArchivePanel/Archive/MoreButton").text = I18n.text("LOAD MORE")
	get_node("Description").text = I18n.text("Inspect history without changing the run. Create or activate a branch to play another path.")
	tree_view.set_column_title(0, I18n.text("Branch"))
	tree_view.set_column_title(1, I18n.text("From command"))
	tree_view.visible = branch_read_allowed; activate_button.visible = branch_read_allowed
	branch_key.placeholder_text = I18n.text("branch-name"); branch_key.visible = fork_allowed
	branch_button.disabled = not fork_allowed; branch_button.visible = fork_allowed
	get_node("Body/DetailsPanel/Information/BranchControls/Simulate").visible = simulation_allowed
	if not _connected:
		_connected = true
		list.item_selected.connect(_select)
		tree_view.item_activated.connect(_activate_selected)
		scrubber.value_changed.connect(func(value): _select(int(value)))
		_wire_button(get_node("Header/Back"), router.open_game)
		_wire_button(get_node("Body/ArchivePanel/Archive/MoreButton"), _load)
		_wire_button(get_node("Body/ArchivePanel/Archive/ActivateButton"), _activate_selected)
		_wire_button(get_node("Body/DetailsPanel/Information/BranchControls/BranchButton"), _branch)
		_wire_button(get_node("Body/DetailsPanel/Information/PlaybackControls/NextButton"), _next_frame)
		_wire_button(get_node("Body/DetailsPanel/Information/PlaybackControls/Restart"), _restart_frames)
		_wire_button(get_node("Body/DetailsPanel/Information/PlaybackControls/Inspect"), _inspect_raw)
		_wire_button(get_node("Body/DetailsPanel/Information/BranchControls/Simulate"), _simulate)
		_wire_button(get_node("Body/DetailsPanel/Information/BranchControls/Verify"), _verify)
	call_deferred("_load")
	if branch_read_allowed: call_deferred("_load_tree")

func _valid() -> bool:
	return is_inside_tree() and screen_run_id == str(GameSession.run.get("runId", ""))

func _load() -> void:
	if page_loading: return
	page_loading = true
	more_button.disabled = true
	var response := await GameSession.timeline(model.next_cursor)
	if not _valid(): return
	page_loading = false
	if not response.ok:
		details.text = I18n.error(response)
		more_button.disabled = false
		return
	model.append_page(response.data)
	entries = model.entries()
	list.clear()
	for entry in entries:
		list.add_item("#%03d • %s %s • %s" % [int(entry.runSequence), I18n.text("Turn"), int(entry.get("turn", 0)), str(entry.get("commandType", ""))])
	scrubber.max_value = maxi(entries.size() - 1, 0)
	more_button.disabled = response.data.get("items", []).is_empty()
	if selected_sequence < 0 and not entries.is_empty(): _select(entries.size() - 1)

func _select(index: int) -> void:
	if index < 0 or index >= entries.size(): return
	request_epoch += 1
	_history_commit = {}
	var ticket := request_epoch
	var entry: Dictionary = entries[index]
	selected_entry = entry.duplicate(true)
	selected_sequence = int(entry.runSequence)
	list.select(index)
	scrubber.set_value_no_signal(index)
	details.text = "#%s • %s %s • %s • %s" % [selected_sequence, I18n.text("Turn"), int(entry.get("turn", 0)), str(entry.get("phase", "")), str(entry.get("actorId", ""))]
	if _history_ticket != null and not _history_ticket.done: _history_ticket.cancel()
	if is_instance_valid(_history_widget): _history_widget.dispose()
	frame_text.text = I18n.text("Loading saved state…")
	for child in history_view.get_children():
		history_view.remove_child(child)
		child.queue_free()
	_history_widget = null
	next_button.disabled = true
	if not history_allowed or not entry.get("stateAvailable", false):
		frame_text.text = I18n.text("Historical inspection is not available in this mode.")
		return
	# Debounce slider motion; requests already sent are discarded by generation.
	await get_tree().create_timer(.08).timeout
	if not _valid() or ticket != request_epoch: return
	var state: Dictionary = historical_cache.get(selected_sequence, {})
	if state.is_empty():
		_history_ticket = GameSession.begin_replay_inspection(screen_run_id, selected_sequence)
		var response: Dictionary = await _history_ticket.result()
		if not _valid() or ticket != request_epoch: return
		if not response.ok:
			frame_text.text = I18n.error(response)
			return
		state = response.data
		if str(state.get("runId", "")) != screen_run_id or int(state.get("sequence", -1)) != selected_sequence: return
		var bytes := JSON.stringify(state).to_utf8_buffer().size()
		if bytes < 8 * 1024 * 1024:
			historical_cache.clear() # One full visual command; timeline headers remain paginated.
			historical_cache[selected_sequence] = state.duplicate(true)
	_history_commit = state
	_mount_history(state)
	_load_causal_events(ticket, selected_sequence)
	frame_text.text = I18n.text("Saved state loaded. Frame playback only changes this inspector.")
	next_button.disabled = not playback.has_frames()

func _mount_history(commit: Dictionary) -> void:
	if is_instance_valid(_history_widget): _history_widget.dispose(); _history_widget.queue_free()
	_history_widget = preload("res://scripts/ui/historical_beat_view.gd").create(visual_context)
	history_view.add_child(_history_widget)
	_history_widget.load_commit(commit, router.presentation, false)
	playback = _history_widget.playback

func _next_frame() -> void:
	if not is_instance_valid(_history_widget): return
	_history_widget.director.advance()
	next_button.disabled = not playback.has_frames()

func _restart_frames() -> void:
	if _history_commit.is_empty(): return
	_mount_history(_history_commit)
	next_button.disabled = not playback.has_frames()
	frame_text.text = I18n.text("Saved state loaded. Frame playback only changes this inspector.")

func _load_tree() -> void:
	if not branch_read_allowed: return
	var response := await GameSession.branch_tree()
	if not _valid(): return
	tree_view.clear()
	if response.ok: _build_tree(response.data)
	else: details.text = I18n.error(response)

func _build_tree(data: Dictionary) -> void:
	var parents := {}
	for row in TimelinePresenter.lineage(data):
		var item := tree_view.create_item(parents.get(str(row.parentRunId)))
		var id := str(row.runId)
		parents[id] = item
		var key := str(row.branchKey) if row.branchKey != null else I18n.text("origin")
		item.set_text(0, ("● " if id == screen_run_id else "") + key)
		item.set_text(1, str(int(row.sourceSequence)) if row.sourceSequence != null else "—")
		item.set_metadata(0, id)
		item.set_tooltip_text(0, id)
		if id == screen_run_id: item.select(0)

func _activate_selected() -> void:
	var item := tree_view.get_selected()
	if not item or working or GameSession.busy: return
	var id := str(item.get_metadata(0))
	if id.is_empty() or id == screen_run_id: return
	working = true
	var accepted := await GameSession.continue_run(id)
	if not is_inside_tree(): return
	working = false
	if accepted: router.open_game()

func _branch() -> void:
	if working or not fork_allowed: return
	if selected_sequence < 0 or branch_key.text.strip_edges().is_empty():
		router.show_error(I18n.text("Select a command and enter a branch name."))
		return
	working = true
	branch_button.disabled = true
	var key := branch_key.text.strip_edges()
	var response := await GameSession.create_branch(selected_sequence, key)
	if not _valid(): return
	working = false
	branch_button.disabled = false
	if not response.ok:
		router.show_error(I18n.error(response))
		return
	var id := str(response.data.get("runId", ""))
	if id.is_empty(): return
	working = true
	var accepted := await GameSession.continue_run(id)
	if not is_inside_tree(): return
	working = false
	if accepted:
		router.show_toast(I18n.text("Branch '%s' activated.") % key)
		router.open_game()

func _verify() -> void:
	if working: return
	working = true
	var result := await GameSession.verify()
	if not _valid(): return
	working = false
	if result.ok: router.show_toast(I18n.text("All hashes match.") if result.data.get("isValid", false) else I18n.text("A divergence was found."))
	else: router.show_error(I18n.error(result))

func _simulate() -> void:
	if working: return
	working = true
	var response := await GameSession.simulate_end_turn()
	if not _valid(): return
	working = false
	if response.ok:
		var dialog := preload("res://scripts/ui/inspection_dialog.gd").create(visual_context)
		dialog.setup(I18n.text("Simulation"), I18n.text("Simulation completed without changing the run."), response.data)
		add_child(dialog)
		dialog.popup_centered()
	else: router.show_error(I18n.error(response))

func _inspect_raw() -> void:
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").create(visual_context)
	dialog.setup(I18n.text("Commands"), details.text, selected_entry)
	add_child(dialog)
	dialog.popup_centered()

func _wire_button(value: Button, action: Callable) -> void:
	value.pressed.connect(func(): GameAudio.ui(); action.call())

func dispose() -> void:
	request_epoch += 1
	if _history_ticket != null and not _history_ticket.done: _history_ticket.cancel()
	if is_instance_valid(_history_widget): _history_widget.dispose()
	historical_cache.clear()

func _load_causal_events(ticket: int, sequence: int) -> void:
	if is_instance_valid(_causal_view): _causal_view.queue_free()
	_causal_view = null
	if not GameSession.allows_tool("debug.card_inspection.full"): return
	var response: Dictionary = await GameSession.gameplay_events(screen_run_id, sequence)
	if not _valid() or ticket != request_epoch or sequence != selected_sequence: return
	if not response.get("ok", false): return
	var data: Dictionary = response.get("data", {})
	var batch: Variant = data.get("batch")
	_causal_view = preload("res://scripts/ui/gameplay_event_view.gd").create()
	history_view.add_child(_causal_view)
	_causal_view.present(batch if batch is Dictionary else {}, I18n.locale)
