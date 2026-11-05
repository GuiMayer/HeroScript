extends SceneTree

class Presenter extends RefCounted:
	var actions: Array = []
	func system_candidate(types: Array) -> Dictionary:
		for action in actions:
			if action.command.actionType in types: return action
		return {}

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	var view = load("res://scripts/ui/combat_view.gd").new()
	var presenter := Presenter.new()
	presenter.actions = [{"source": "System", "command": {"actionType": "PASS"}}]
	view.presenter = presenter
	view.visual_context = load("res://scripts/ui/visual_context.gd").new()
	view.visual_context.setting_id = "prototype-test"
	view.presentation = {"hidden_system_actions": {"prototype-test": ["PASS"]}}
	var ok: bool = view._visible_pass_candidate().is_empty()
	view.visual_context.setting_id = "default"
	ok = ok and view._visible_pass_candidate().get("command", {}).get("actionType") == "PASS"
	view.visual_context.setting_id = "prototype-test"
	presenter.actions.append({"source": "System", "command": {"actionType": "PASS_PRIORITY"}})
	ok = ok and view._visible_pass_candidate().get("command", {}).get("actionType") == "PASS_PRIORITY"
	print("[PASS] MVP hides PASS; other settings and priority retain their actions" if ok else "[FAIL] system action visibility")
	view.free()
	root.get_node("GameAudio").shutdown()
	quit(0 if ok else 1)
