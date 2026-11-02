@tool
extends VBoxContainer
## Passive replay interface. No session, transport, commands or playback authority.
signal intent_requested(action: String, arguments: Array)
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
var list: ItemList
var heading: Label
var progress: ProgressBar
var state_text: RichTextLabel
var frame_text: RichTextLabel
var play_button: Button
var previous_button: Button
var next_button: Button
var verify_button: Button
var more_button: Button
var inspect_button: Button
var watch_button: Button
var visual_slot: VBoxContainer
var visual_scroll: ScrollContainer
var loaded_count: Label
var _last_selected := -2

var _bound := false
static func create(): return load("res://scenes/ui/components/run_replay_view.tscn").instantiate()
func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint():
		visual_context = ContextType.new(); theme = visual_context.theme
func _bind_nodes() -> void:
	frame_text = get_node("Body/DetailsPanel/Information/FrameText")
	heading = get_node("Body/DetailsPanel/Information/Heading")
	inspect_button = get_node("Body/DetailsPanel/Information/PlaybackControls/InspectButton")
	list = get_node("Body/ArchivePanel/Archive/ReplayCommandList")
	loaded_count = get_node("Body/ArchivePanel/Archive/LoadedCount")
	more_button = get_node("Body/ArchivePanel/Archive/MoreButton")
	next_button = get_node("Body/DetailsPanel/Information/PlaybackControls/NextButton")
	play_button = get_node("Body/DetailsPanel/Information/PlaybackControls/ReplayPlayButton")
	previous_button = get_node("Body/DetailsPanel/Information/PlaybackControls/PreviousButton")
	progress = get_node("Body/DetailsPanel/Information/ReplayProgress")
	state_text = get_node("Body/DetailsPanel/Information/StateText")
	verify_button = get_node("Body/DetailsPanel/Information/PlaybackControls/VerifyButton")
	visual_scroll = get_node("Body/DetailsPanel/Information/VisualScroll")
	visual_slot = get_node("Body/DetailsPanel/Information/VisualScroll/VisualSlot")
	watch_button = get_node("Body/DetailsPanel/Information/PlaybackControls/WatchButton")
func setup(context, from_history := false) -> void:
	visual_context = context; theme = context.theme; _bind_nodes()
	get_node("Header/Back").text = context.translator.text("BACK TO RESULT")
	get_node("Header/Title").text = context.translator.text("JOURNEY REPLAY")
	get_node("Body/DetailsPanel/Information/PlaybackControls/Restart").text = context.translator.text("RESTART")
	get_node("Body/DetailsPanel/Information/PlaybackControls/InspectButton").text = context.translator.text("Show engine details")
	get_node("Body/DetailsPanel/Information/PlaybackControls/NextButton").text = context.translator.text("NEXT")
	get_node("Body/DetailsPanel/Information/PlaybackControls/ReplayPlayButton").text = context.translator.text("PLAY")
	get_node("Body/DetailsPanel/Information/PlaybackControls/PreviousButton").text = context.translator.text("PREVIOUS")
	get_node("Body/DetailsPanel/Information/PlaybackControls/VerifyButton").text = context.translator.text("VERIFY INTEGRITY")
	get_node("Body/DetailsPanel/Information/PlaybackControls/WatchButton").text = context.translator.text("Watch combat")
	get_node("Body/DetailsPanel/Information/Heading").text = context.translator.text("Loading replay…")
	get_node("Body/ArchivePanel/Archive/Heading").text = context.translator.text("COMMAND TIMELINE")
	get_node("Body/ArchivePanel/Archive/MoreButton").text = context.translator.text("LOAD MORE")
	get_node("Description").text = context.translator.text("This is the immutable recording produced by the engine. Playback only moves a local viewing cursor.")
	get_node("Header/Back").text = context.translator.text("BACK TO HISTORY" if from_history else "BACK TO RESULT")
	if _bound: return
	_bound = true
	list.item_selected.connect(func(index): intent_requested.emit("select", [index]))
	get_node("Header/Back").pressed.connect(func(): intent_requested.emit("back", []))
	get_node("Body/DetailsPanel/Information/PlaybackControls/Restart").pressed.connect(func(): intent_requested.emit("restart", []))
	get_node("Body/DetailsPanel/Information/PlaybackControls/PreviousButton").pressed.connect(func(): intent_requested.emit("previous", []))
	get_node("Body/DetailsPanel/Information/PlaybackControls/NextButton").pressed.connect(func(): intent_requested.emit("next", []))
	get_node("Body/DetailsPanel/Information/PlaybackControls/ReplayPlayButton").pressed.connect(func(): intent_requested.emit("toggle_play", []))
	get_node("Body/DetailsPanel/Information/PlaybackControls/VerifyButton").pressed.connect(func(): intent_requested.emit("verify", []))
	get_node("Body/ArchivePanel/Archive/MoreButton").pressed.connect(func(): intent_requested.emit("more", []))
	get_node("Body/DetailsPanel/Information/PlaybackControls/InspectButton").pressed.connect(func(): intent_requested.emit("inspect", []))
	get_node("Body/DetailsPanel/Information/PlaybackControls/WatchButton").pressed.connect(func(): intent_requested.emit("watch", []))

func apply_model(model: Dictionary) -> void:
	var entries: Array = model.entries
	while list.item_count < entries.size():
		var entry: Dictionary = entries[list.item_count]
		list.add_item("#%03d  •  %s  ·  %s %s" % [int(entry.sequence),
			visual_context.translator.text(str(entry.get("commandType", "")).replace("_", " ").capitalize()), visual_context.translator.text("Step"), int(entry.get("step", 0))])
		list.set_item_tooltip(list.item_count - 1, list.get_item_text(list.item_count - 1))
	if model.selected_index >= 0 and model.selected_index != _last_selected:
		list.select(model.selected_index); list.ensure_current_is_visible()
		_last_selected = model.selected_index
	progress.max_value = maxi(int(model.through_sequence) - 1, 1)
	progress.value = maxi(model.selected_index, 0)
	heading.text = visual_context.translator.text("Loading replay…") if entries.is_empty() else visual_context.translator.text("Command %s of %s") % [model.selected_index + 1, model.through_sequence]
	if model.page_error: heading.text = visual_context.translator.text("Replay unavailable")
	state_text.clear(); state_text.add_text(model.state_text)
	frame_text.clear(); frame_text.add_text(model.frame_text)
	previous_button.disabled = model.selected_index <= 0
	next_button.disabled = entries.is_empty() or (not model.has_more and model.selected_index >= entries.size() - 1)
	play_button.disabled = entries.is_empty()
	play_button.text = visual_context.translator.text("PAUSE REPLAY" if model.playing else "PLAY")
	verify_button.disabled = entries.is_empty() or model.verifying
	more_button.visible = model.has_more
	more_button.disabled = model.page_loading
	loaded_count.text = visual_context.translator.text("%s commands loaded of %s") % [entries.size(), model.through_sequence]
	inspect_button.visible = model.inspection_allowed
	inspect_button.disabled = model.presentation_loading or model.selected_index < 0
	watch_button.visible = model.inspection_allowed; watch_button.disabled = inspect_button.disabled
