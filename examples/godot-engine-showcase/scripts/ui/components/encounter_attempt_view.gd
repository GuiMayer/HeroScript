@tool
extends VBoxContainer
## Read-only attempt presentation; commands and build permissions come from the server.
signal adjust_requested
var _bound := false
func apply_projection(run: Dictionary, can_adjust: bool, context) -> void:
	if not _bound:
		get_node("AdjustBuild").pressed.connect(func(): adjust_requested.emit())
		_bound = true
	var attempt = run.get("encounterAttempt")
	visible = attempt is Dictionary and (bool(attempt.get("awaitingConfirmation", false)) or bool(attempt.get("pendingRetry", false)))
	if not visible: return
	var pending := bool(attempt.get("pendingRetry", false))
	get_node("Title").text = context.translator.text("ATTEMPT %s · %s") % [str(int(attempt.get("number", 1))), context.translator.content_name(str(attempt.get("outcome", "")))]
	get_node("Description").text = context.translator.text("Your campaign and build are preserved. Adjust your build or try this encounter again." if pending else "Confirm the result to prepare another attempt. Your campaign will continue.")
	var reason := str(attempt.get("outcomeRuleId", "")) if attempt.get("outcomeRuleId") != null else ""
	get_node("Reason").visible = not reason.is_empty()
	get_node("Reason").text = context.translator.text("Outcome rule: %s") % context.translator.content_name(reason, reason.replace(".", " ").capitalize())
	get_node("AdjustBuild").visible = pending and can_adjust
	get_node("AdjustBuild").text = context.translator.text("ADJUST BUILD")
