extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func run() -> void:
	for path in ["combat_view", "combat_mvp_view"]:
		var view = load("res://scenes/ui/components/" + path + ".tscn").instantiate()
		var dock: Node = view.get_node("CombatBody/CombatGameplay/CombatBottomHUD/TurnControls")
		check(dock.has_node("HandTools") and dock.has_node("PileControls") and dock.has_node("RightPileControls"), path + ": fixed controls are authored before setup")
		check(view.has_node("FloatingCombatHUD/FloatingHUDHost/CombatAttributesDrawer"), path + ": reading overlay is authored in its final layer")
		var hand = view.get_node("CombatBody/CombatGameplay/CombatBottomHUD/HandSection/CardHandView")
		check(hand.get_node(hand.toolbar_path) == dock.get_node("HandTools"), path + ": hand tools use an explicit editable path")
		view.free()
	var standalone_hand = load("res://scenes/ui/components/card_hand.tscn").instantiate()
	var shared_style = standalone_hand.visual_style
	check(shared_style.get_script().is_tool(), "hand visual resource executes in the editor")
	check(standalone_hand._visual_style_key() == shared_style.presentation_key(), "property-based key preserves the hand layout cache contract")
	standalone_hand.visual_style = shared_style.duplicate()
	var original_key = standalone_hand._visual_style_key()
	standalone_hand.visual_style.lift += 1.0
	check(standalone_hand._visual_style_key() != original_key and shared_style.lift != standalone_hand.visual_style.lift, "Inspector edits invalidate layout without mutating the shared style")
	standalone_hand.free()
	var actor = load("res://scenes/ui/components/battlefield_actor.tscn").instantiate()
	check(actor.authored_battlefield_layout and actor.get_node("%ActorTarget").get_index() < actor.get_node("%ActorResources").get_index(), "actor node order is defined by the scene")
	actor.free()
	var badge = load("res://scenes/ui/components/status_badge.tscn").instantiate()
	var icon = load("res://assets/ivory/flame.svg")
	badge.icon_overrides["custom_status"] = icon
	badge.apply_status({"statusId": "custom_status", "stacks": 2})
	check(badge.icon_rect.visible and badge.icon_rect.texture == icon, "new status icons can be assigned through the scene Inspector")
	badge.free()
	quit(1 if failures else 0)
