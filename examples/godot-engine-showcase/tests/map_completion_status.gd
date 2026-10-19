extends SceneTree
## Production MapView/RouteView must expose current and completed independently.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	var translator = root.get_node("I18n")
	for language in ["en", "pt_BR"]:
		translator.set_locale(language)
		var context = load("res://scripts/ui/visual_context.gd").new(translator)
		var run := {"runId":"completion-qa", "currentNodeId":"a", "map":{"resolvedNodeIds":[], "nodes":[
			{"nodeId":"a", "activity":{"type":"Shop"}, "nextNodeIds":["b"]},
			{"nodeId":"b", "activity":{"type":"Encounter"}, "nextNodeIds":[]}]}}
		var view = load("res://scripts/ui/map_view.gd").new()
		view.size = Vector2(1200,650); root.add_child(view)
		var intents: Array = []
		view.intent_requested.connect(func(action,args): intents.append([action,args]))
		for state in ["current", "current_completed", "completed", "current_again", "unresolved_again"]:
			run.currentNodeId = "b" if state == "completed" else "a"
			run.map.resolvedNodeIds = [] if state in ["current","unresolved_again"] else ["a"]
			var original := JSON.stringify(run)
			var presenter = load("res://scripts/presentation/activity_presenter.gd").new(run,[],{},translator)
			var old_button = view.route.buttons.get("a") if is_instance_valid(view.route) else null
			view.apply_projection(run,[],presenter,context); await settle()
			view.route.buttons.a.pressed.emit(); await settle()
			var current: bool = run.currentNodeId == "a"
			var resolved: bool = "a" in run.map.resolvedNodeIds
			var caption: String = view.route.buttons.a.get_node("Caption").text
			check(caption.begins_with("● ") == current and caption.contains("✓") == resolved,
				language + "/" + state + ": caption independently shows current marker and completion checkmark")
			var expected: String = translator.text("YOU ARE HERE") if current else ""
			if resolved: expected += (" · " if current else "") + translator.text("COMPLETED")
			var texts: Array = view._inspector.find_children("*","Label",true,false).map(func(label): return label.text)
			check(expected in texts, language + "/" + state + ": inspector exposes both localized states without hiding completion")
			check((old_button == null or old_button == view.route.buttons.a) and JSON.stringify(run) == original and intents.is_empty(),
				language + "/" + state + ": reconciliation preserves button identity, input snapshot and gameplay authority")
		view.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(1 if failures else 0)
