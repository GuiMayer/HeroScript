extends SceneTree
func _init() -> void: call_deferred("_run")
func _run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var presenter = preload("res://scripts/presentation/rune_chain_presenter.gd")
	var frame := {"applications": [{"effectType": "APPLY_STATUS", "statusId": "burn", "provenance": {"componentId": "rune.affinity"}}, {"effectType": "MODIFY_RESOURCE", "resourceId": "block", "previousValue": 1, "currentValue": 3, "provenance": {"componentId": "rune.resonance"}}, {"effectType": "CARD_ZONE_FLOW", "cardZoneSteps": [{}], "provenance": {"componentId": "rune.catalyst"}}]}
	var cues: Array = presenter.cues(frame, context.translator)
	assert(cues.size() == 3 and cues[1].contains("+2") and cues[2].contains("Catalyst"))
	assert(presenter.cues({"applications": [{"effectType": "MODIFY_RESOURCE", "previousValue": 3, "currentValue": 3, "provenance": {"componentId": "rune.resonance"}}]}, context.translator).is_empty())
	print("[PASS] chain follows applied visible facts; zero changes are silent")
	quit(0)
