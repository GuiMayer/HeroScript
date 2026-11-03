class_name GameplayRouteResolver
extends RefCounted
## Pure route and navigation helper extracted from main.gd.

const DETAILED_ACTIVITY_CHOICES := [
	"PICK_CARD_REWARD",
	"DECOMPOSE_CARD_REWARD",
	"BUY_SHOP_ITEM",
	"APPLY_PREPARATION_OPTION",
	"UPGRADE_CARD",
	"REMOVE_CARD_TRANSFORMATION",
	"REPLACE_CARD_TRANSFORMATION",
	"ACQUIRE_RELIC",
	"OPEN_INSCRIPTION_OFFER",
	"CHOOSE_INSCRIPTION_OFFER",
]


static func has_dialogue_activity(run: Dictionary) -> bool:
	if str(run.get("lifecycle", "Active")).to_lower() != "active":
		return false
	var id := str(run.get("currentNodeId", ""))
	if id in run.get("map", {}).get("resolvedNodeIds", []):
		return false
	for node in run.get("map", {}).get("nodes", []):
		if str(node.get("nodeId", "")) == id:
			return str(node.get("activity", {}).get("type", "")) == "Dialogue"
	return false


static func resolve_route(run: Dictionary, combat: Dictionary, choices: Array) -> String:
	if run.is_empty():
		return "menu"
	if not combat.is_empty() and str(combat.get("status", "ACTIVE")) == "ACTIVE":
		return "combat"
	var attempt = run.get("encounterAttempt")
	if attempt is Dictionary and (bool(attempt.get("awaitingConfirmation", false)) or bool(attempt.get("pendingRetry", false))):
		return "activity"
	if has_dialogue_activity(run):
		return "dialogue"
	var types: Array = choices.map(func(choice): return str(choice.get("type", "")))
	var detailed := types.any(func(type): return type in DETAILED_ACTIVITY_CHOICES)
	return "activity" if detailed or str(run.get("lifecycle", "Active")).to_lower() != "active" else "map"
