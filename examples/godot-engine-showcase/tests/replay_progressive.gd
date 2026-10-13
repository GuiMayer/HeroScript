extends SceneTree
var failures := 0
class Session extends RefCounted:
	var calls: Array = []
	var head := 4001
	var held
	var hold_more := true
	var hold_presentations := false
	var presentation_calls: Array = []
	var presentation_tickets: Dictionary = {}
	func replay_context() -> Array: return ["fixture", 1]
	func begin_replay_page(id: String, cursor: int, limit: int, through: int):
		calls.append([cursor, limit, through])
		var ticket = load("res://scripts/engine/read_ticket.gd").new()
		if cursor > 0 and hold_more: held = ticket; return ticket
		ticket.finish(page(id,cursor,limit)); return ticket
	func page(_id: String, cursor: int, limit: int) -> Dictionary:
		var items: Array = []
		for sequence in range(cursor + 1, mini(cursor + limit, head) + 1): items.append({"sequence": sequence, "stateHash": "h%s" % sequence})
		var next := mini(cursor + limit, head)
		return {"ok": true, "data": {"items": items, "throughSequence": head, "nextCursor": next, "hasMore": next < head}}
	func begin_replay_presentation(id: String, sequence: int):
		presentation_calls.append(sequence)
		var ticket = load("res://scripts/engine/read_ticket.gd").new()
		if hold_presentations: presentation_tickets[sequence] = ticket; return ticket
		ticket.finish({"ok": true, "data": {"runId": id, "sequence": sequence, "stateHash": "h%s" % sequence, "stateSummary": {"lifecycle": "Active"}}}); return ticket
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var session := Session.new()
	var controller = load("res://scripts/ui/controllers/run_replay_controller.gd").new()
	controller.setup(session, "run", root.get_node("I18n")); await controller.start()
	check(controller.selected_index == 0 and controller.entries.size() == 25 and session.calls.size() == 1, "first command does not await later pages")
	check(controller.first_content_ms >= 0 and not controller.presentation_loading, "useful content has separate opening timing")
	await controller.select(24)
	controller.navigate(1)
	check(controller.selected_index == 24 and controller.page_loading and session.calls[1][2] == 4001, "next waits at page boundary with pinned snapshot")
	session.held.finish(session.page("run",25,25))
	for i in 4: await process_frame
	check(controller.selected_index == 25 and controller.entries.size() == 50, "next crosses page without skipping command")
	session.hold_more = false
	while controller.has_more: await controller.load_more()
	check(controller.entries.size() == 4001 and controller.entries.back().sequence == 4001, "more than four thousand commands remain accessible")
	check(session.calls.size() == 161, "explicit hasMore avoids final empty request")
	controller.max_cache_entries = 2
	for index in range(5): await controller.select(index)
	check(controller.commit_cache.size() == 2 and controller.cache_bytes <= controller.max_cache_bytes, "LRU is bounded by count and serialized bytes")
	var reads: int = session.presentation_calls.size()
	await controller.select(4)
	check(session.presentation_calls.size() == reads, "current cache hit needs no request")
	await controller.select(0)
	check(session.presentation_calls.size() == reads + 1, "evicted command reloads canonically")
	session.hold_presentations = true
	controller.select(6); controller.select(6)
	check(session.presentation_calls.count(7) == 1 and controller._pending.size() == 1, "identical in-flight selections are coalesced")
	controller.select(7)
	check(session.presentation_tickets[7].is_cancelled and controller._pending.size() == 1, "new selection cancels obsolete exclusive read")
	session.presentation_tickets[8].finish({"ok": true, "data": {"runId": "run", "sequence": 8, "stateHash": "h8", "stateSummary": {"lifecycle": "Active"}}})
	for i in 3: await process_frame
	check(controller.selected_index == 7 and not controller.presentation_loading and controller._pending.is_empty(), "stale response never overwrites current selection")
	controller.dispose()
	check(controller.entries.is_empty() and controller.commit_cache.is_empty(), "dispose releases viewing data")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout; quit(0 if failures == 0 else 1)
