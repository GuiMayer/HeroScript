class_name SessionCommandDispatcher
extends RefCounted
## Command preparation, candidate mapping, and receipt validation helpers for GameSession.

static func candidate_command_type(candidate: Dictionary) -> String:
	var command_data: Dictionary = candidate.get("command", {})
	if str(candidate.get("source", "")) == "Card":
		return "PLAY_CARD"
	if str(command_data.get("actionType", "")) == "END_TURN":
		return "END_TURN"
	return "EXECUTE_ACTION"


static func normalize_receipt_response(response: Dictionary) -> Dictionary:
	if response.get("ok", false) and (
		not response.get("data") is Dictionary
		or not typeof(response.data.get("sequence")) in [TYPE_INT, TYPE_FLOAT]
	):
		# A 2xx malformed receipt is uncertain, not permission to send a new write.
		return {"ok": false, "status": 0, "errorKey": "The engine returned an invalid JSON response."}
	return response


static func should_clear_pending_on_failure(response: Dictionary) -> bool:
	var status := int(response.get("status", 0))
	return status > 0 and status < 500
