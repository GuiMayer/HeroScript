extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, message: String) -> void:
    print("[PASS] " if ok else "[FAIL] ", message)
    if not ok: failures += 1
func _run() -> void:
    var badge = load("res://scenes/ui/components/status_badge.tscn").instantiate()
    var label = badge.get_node("%StackLabel")
    label.add_theme_font_size_override("font_size", 19)
    badge.get_theme_stylebox("panel").content_margin_left = 7
    badge.apply_status({"statusId": "burn", "stacks": 3})
    root.add_child(badge)
    await process_frame
    badge.apply_status({"statusId": "poison", "stacks": 8})
    check(badge.get_node("%StackLabel") == label, "Status updates retain authored nodes")
    check(label.text == "8" and label.get_theme_font_size("font_size") == 19, "Status data updates preserve inspector typography")
    check(badge.get_theme_stylebox("panel").content_margin_left == 7, "Status updates preserve scene padding")
    var resource = load("res://scenes/ui/components/resource_view.tscn").instantiate()
    var bar = resource.get_node("%ProgressBar")
    bar.custom_minimum_size.y = 39
    resource.setup("test", {"current": 5, "minimum": 0, "maximum": 10})
    root.add_child(resource)
    resource.setup("test", {"current": 9, "minimum": 0, "maximum": 10})
    check(resource.bar == bar and resource.bar.value == 9, "Resource scene updates existing nodes before and after mounting")
    check(resource.bar.custom_minimum_size.y == 39, "Resource data preserves authored bar height")
    var source = {"current": 9, "minimum": 0, "maximum": 10}
    resource.setup("test", source)
    resource.present_change({"resourceId": "test", "resourceField": "Current", "previousValue": 9, "currentValue": 2}, 0)
    check(resource.bar.value == 2 and source.current == 9, "Resource animation consumes snapshots without mutating input")
    resource.free()
    var actor = load("res://scenes/ui/components/actor_panel.tscn").instantiate()
    var target = actor.get_node("%ActorTarget")
    var title = actor.get_node("%ActorTitle")
    title.add_theme_font_size_override("font_size", 23)
    actor.setup({"instanceId": "first", "resources": {"health": {"current": 4, "minimum": 0, "maximum": 9}}}, false, {}, "")
    root.add_child(actor)
    var health = actor.find_child("Resource_health", true, false)
    actor.setup({"instanceId": "second", "resources": {"health": {"current": 7, "minimum": 0, "maximum": 9}}}, false, {}, "")
    check(actor.target_button == target and actor.find_child("Resource_health", true, false) == health, "Actor data retains target and resource identities")
    check(title.get_theme_font_size("font_size") == 23, "Actor data preserves authored typography")
    var selected: Array = []
    actor.target_selected.connect(func(id): selected.append(id))
    target.pressed.emit()
    check(selected == ["second"], "Persistent target uses current actor identity")
    actor.setup({"instanceId": "second"}, true, {}, "Attack", true)
    actor.setup({"instanceId": "second"}, true, {}, "Attack", false, true)
    check(actor.target_button == target, "Compact and battlefield variants reuse authored controls")
    actor.free()
    badge.free()
    quit(1 if failures else 0)
