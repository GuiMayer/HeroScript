extends RefCounted
## Run editor adapter. The existing session owns every command, retry and accepted snapshot.
var session
func _init(owner) -> void: session = owner
func read() -> Dictionary: return await session.read_deck_compartments()
func preview(snapshot: Dictionary, allocations: Array) -> Dictionary: return await session.preview_deck_compartments(snapshot, allocations)
func apply(snapshot: Dictionary, allocations: Array) -> Dictionary:
	if str(session.run.get("runId", "")) != str(snapshot.get("runId", "")): return {"ok": false, "status": 409, "error": "Run changed. Reopen the editor."}
	var ok: bool = await session.recover_pending() if session.has_pending_command else await session.apply_deck_compartments(snapshot, allocations)
	return {"ok": ok, "status": 0 if session.has_pending_command else 400, "error": "Recover the pending command." if session.has_pending_command else "Build could not be applied. Reopen to review the current version."}
