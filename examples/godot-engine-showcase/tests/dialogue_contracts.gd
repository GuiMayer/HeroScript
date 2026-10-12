extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var t = load("res://scripts/ui/visual_context.gd").new().translator
	var dialogue := {"dialogueInstanceId": "active", "dialogueId": "story", "nodeId": "current", "activityNodeId": "stop", "contentRevision": "revision",
		"title": {"text": "Story"}, "speaker": {"text": "Keeper"}, "text": {"text": "Read"}, "choices": [{"choiceId": "same", "text": {"text": "Pay"}, "available": true, "costs": [{"resourceId": "custom", "amount": 9}]}]}
	var run := {"runId": "run", "branchId": "branch", "sequence": 4, "currentNodeId": "stop", "dialogues": [dialogue]}
	var option := {"type": "CHOOSE_DIALOGUE_OPTION", "subjectId": "same", "payload": {"dialogueInstanceId": "active", "nodeId": "current", "choiceId": "same"}}
	var presenter = load("res://scripts/presentation/dialogue_presenter.gd")
	var model: Dictionary = presenter.build(run, [option], t)
	check(model.dialogueInstanceId == "active" and model.nodeId == "current" and model.contentRevision == "revision" and model.sequence == 4 and model.branchId == "branch", "dialogue projection retains complete context identity")
	check(model.options[0].interactionKind == "Choice" and model.options[0].cost.contains("9") and model.options[0].available, "single costly option remains a choice, never inferred Continue")
	var stale: Dictionary = option.duplicate(true); stale.payload.nodeId = "previous"
	check(not presenter.build(run, [stale], t).options[0].available, "choice ID from an old node cannot authorize the current option")
	stale.payload.dialogueInstanceId = "other"
	check(presenter.build(run, [stale], t).is_empty(), "other instance with the same choice ID cannot become the active dialogue")
	dialogue.choices[0].interactionKind = "Continue"
	check(presenter.build(run, [option], t).options[0].interactionKind == "Continue", "Continue is explicit published metadata")
	model.options[0].command.payload.choiceId = "modified"
	check(option.payload.choiceId == "same", "projection never exposes mutable command source references")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
