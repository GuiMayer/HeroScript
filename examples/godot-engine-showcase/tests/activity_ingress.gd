extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var flow = load("res://scripts/application/activity_choices.gd")
	var entry := {"type": "CREATE_SHOP", "payload": {"shopId": "published"}}
	check(flow.single_entry([entry, {"type": "ABANDON_RUN"}]) == entry, "travel ingress keeps the exact published entry command")
	check(flow.single_entry([entry, {"type": "CREATE_PREPARATION"}]).is_empty(), "multiple entry choices require explicit player input")
	var completion := {"type":"RESOLVE_NODE","payload":{"currentNodeId":"published"}}
	check(flow.only_completion([completion,{"type":"ABANDON_RUN"}]) == completion, "sole published completion is automatically returned unchanged")
	check(flow.only_completion([completion,{"type":"PICK_CARD_REWARD"}]).is_empty(), "unfinished reward is never automatically skipped on map entry")
	check(flow.only_completion([completion,{"type":"BUY_SHOP_ITEM"}]).is_empty(), "entering map to inspect a pending shop never automatically skips the visit")
	for type in ["BUY_SHOP_ITEM", "PICK_CARD_REWARD", "CHOOSE_DIALOGUE_OPTION", "UPGRADE_CARD", "UNKNOWN"]:
		check(flow.single_entry([{"type": type}]).is_empty(), "ingress never chooses a consequence: " + type)
	var context = load("res://scripts/ui/visual_context.gd").new()
	var presenter = load("res://scripts/presentation/activity_presenter.gd").new({}, [], {}, context.translator)
	check(presenter.choice_label({"type": "UNKNOWN_MOD_COMMAND"}) == "UNKNOWN MOD COMMAND", "unknown commands retain a usable generic presentation")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
