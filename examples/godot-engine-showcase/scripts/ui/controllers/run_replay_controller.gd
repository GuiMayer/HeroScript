extends RefCounted
## Disposable read cursor; never submits game commands.
signal changed
const PAGE_SIZE := 25
var session
var translator
var run_id := ""
var endpoint_context: Array = []
var entries: Array = []
var selected_index := -1
var through_sequence := 0
var next_cursor := 0
var has_more := true
var page_loading := false
var page_error := false
var presentation_loading := false
var disposed := false
var playing := false
var verifying := false
var state_text := ""
var frame_text := ""
var commit_cache: Dictionary = {}
var _page_ticket
var _presentation_ticket
var _epoch := 0
var play_epoch := 0
var started_ms := 0
var first_content_ms := -1
func setup(client, id: String, i18n) -> void:
	session = client; run_id = id; translator = i18n; endpoint_context = session.replay_context()
func valid() -> bool: return not disposed and endpoint_context == session.replay_context()
func model() -> Dictionary:
	return {"entries": entries, "selected_index": selected_index, "through_sequence": through_sequence,
		"has_more": has_more, "page_loading": page_loading, "presentation_loading": presentation_loading,
		"playing": playing, "verifying": verifying, "state_text": state_text, "frame_text": frame_text}
func start() -> void:
	started_ms = Time.get_ticks_msec()
	await load_more()
	if valid() and not entries.is_empty(): await select(0)
func load_more() -> void:
	if not valid() or page_loading or not has_more: return
	page_loading = true; page_error = false; changed.emit()
	_page_ticket = session.begin_replay_page(run_id, next_cursor, PAGE_SIZE, through_sequence)
	var response: Dictionary = await _page_ticket.result()
	if not valid(): return
	page_loading = false
	if not response.get("ok", false):
		page_error = true
		if not response.get("cancelled", false): state_text = translator.error(response)
		changed.emit(); return
	var page: Dictionary = response.data
	var incoming: Array = page.get("items", [])
	var incoming_head := int(page.get("throughSequence", 0))
	if through_sequence != 0 and through_sequence != incoming_head:
		page_error = true
		state_text = translator.text("Replay snapshot changed."); changed.emit(); return
	through_sequence = incoming_head
	var expected := next_cursor + 1
	for item in incoming:
		if int(item.get("sequence", 0)) != expected:
			page_error = true
			state_text = translator.text("Replay page is not contiguous."); changed.emit(); return
		expected += 1
	var cursor := int(page.get("nextCursor", next_cursor))
	if cursor != expected - 1 or (page.get("hasMore", false) and incoming.is_empty()):
		page_error = true
		state_text = translator.text("Replay page is not contiguous."); changed.emit(); return
	entries.append_array(incoming)
	next_cursor = cursor; has_more = bool(page.get("hasMore", false))
	if entries.is_empty(): state_text = translator.text("This journey has no recorded commands.")
	changed.emit()
func select(index: int) -> void:
	if not valid() or index < 0 or index >= entries.size(): return
	_epoch += 1
	var epoch := _epoch
	if _presentation_ticket != null and not _presentation_ticket.done: _presentation_ticket.cancel()
	selected_index = index
	var entry: Dictionary = entries[index]
	frame_text = preload("res://scripts/presentation/run_replay_presenter.gd").frames_text(entry, translator)
	state_text = translator.text("Loading saved state…"); presentation_loading = true; changed.emit()
	var sequence := int(entry.sequence)
	var response: Dictionary = {"ok": true, "data": commit_cache[sequence]} if commit_cache.has(sequence) else {}
	if response.is_empty():
		_presentation_ticket = session.begin_replay_presentation(run_id, sequence)
		response = await _presentation_ticket.result()
	if not valid() or epoch != _epoch: return
	presentation_loading = false
	if not response.get("ok", false):
		state_text = translator.text("Historical inspection is unavailable in this mode.") if response.get("status", 0) == 403 else translator.error(response)
		if response.get("status", 0) == 403 and first_content_ms < 0: first_content_ms = Time.get_ticks_msec() - started_ms
		changed.emit(); return
	var projection: Dictionary = response.data
	if str(projection.get("runId", "")) != run_id or int(projection.get("sequence", 0)) != sequence or projection.get("stateHash", "") != entry.get("stateHash", ""):
		state_text = translator.text("Replay presentation does not match its command."); changed.emit(); return
	commit_cache[sequence] = projection
	state_text = preload("res://scripts/presentation/run_replay_presenter.gd").summary_text(projection, translator)
	var transcript: String = preload("res://scripts/presentation/run_replay_presenter.gd").transcript_text(projection, translator)
	if not transcript.is_empty(): frame_text = transcript
	if first_content_ms < 0: first_content_ms = Time.get_ticks_msec() - started_ms
	changed.emit()
func navigate(direction: int) -> bool:
	if not valid(): return false
	var index := maxi(0, selected_index + direction)
	if index >= entries.size() and has_more:
		await load_more()
	if not valid() or index >= entries.size() or index == selected_index: return false
	await select(index)
	return valid() and not page_error
func stop() -> void:
	playing = false; _epoch += 1; play_epoch += 1; changed.emit()
func dispose() -> void:
	if disposed: return
	disposed = true; playing = false; _epoch += 1
	if _page_ticket != null and not _page_ticket.done: _page_ticket.cancel()
	if _presentation_ticket != null and not _presentation_ticket.done: _presentation_ticket.cancel()
	commit_cache.clear(); entries.clear()
