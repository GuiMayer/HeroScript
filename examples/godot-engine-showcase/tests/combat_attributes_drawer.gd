extends SceneTree
var failures := 0
class Gateway extends RefCounted:
	var inspections := 0
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		inspections += 1
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 12: await process_frame
func run() -> void:
	var session = root.get_node("GameSession")
	var prefs = root.get_node("Preferences")
	var gateway := Gateway.new()
	session.configure(gateway)
	session._run = {"runId": "attributes-drawer", "sequence": 1, "playerEntityId": "hero"}
	session._combat = {"combatId": "combat", "activeActorId": "enemy", "actors": [
		{"instanceId": "hero", "definitionId": "Hero", "controllerBinding": {"kind": "Player"}, "resources": {"health": {"current": 30, "maximum": 50}, "energy": {"current": 4}}, "statuses": []},
		{"instanceId": "enemy", "definitionId": "Enemy", "resources": {"health": {"current": 999}}, "statuses": []}]}
	session._character_attributes = {"runId": "attributes-drawer", "runSequence": 1, "actorId": "hero", "attributes": [{"attributeId": "armor", "displayName": "Armor", "baseValue": 0, "effectiveValue": 25, "unit": "points"}]}
	session._card_zones = {"zones": []}
	prefs.reduced_motion = true
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	router.show_combat(); await settle()
	var view = router.host.get_child(0).view
	var drawer = view.character_sidebar
	var original := JSON.stringify([session.run, session.combat, session.character_attributes])
	var before: Rect2 = view.bottom_hud.get_global_rect()
	var inspections: int = gateway.inspections
	check(drawer.attributes_view.get_script() == load("res://scripts/ui/loadout/character_attributes_view.gd"), "combat uses the exact loadout attribute component")
	check(not drawer.visible and view.find_child("CharacterSidebarReopen", true, false) == null, "starts closed without the former top-right button")
	var key := InputEventKey.new(); key.physical_keycode = KEY_I; key.pressed = true
	root.push_input(key, true); await settle()
	check(drawer.expanded and drawer.visible and is_equal_approx(drawer.global_position.x, 0), "I opens the floating drawer at the viewport left")
	check(drawer.attributes_view._rows["armor"].equipped.text == "25", "canonical player attribute is shown during an enemy activation")
	check(drawer.attributes_view._rows["resource:health"].equipped.text == "30 / 50", "live player resources are shown instead of enemy values")
	check(drawer.get_parent() == view.floating_hud.host and view.bottom_hud.get_global_rect() == before, "opening reserves no layout space")
	var pointer := InputEventMouseMotion.new(); pointer.position = Vector2(1000, 300); view._input(pointer)
	check(drawer.expanded, "keyboard opening stays open when mouse leaves")
	var cancel := InputEventAction.new(); cancel.action = "ui_cancel"; cancel.pressed = true
	view._input(cancel)
	check(not drawer.expanded and not paused, "Escape closes attributes before opening pause")
	view._input(key)
	view._input(key); check(not drawer.visible, "I closes with reduced motion immediately")
	pointer.position = Vector2(100, 200); view._input(pointer)
	pointer.position = Vector2(2, 200); view._input(pointer)
	check(drawer.expanded and drawer.visible, "mouse proximity opens the left drawer")
	pointer.position = Vector2(900, 200); view._input(pointer)
	check(not drawer.visible, "hover opening closes after leaving the drawer")
	view.hand.reading_modal = true; view._input(key)
	check(not drawer.expanded, "card reading modal blocks the shortcut")
	view.hand.reading_modal = false
	paused = true; view._input(key); paused = false
	check(not drawer.expanded, "pause blocks the shortcut")
	prefs.reduced_motion = false; drawer.visual_context.update_from(prefs, root.get_node("I18n"), AppTheme.profile)
	view._input(key)
	check(drawer.visible and drawer.openness < 1, "normal opening starts a smooth slide")
	await create_timer(.09).timeout
	check(drawer.openness > 0 and drawer.openness < 1, "intermediate animation pose exists")
	view._input(key); await create_timer(.25).timeout
	check(not drawer.visible and is_zero_approx(drawer.openness), "interrupted opening reverses and settles closed")
	prefs.reduced_motion = true; drawer.visual_context.update_from(prefs, root.get_node("I18n"), AppTheme.profile)
	view._input(key); await settle()
	check(JSON.stringify([session.run, session.combat, session.character_attributes]) == original, "drawer interactions preserve immutable engine facts")
	session._combat.actors[0].resources.health.current = 21
	session._character_attributes.attributes[0].effectiveValue = 42
	router.host.get_child(0).refresh_state(router.presentation); await settle()
	check(view.character_sidebar == drawer and drawer.expanded, "refresh keeps the drawer and its open state")
	check(drawer.attributes_view._rows.armor.equipped.text == "42" and drawer.attributes_view._rows["resource:health"].equipped.text == "21 / 50", "refresh immediately presents current engine facts")
	check(view.bottom_hud.get_global_rect() == before, "open close and refresh never move the hand or controls")
	check(gateway.inspections == inspections, "drawer makes no additional engine requests")
	check(JSON.stringify([session.run, session.combat, session.character_attributes]) != original and session.run.sequence == 1, "only fixture updates change facts; drawer never advances run sequence")
	check(prefs.DEFAULT_KEYS.open_character == KEY_I and prefs.DEFAULT_KEYS.inspect_card == KEY_V, "keyboard defaults separate attributes and card inspection")
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="):
			await RenderingServer.frame_post_draw
			root.get_texture().get_image().save_png(arg.trim_prefix("--lab-output=").path_join("combat-attributes.png"))
	router.queue_free(); await settle()
	quit(1 if failures else 0)
