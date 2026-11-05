@tool
extends Node
# Runs with --editor: runtime-only tests cannot detect placeholder instances.
var checks := 0
var failures := 0
func _ready() -> void:
	if Engine.is_editor_hint() and "--editor-ui-regression" in OS.get_cmdline_user_args(): run.call_deferred()
func run() -> void:
	for path in ["res://data/default_hand_visual_style.tres", "res://data/themes/ivory_run.tres", "res://data/themes/ivory_run_card_style.tres", "res://data/themes/ivory_menu_style.tres"]:
		var resource: Resource = load(path)
		if not resource.get_script().is_tool():
			push_error("Editor presentation resource is not tool-enabled: " + path)
			failures += 1
	var paths: Array[String] = []
	_collect("res://scenes/ui", paths)
	paths.sort()
	for path in paths:
		if path.ends_with("/combat_hand.tscn"): continue # Requires its authored combat toolbar.
		var scene: PackedScene = load(path)
		var state := scene.get_state()
		var script: Script
		for index in state.get_node_property_count(0):
			if state.get_node_property_name(0, index) == &"script": script = state.get_node_property_value(0, index)
		if script == null or not script.is_tool(): continue
		print("[EDITOR SCENE] ", path)
		var view = scene.instantiate()
		add_child(view)
		for frame in 3: await get_tree().process_frame
		_check_visual_scripts(view, path)
		if path.ends_with("/run_scenery_host.tscn"):
			var scenery = view.acquire_combat()
			scenery.apply_model([], "", preload("res://scripts/ui/visual_context.gd").new())
			view.release_combat(scenery)
		remove_child(view)
		view.queue_free()
		for frame in 3: await get_tree().process_frame
		checks += 1
		print("[PASS] editor mounts ", path)
	print("[EDITOR TOTAL] ", checks)
	get_tree().quit(1 if failures else 0)
func _collect(path: String, paths: Array[String]) -> void:
	for directory in DirAccess.get_directories_at(path): _collect(path.path_join(directory), paths)
	for file in DirAccess.get_files_at(path):
		if file.ends_with(".tscn"): paths.append(path.path_join(file))

func _check_visual_scripts(node: Node, path: String) -> void:
	var script: Script = node.get_script()
	# This lifecycle helper restores runtime popup focus; previews never call it.
	var runtime_only := script != null and script.resource_path == "res://scripts/ui/components/modal_focus.gd"
	if script != null and not script.is_tool() and not runtime_only:
		push_error("Editor visual has a placeholder script: %s (%s)" % [node.name, path])
		failures += 1
	for child in node.get_children(): _check_visual_scripts(child, path)
