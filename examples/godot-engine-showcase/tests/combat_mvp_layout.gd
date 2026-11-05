extends SceneTree
## Production combat UI with offline projection fixtures; no REST or saved preferences.
var failures := 0
class InspectionFixture extends RefCounted:
	func connect_engine() -> Dictionary:
		return {"ok": false, "status": 503, "error": "Offline layout fixture"}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 12: await process_frame
func _actor(id: String, definition: String, side: String) -> Dictionary:
	return {"instanceId": id, "definitionId": definition, "sideId": side, "resources": {
		"health": {"current": 48, "maximum": 72}, "block": {"current": 8, "maximum": 999},
		"energy": {"current": 3, "maximum": 3}, "mana": {"current": 5, "maximum": 8}}, "statuses": []}
func _run() -> void:
	var prefs = root.get_node("Preferences")
	var i18n = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var prior := {"scale": prefs.text_scale, "motion": prefs.reduced_motion, "locale": i18n.locale,
		"run": session._run, "combat": session._combat, "zones": session._card_zones,
		"actions": session._legal_actions, "gateway": session._gateway, "attributes": session._character_attributes}
	prefs.reduced_motion = true
	session.configure(InspectionFixture.new())
	session.synchronized = true
	session._run = {"settingId": "prototype-test", "runId": "layout-fixture", "sequence": 1, "playerEntityId": "hero"}
	session._character_attributes = {"settingId": "prototype-test", "runId": "layout-fixture", "runSequence": 1, "actorId": "hero", "attributes": [
		{"attributeId": "armor", "displayName": "Armor", "unit": "points", "baseValue": 0, "effectiveValue": 25},
		{"attributeId": "armor_reduction", "displayName": "Armor reduction", "unit": "percent", "baseValue": 0, "effectiveValue": 20},
		{"attributeId": "critical_chance", "displayName": "Critical chance", "unit": "percent", "baseValue": 15, "effectiveValue": 150},
		{"attributeId": "critical_damage", "displayName": "Critical damage bonus per tier", "unit": "percent", "baseValue": 100, "effectiveValue": 100}],
		"criticalRules": [{"ruleId": "direct-damage", "guaranteedTiers": 1, "nextTierChancePercent": 50}]}
	session._legal_actions = []
	session._combat = {"combatId": "layout-combat", "activeActorId": "hero", "activation": {
		"round": 1, "activeActorId": "hero", "waitingForInput": true,
		"intents": [{"actorId": "wisp", "actionId": "Attack"}, {"actorId": "sentinel", "actionId": "Poison Spit"}]},
		"relationships": {"sameSide": "Ally", "differentSides": "Enemy"}, "actors": [
			_actor("hero", "spire_adept", "player"), _actor("wisp", "spire_wisp", "enemy"),
			_actor("sentinel", "spire_sentinel", "enemy")]}
	var cards: Array = []
	for i in 12:
		cards.append({"cardInstanceId": "fixture-%s" % i, "definitionId": ["basic_attack", "defend", "fireball", "heal", "arcane_bolt"][i % 5]})
		session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": "fixture-%s" % i,
			"actionType": "PLAY_CARD", "targetIds": ["wisp"]}, "costs": [{"resourceId": "energy", "amount": 1}]})
		session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": "fixture-%s" % i,
			"actionType": "PLAY_CARD", "targetIds": ["wisp"], "costOptionId": "alternate"}, "costs": [{"resourceId": "mana", "amount": 2}]})
	session._card_zones = {"zones": [{"zoneId": "custom-play", "allowsCardPlay": true, "contentsVisible": true,
		"cards": cards, "count": 5}, {"zoneId": "draw", "count": 8, "presentation": {"labelKey": "Draw pile"}},
		{"zoneId": "discard", "count": 2, "presentation": {"labelKey": "Discard pile"}}]}
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var hand = screen.view.hand

	check(screen.scene_file_path.ends_with("combat_mvp.tscn"), "MVP run selects dedicated screen")
	check(screen.view.get_script().resource_path.ends_with("combat_mvp_view.gd"), "MVP uses layout variant with shared combat controller")
	check(not screen.view.header_view.get_node("TimelineButton").visible, "MVP header hides timeline")
	check(not screen.view.header_view.get_node("HeroName").text.is_empty(), "header identifies current hero")
	var registry = load("res://scripts/ui/scene_registry.gd")
	check(registry.combat_route({"settingId": "ascendant"}, "prototype-test") == "combat", "actual run setting overrides menu selection")
	check(registry.combat_route({"settingId": "prototype-test"}, "default") == "combat_mvp", "continuing MVP selects correct scene")
	for resolution in [Vector2i(1920,1080), Vector2i(1280,800), Vector2i(2560,1080)]:
		root.size = resolution
		await settle()
		check(screen.view.bottom_hud.get_global_rect().end.x <= screen.get_global_rect().end.x + 1, "right controls remain within viewport " + str(resolution))
		check(hand.order.size() == 12, "shared hand supports twelve cards")
		var before: Rect2 = hand.get_global_rect()
		screen._toggle_character_sidebar()
		await settle()
		check(hand.get_global_rect() == before, "attribute overlay does not move hand")
		if DisplayServer.get_name() != "headless":
			await RenderingServer.frame_post_draw
			for argument in OS.get_cmdline_user_args():
				if argument.begins_with("--lab-output="):
					root.get_texture().get_image().save_png(argument.trim_prefix("--lab-output=").path_join("mvp-%sx%s.png" % [resolution.x,resolution.y]))
	router.queue_free()
	await process_frame
	quit(1 if failures else 0)
