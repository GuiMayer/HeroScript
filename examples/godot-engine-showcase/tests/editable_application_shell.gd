extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print(("[PASS] " if ok else "[FAIL] ") + message)
	if not ok: failures += 1
func run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new(null,{"reduced_motion":true})
	context.set_scenery_profile(load("res://data/themes/ivory_scenery.tres"))
	var shell = load("res://scenes/ui/application_shell.tscn").instantiate()
	shell.mount(context)
	var host = shell.host
	var scenery = shell.run_scenery
	var background = scenery.background
	var toast = shell.toast
	shell.mount(context)
	check(shell.host == host and shell.run_scenery == scenery and scenery.background == background and shell.toast == toast, "Shell mounting binds the same authored hosts")
	check(shell.get_child_count() == 3, "Repeated shell mounting does not duplicate chrome")
	root.add_child(shell)
	shell.show_pause({"type":"ABANDON_RUN"},[{"speaker":"Speaker","text":"Remembered line"}])
	var canvas = shell.pause_canvas
	check(canvas.scene_file_path == "res://scenes/ui/overlays/pause_menu.tscn", "Pause uses the canonical editable scene")
	check(canvas.layer == 10 and canvas.process_mode == Node.PROCESS_MODE_ALWAYS, "Pause retains modality and paused-tree processing")
	var resume = shell.pause_layer.find_child("ResumeButton",true,false)
	var counts: Array = []
	shell.resume_requested.connect(func(): counts.append(1))
	resume.pressed.emit()
	check(counts.size() == 1, "Authored resume button emits one application intent")
	check(not canvas.get_node("%ConversationHistoryButton").disabled and canvas.get_node("%AbandonRunButton").visible, "Pause data configures history and abandon visibility")
	var frame = canvas.get_node("%Frame")
	frame.get_theme_stylebox("panel").content_margin_left = 21
	shell.refresh_pause_visuals()
	check(frame.get_theme_stylebox("panel").content_margin_left == 21, "Refreshing pause colors preserves authored padding")
	check(resume.pressed.get_connections().size() == 1, "Visual refresh does not duplicate button handlers")
	shell.close_pause()
	check(shell.pause_canvas == null and shell.pause_layer == null, "Closing pause clears modal references")
	check(root.get_node("GameServices").diagnostics().is_empty(), "Shell fixtures send no HTTP")
	shell.free()
	quit(1 if failures else 0)
