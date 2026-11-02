extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
    print(("[PASS] " if ok else "[FAIL] ") + message)
    if not ok: failures += 1
func run() -> void:
    var current = load("res://data/themes/ivory_run.tres")
    var factory = load("res://scripts/ui/styles/view_style_factory.gd").new()
    check(factory.profile == current and AppTheme.profile == current, "Global helpers and standalone factories default to the current profile")
    check(factory.NIGHT == current.color("surface.canvas"), "Standalone laboratory canvas follows the active palette")
    var actor = load("res://scenes/ui/components/actor_panel.tscn").instantiate()
    var resource = load("res://scenes/ui/components/resource_view.tscn").instantiate()
    var badge = load("res://scenes/ui/components/status_badge.tscn").instantiate()
    for view in [actor, resource, badge]: root.add_child(view)
    for frame in 3: await process_frame
    check(actor.presentation_model().actor == actor.preview_actor, "Actor F6 preview receives the authored sample")
    check(resource.presentation_model().state == resource.preview_state and resource.bar.value == resource.preview_state.current, "Resource F6 preview applies its actual bounds and current value")
    check(badge.presentation_model() == badge.preview_status, "Status F6 preview receives stacks and duration")
    for view in [actor, resource, badge]:
        check(view.visual_context.profile == current and view.theme == view.visual_context.theme, "Preview uses the same current context and theme: " + view.name)
    var holder := Control.new()
    root.add_child(holder)
    for scene in ["actor_panel", "resource_view", "status_badge"]:
        var child = load("res://scenes/ui/components/" + scene + ".tscn").instantiate()
        holder.add_child(child)
        check(child.presentation_model().is_empty(), "Embedded unbound component does not inject sample gameplay data: " + scene)
    var bound = load("res://scenes/ui/components/resource_view.tscn").instantiate()
    bound.setup("custom", {"current": 7, "minimum": 0, "maximum": 9})
    root.add_child(bound)
    check(bound.presentation_model().resourceId == "custom" and bound.bar.value == 7, "Root preview never overwrites supplied engine data")
    var event_lab = load("res://scenes/gameplay_event_lab.tscn").instantiate()
    root.add_child(event_lab)
    check(event_lab.theme != null and event_lab.get_child(0).color == current.color("surface.canvas"), "Event laboratory uses the current themed canvas and native controls")
    event_lab.free()
    var deck_lab = load("res://scenes/deck_compartment_lab.tscn").instantiate()
    # Load the fixture after autoload registration; never start the live API flow.
    var offline_script := GDScript.new()
    offline_script.source_code = """
extends "res://scripts/labs/deck_compartment_lab.gd"
func _start() -> void: pass
"""
    check(offline_script.reload() == OK, "Offline laboratory fixture compiles after autoload registration")
    deck_lab.set_script(offline_script)
    root.add_child(deck_lab)
    await process_frame
    check(deck_lab.theme != null and deck_lab.get_node("Background").color == current.color("surface.canvas"), "Deck laboratory applies the current theme before connecting")
    deck_lab.free()
    for view in [actor, resource, badge, holder, bound]: view.free()
    quit(1 if failures else 0)
