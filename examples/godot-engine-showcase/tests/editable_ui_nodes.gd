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
    badge.free()
    quit(1 if failures else 0)
