extends VBoxContainer

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

func setup(owner) -> void:
	router = owner
	screen_run_id = str(GameSession.run.get("runId", ""))
	history_allowed = GameSession.allows_tool("timeline.inspect_state")
	fork_allowed = GameSession.allows_tool("timeline.branch.create")
	branch_read_allowed = GameSession.allows_tool("timeline.branch.read")
	simulation_allowed = GameSession.allows_tool("simulation.run")
	add_theme_constant_override("separation", 8)
	var header := HBoxContainer.new()
	header.add_child(AppTheme.title(I18n.text("DETERMINISTIC TIMELINE"), 28))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(I18n.text("BACK TO COMBAT"), router.open_game))
	add_child(header)
	add_child(AppTheme.muted(I18n.text("Inspect history without changing the run. Create or activate a branch to play another path.")))
	var body := HSplitContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(body)
	var left := VBoxContainer.new()
	left.custom_minimum_size.x = 330
	list = ItemList.new()
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.item_selected.connect(_select)
	left.add_child(list)
	more_button = _button(I18n.text("LOAD MORE"), _load)
	left.add_child(more_button)
	tree_view = Tree.new()
	tree_view.custom_minimum_size.y = 190
	tree_view.columns = 2
	tree_view.set_column_title(0, I18n.text("Branch"))
	tree_view.set_column_title(1, I18n.text("From command"))
	tree_view.column_titles_visible = true
	tree_view.item_activated.connect(_activate_selected)
	tree_view.visible = branch_read_allowed
	left.add_child(tree_view)
	activate_button = _button(I18n.text("ACTIVATE BRANCH"), _activate_selected)
	activate_button.visible = branch_read_allowed
	left.add_child(activate_button)
	body.add_child(AppTheme.panel(left))
	var right := VBoxContainer.new()
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	details = RichTextLabel.new()
	details.custom_minimum_size.y = 68
	right.add_child(details)
	scrubber = HSlider.new()
	scrubber.step = 1
	scrubber.value_changed.connect(func(value): _select(int(value)))
	right.add_child(scrubber)
	history_view = preload("res://scripts/ui/historical_combat_view.gd").new()
	history_view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	right.add_child(history_view)
	frame_text = RichTextLabel.new()
	frame_text.custom_minimum_size.y = 100
	frame_text.selection_enabled = true
	right.add_child(frame_text)
	var playback_row := HFlowContainer.new()
	playback_row.add_child(_button(I18n.text("RESTART ANIMATIONS"), _restart_frames))
	next_button = _button(I18n.text("NEXT FRAME"), _next_frame)
	playback_row.add_child(next_button)
	playback_row.add_child(_button(I18n.text("Show engine details"), _inspect_raw))
	right.add_child(playback_row)
	var actions := HFlowContainer.new()
	branch_key = LineEdit.new()
	branch_key.custom_minimum_size.x = 180
	branch_key.placeholder_text = I18n.text("branch-name")
	branch_key.text = "alternative"
	branch_key.visible = fork_allowed
	actions.add_child(branch_key)
	branch_button = _button(I18n.text("BRANCH HERE"), _branch)
	branch_button.disabled = not fork_allowed
	branch_button.visible = fork_allowed
	actions.add_child(branch_button)
	var simulate_button := _button(I18n.text("SIMULATE CURRENT STATE"), _simulate)
	simulate_button.visible = simulation_allowed
	actions.add_child(simulate_button)
	actions.add_child(_button(I18n.text("VERIFY REPLAY"), _verify))
	right.add_child(actions)
	body.add_child(AppTheme.panel(right))
	playback = preload("res://scripts/presentation/playback.gd").new()
	add_child(playback)
	playback.frame_presented.connect(_frame_presented)
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
	var ticket := request_epoch
	var entry: Dictionary = entries[index]
	selected_entry = entry.duplicate(true)
	selected_sequence = int(entry.runSequence)
	list.select(index)
	scrubber.set_value_no_signal(index)
	details.text = "#%s • %s %s • %s • %s" % [selected_sequence, I18n.text("Turn"), int(entry.get("turn", 0)), str(entry.get("phase", "")), str(entry.get("actorId", ""))]
	playback.clear()
	frame_text.text = I18n.text("Loading saved state…")
	for child in history_view.get_children():
		history_view.remove_child(child)
		child.queue_free()
	history_view.presenter = null
	next_button.disabled = true
	if not history_allowed or not entry.get("stateAvailable", false):
		frame_text.text = I18n.text("Historical inspection is not available in this mode.")
		return
	# Debounce slider motion; requests already sent are discarded by generation.
	await get_tree().create_timer(.08).timeout
	if not _valid() or ticket != request_epoch: return
	var state: Dictionary = historical_cache.get(selected_sequence, {})
	if state.is_empty():
		var response := await GameSession.historical_state(selected_sequence)
		if not _valid() or ticket != request_epoch: return
		if not response.ok:
			frame_text.text = I18n.error(response)
			return
		state = response.data
		historical_cache[selected_sequence] = state.duplicate(true)
		if historical_cache.size() > 32: historical_cache.erase(historical_cache.keys()[0])
	history_view.display(state, router.presentation)
	frame_text.text = I18n.text("Saved state loaded. Frame playback only changes this inspector.")
	if entry.get("resolutionCommandId") != null:
		var resolution := await GameSession.resolution(str(entry.resolutionCommandId))
		if not _valid() or ticket != request_epoch: return
		if resolution.ok:
			playback.load_receipt({"state": {"resolution": resolution.data}})
		else:
			frame_text.text = I18n.error(resolution)
	next_button.disabled = not playback.has_frames()

func _next_frame() -> void:
	playback.advance()
	next_button.disabled = not playback.has_frames()

func _restart_frames() -> void:
	playback.index = 0
	next_button.disabled = not playback.has_frames()
	frame_text.text = I18n.text("Saved state loaded. Frame playback only changes this inspector.")

func _frame_presented(frame: Dictionary, index: int, total: int) -> void:
	frame_text.text = "%s/%s • %s" % [index + 1, total, str(frame.get("transitionType", ""))]
	if history_view.presenter == null: return
	for application in frame.get("applications", []):
		frame_text.text += "\n" + history_view.presenter.application_text(application, true)
		var id := str(application.get("targetEntityId", ""))
		if history_view.actor_panels.has(id) and not Preferences.reduced_motion:
			history_view.actor_panels[id].portrait_view.flash_hit()

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
		var dialog := preload("res://scripts/ui/inspection_dialog.gd").new()
		dialog.setup(I18n.text("Simulation"), I18n.text("Simulation completed without changing the run."), response.data)
		add_child(dialog)
		dialog.popup_centered()
	else: router.show_error(I18n.error(response))

func _inspect_raw() -> void:
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new()
	dialog.setup(I18n.text("Commands"), details.text, selected_entry)
	add_child(dialog)
	dialog.popup_centered()

func _button(text: String, action: Callable) -> Button:
	var value := AppTheme.button(text)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
