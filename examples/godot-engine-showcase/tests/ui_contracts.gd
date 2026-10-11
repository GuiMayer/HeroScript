extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var Models = load("res://scripts/presentation/view_models.gd")
	var source := {"cardInstanceId": "a", "unknown": {"tags": ["fire"]}}
	var model = Models.Card.new(source)
	source.unknown.tags.append("changed")
	var copy: Dictionary = model.to_dictionary()
	copy.unknown.tags.append("view mutation")
	check(model.instance_id == "a" and model.read("unknown").tags == ["fire"], "model owns defensive copies, retaining extensions")
	var candidate := {"command": {"cardInstanceId": "a", "targetIds": ["enemy"]}}
	var presenter = load("res://scripts/presentation/combat_presenter.gd").new({}, {}, [candidate], root.get_node("I18n"))
	var options: Array = presenter.candidates("a")
	options[0].command.targetIds.clear()
	check(presenter.targets(presenter.candidates("a")[0]) == ["enemy"], "public candidates are defensive")
	var targets: Array = presenter.targets(candidate)
	targets.clear()
	check(candidate.command.targetIds == ["enemy"], "target query never aliases candidate data")
	var query: Dictionary = presenter.candidate_for_card("a")
	query.command.targetIds.clear()
	check(presenter.candidates("a")[0].command.targetIds == ["enemy"], "representative query never aliases snapshot")
	for path in ["res://scripts/presentation/combat_presenter.gd", "res://scripts/presentation/view_models.gd"]:
		var code := FileAccess.get_file_as_string(path)
		check(not code.contains("GameSession") and not code.contains("AppTheme") and not code.contains("extends Control"), "passive presentation boundary: " + path)
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
