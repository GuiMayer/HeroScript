extends VBoxContainer
## Read-only archive browser. It never activates a run unless Continue is chosen.

var router
var entries: Array = []
var selected: Dictionary = {}
var list: ItemList
var details: RichTextLabel
var replay_button: Button
var continue_button: Button
var verify_button: Button
var loading := false

func setup(owner) -> void:
	router = owner
	add_theme_constant_override("separation", 12)
	var header := HBoxContainer.new()
	header.add_child(AppTheme.title(I18n.text("JOURNEY HISTORY"), 30))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(I18n.text("BACK"), router.back_to_menu))
	add_child(header)
	add_child(AppTheme.muted(I18n.text("Every journey is an immutable engine record. Inspect, verify or replay it without changing its outcome.")))
	var body := HSplitContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(body)
	var left := VBoxContainer.new()
	left.custom_minimum_size.x = 410
	left.add_child(AppTheme.muted(I18n.text("RECORDED JOURNEYS"), 13))
	list = ItemList.new()
	list.name = "RunHistoryList"
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.item_selected.connect(_select)
	left.add_child(list)
	body.add_child(AppTheme.panel(left))
	var right := VBoxContainer.new()
	right.add_theme_constant_override("separation", 12)
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	details = RichTextLabel.new()
	details.name = "RunHistoryDetails"
	details.bbcode_enabled = true
	details.fit_content = true
	details.custom_minimum_size.y = 250
	right.add_child(details)
	replay_button = _button(I18n.text("WATCH REPLAY"), _watch)
	replay_button.name = "WatchReplayButton"
	replay_button.disabled = true
	right.add_child(replay_button)
	verify_button = _button(I18n.text("VERIFY INTEGRITY"), _verify)
	verify_button.disabled = true
	right.add_child(verify_button)
	continue_button = _button(I18n.text("CONTINUE JOURNEY"), _continue)
	continue_button.disabled = true
	right.add_child(continue_button)
	var flex := Control.new()
	flex.size_flags_vertical = Control.SIZE_EXPAND_FILL
	right.add_child(flex)
	body.add_child(AppTheme.panel(right))
	call_deferred("_load")

func _load() -> void:
	if loading: return
	loading = true
	details.text = I18n.text("Loading journey history…")
	var response: Dictionary = await GameSession.run_history()
	if not is_inside_tree(): return
	loading = false
	if not response.ok:
		details.text = I18n.error(response)
		return
	entries = response.data.get("items", []).duplicate(true)
	list.clear()
	for run in entries:
		var lifecycle := _lifecycle(str(run.get("lifecycle", "Active")))
		var node := I18n.content_name(str(run.get("currentNodeId", "")), I18n.text("Unknown stop"))
		list.add_item("%s  •  %s\n%s  •  #%s" % [lifecycle, node, I18n.text("Sequence"), int(run.get("sequence", 0))])
	if entries.is_empty():
		details.text = I18n.text("No recorded journeys yet. Start a new journey to create the first entry.")
		return
	_select(0)

func _select(index: int) -> void:
	if index < 0 or index >= entries.size(): return
	selected = entries[index].duplicate(true)
	list.select(index)
	var id := str(selected.get("runId", ""))
	var mode := I18n.content_name(str(selected.get("modeId", "")), str(selected.get("modeId", "")).replace("_", " ").capitalize())
	var node := I18n.content_name(str(selected.get("currentNodeId", "")), I18n.text("Unknown stop"))
	var lifecycle := _lifecycle(str(selected.get("lifecycle", "Active")))
	details.text = "[font_size=26][color=#e7b75d]%s[/color][/font_size]\n\n%s\n%s\n%s\n%s\n[color=#a9a0a8]%s[/color]" % [
		lifecycle,
		I18n.text("Mode: %s") % mode,
		I18n.text("Seed: %s") % str(selected.get("seed", "—")),
		I18n.text("Current stop: %s") % node,
		I18n.text("Recorded commands: %s") % int(selected.get("sequence", 0)),
		I18n.text("Run ID: %s") % id]
	replay_button.disabled = id.is_empty() or int(selected.get("sequence", 0)) < 1
	verify_button.disabled = replay_button.disabled
	continue_button.disabled = str(selected.get("lifecycle", "")).to_lower() != "active"

func _watch() -> void:
	if selected.is_empty(): return
	router.show_run_replay(str(selected.get("runId", "")), true)

func _verify() -> void:
	if selected.is_empty() or loading: return
	loading = true
	verify_button.disabled = true
	router.show_toast(I18n.text("Verifying replay…"))
	var result: Dictionary = await GameSession.verify_run(str(selected.get("runId", "")))
	if not is_inside_tree(): return
	loading = false
	verify_button.disabled = false
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		router.show_toast(I18n.text("Deterministic replay verified.") if valid else I18n.text("Replay diverged."), not valid)
	else:
		router.show_error(I18n.error(result))

func _continue() -> void:
	if selected.is_empty() or loading: return
	loading = true
	continue_button.disabled = true
	router.show_toast(I18n.text("Restoring your journey…"))
	if await GameSession.continue_run(str(selected.get("runId", ""))) and is_inside_tree():
		router.open_game()
	elif is_inside_tree():
		loading = false
		continue_button.disabled = false

func _lifecycle(value: String) -> String:
	return I18n.text({"active": "IN PROGRESS", "completed": "COMPLETED", "abandoned": "ABANDONED"}.get(value.to_lower(), value.to_upper()))

func _button(text: String, action: Callable) -> Button:
	var value := AppTheme.button(text)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
