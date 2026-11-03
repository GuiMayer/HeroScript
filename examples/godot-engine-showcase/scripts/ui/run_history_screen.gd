@tool
extends "res://scripts/ui/screen_view.gd"
## Read-only archive browser. It never activates a run unless Continue is chosen.

const ScreenContext = preload("res://scripts/ui/context/screen_context.gd")
var _router
var _screen_context
var router:
	get: return _router
	set(value):
		_router = value
		_screen_context = ScreenContext.ensure(value)
var screen_context:
	get:
		if _screen_context == null:
			_screen_context = ScreenContext.ensure(_router)
		return _screen_context
	set(value):
		_screen_context = value
		if value != null and value.router != null:
			_router = value.router
var entries: Array = []
var selected: Dictionary = {}
var list: ItemList
var details: RichTextLabel
var replay_button: Button
var continue_button: Button
var verify_button: Button
var loading := false
var unlock_summary: Label

var _connected := false
func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint():
		mount(ContextType.new()); theme = visual_context.theme
func _bind_nodes() -> void:
	continue_button = get_node("Body/DetailsPanel/Information/ContinueButton")
	details = get_node("Body/DetailsPanel/Information/RunHistoryDetails")
	list = get_node("Body/ArchivePanel/Archive/RunHistoryList")
	replay_button = get_node("Body/DetailsPanel/Information/WatchReplayButton")
	unlock_summary = get_node("ProfileUnlockSummary")
	verify_button = get_node("Body/DetailsPanel/Information/VerifyButton")
func setup(owner) -> void:
	screen_context = ScreenContext.ensure(owner)
	if _router == null: _router = owner
	mount(screen_context.visual_context)
	_bind_nodes()
	theme = visual_context.theme
	get_node("Header/Back").text = I18n.text("BACK")
	get_node("Header/Title").text = I18n.text("JOURNEY HISTORY")
	get_node("Body/DetailsPanel/Information/ContinueButton").text = I18n.text("CONTINUE JOURNEY")
	get_node("Body/DetailsPanel/Information/WatchReplayButton").text = I18n.text("WATCH REPLAY")
	get_node("Body/DetailsPanel/Information/VerifyButton").text = I18n.text("VERIFY INTEGRITY")
	get_node("Body/ArchivePanel/Archive/Heading").text = I18n.text("RECORDED JOURNEYS")
	get_node("Description").text = I18n.text("Every journey is an immutable engine record. Inspect, verify or replay it without changing its outcome.")
	get_node("SettingName").text = I18n.text(str(GameSession.selected_setting.get("displayName", GameSession.selected_setting_id)))
	if not _connected:
		_connected = true
		list.item_selected.connect(_select)
		_wire_button(get_node("Header/Back"), func(): screen_context.back_to_menu())
		_wire_button(get_node("Body/DetailsPanel/Information/WatchReplayButton"), _watch)
		_wire_button(get_node("Body/DetailsPanel/Information/ContinueButton"), _continue)
		_wire_button(get_node("Body/DetailsPanel/Information/VerifyButton"), _verify)
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
	var options: Array = response.data.get("availability", {}).get("options", [])
	var available := options.filter(func(option): return not option.get("availableTargets", []).is_empty())
	unlock_summary.text = I18n.text("Unlocked options: %s / %s") % [available.size(), options.size()]
	var names: Array[String] = []
	for option in options:
		names.append("%s · %s" % [str(option.get("displayName", option.get("unlockId", ""))), I18n.text("Available") if not option.get("availableTargets", []).is_empty() else I18n.text("Locked")])
	unlock_summary.tooltip_text = "\n".join(names)
	# Defense in depth for malformed/stale projections: never show another setting.
	entries = entries.filter(func(record): return str(record.get("settingId", record.get("configName", ""))) == GameSession.selected_setting_id)
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
	details.text = ("[font_size=26]" + AppTheme.color_tag("accent.primary") + "%s[/color][/font_size]\n\n%s\n%s\n%s\n%s\n" + AppTheme.color_tag("text.secondary") + "%s[/color]") % [
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
	screen_context.navigation.show_run_replay(str(selected.get("runId", "")), true)

func _verify() -> void:
	if selected.is_empty() or loading: return
	loading = true
	verify_button.disabled = true
	screen_context.show_toast(I18n.text("Verifying replay…"))
	var result: Dictionary = await GameSession.verify_run(str(selected.get("runId", "")))
	if not is_inside_tree(): return
	loading = false
	verify_button.disabled = false
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		screen_context.show_toast(I18n.text("Deterministic replay verified.") if valid else I18n.text("Replay diverged."), not valid)
	else:
		screen_context.show_error(I18n.error(result))

func _continue() -> void:
	if selected.is_empty() or loading: return
	loading = true
	continue_button.disabled = true
	screen_context.show_toast(I18n.text("Restoring your journey…"))
	if await GameSession.continue_run(str(selected.get("runId", ""))) and is_inside_tree():
		screen_context.open_game()
	elif is_inside_tree():
		loading = false
		continue_button.disabled = false

func _lifecycle(value: String) -> String:
	return I18n.text({"active": "IN PROGRESS", "completed": "COMPLETED", "abandoned": "ABANDONED"}.get(value.to_lower(), value.to_upper()))

func _wire_button(value: Button, action: Callable) -> void:
	value.pressed.connect(func(): GameAudio.ui(); action.call())
