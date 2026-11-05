extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	var selection = preload("res://scripts/ui/controllers/inline_card_selection.gd").new()
	var requests: Array = []; var plays: Array = []
	selection.preview_requested.connect(func(command, ticket): requests.append({"command": command, "ticket": ticket}))
	selection.ready.connect(func(preview): plays.append(preview))
	for purpose in ["exile-cost", "effect-sacrifice"]:
		selection.start({"cardInstanceId": "origin", "actorId": "hero", "targetIds": []})
		var snapshot := {"command": selection.intent(), "isLegal": false, "evaluation": {"paymentRequirements": [{"componentId": purpose, "paymentId": purpose, "count": 1, "eligibleCardIds": ["fee"]}]}}
		var original := JSON.stringify(snapshot)
		selection.accept({"ok": true, "data": snapshot}, selection.generation)
		selection.pick("invalid")
		check(selection.chosen.is_empty(), purpose + ": ineligible click cannot pay or resolve an effect")
		selection.pick("fee")
		check(requests.back().command.paymentSelections[0].cardInstanceIds == ["fee"], purpose + ": hand click builds the engine selection intent")
		var stale: int = selection.generation
		selection.pick("fee")
		check(selection.intent().paymentSelections.is_empty(), purpose + ": clicking a chosen fee toggles it off")
		var ready := snapshot.duplicate(true); ready.isLegal = true
		selection.accept({"ok": true, "data": ready}, stale)
		check(plays.is_empty(), purpose + ": obsolete preview cannot execute")
		selection.pick("fee")
		ready.command = selection.intent()
		selection.accept({"ok": true, "data": ready}, selection.generation)
		check(plays.size() == 1, purpose + ": only the current legal engine preview can resolve")
		plays.clear()
		selection.cancel()
		selection.accept({"ok": true, "data": ready}, selection.generation - 1)
		check(not selection.active and plays.is_empty(), purpose + ": cancellation rejects late responses")
		check(original == JSON.stringify(snapshot), purpose + ": published snapshot remains immutable")
	print("INLINE CARD SELECTION: ", failures, " failure(s)")
	quit(1 if failures else 0)
