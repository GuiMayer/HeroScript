extends SceneTree
var failures := 0
class GatewayFixture extends RefCounted:
	var inspections := 0
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		inspections += 1
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func _run() -> void:
	var session = root.get_node("GameSession")
	var playback = root.get_node("Playback")
	var prefs = root.get_node("Preferences")
	prefs.reduced_motion = true
	prefs.auto_animations = false
	var gateway := GatewayFixture.new()
	session.configure(gateway)
	# A fixture is synchronized explicitly, independent of saved endpoint/startup invalidation.
	session.synchronized = true
	session._run = {"runId": "replace", "sequence": 1}
	session._combat = {"combatId": "c", "activeActorId": "hero", "actors": [{"instanceId": "hero", "resources": {}, "statuses": []}]}
	session._card_zones = {"zones": [{"zoneId": "play", "allowsCardPlay": true, "contentsVisible": true, "count": 1, "cards": [{"cardInstanceId": "card", "definitionId": "defend"}]}]}
	session._legal_actions = [{"source": "Card", "command": {"cardInstanceId": "card", "targetIds": ["hero"], "actionType": "PLAY_CARD"}}]
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var controller = screen.controller
	var hand = screen.view.hand
	screen._choose_card("card")
	check(screen.selected_card == "card" and hand.order.size() == 1, "proof starts with a visible explicitly selected card")
	var options: Array = screen._candidates("card")
	var snapshot := JSON.stringify([session.run, session.combat, session.legal_actions])
	var reads: int = gateway.inspections
	screen._toggle_character_sidebar()
	playback.load_receipt({"state": {"resolution": {"frames": [{"transitionType": "fixture", "applications": []}]}}})
	var cursor: int = playback.index
	router.apply_visual_profile(load("res://data/themes/contrast_test.tres"))
	screen.replace_composition(load("res://scenes/ui/combat_lateral_test.tscn"), load("res://data/themes/lateral_preview_test.tres"))
	await settle()
	check(screen.controller == controller and screen.view.hand == hand, "different theme/composition retains identical controller and hand")
	check(screen.selected_card == "card" and screen.view.character_sidebar_expanded, "replacement preserves valid selection and sidebar")
	check(screen.view._feedback_column.get_parent() == screen.view.floating_hud.host, "alternative composition places contextual preview outside bottom controls")
	check(screen._candidates("card") == options and snapshot == JSON.stringify([session.run, session.combat, session.legal_actions]), "same semantic input maps to identical published payloads; no gameplay state change")
	check(gateway.inspections == reads and playback.index == cursor, "replacement neither requests engine inspection nor advances playback")
	session._legal_actions.append({"source": "System", "command": {"actionType": "PASS"}})
	screen.refresh_state(router.presentation)
	await settle()
	check(screen.view._feedback_column.get_parent() == screen.view.floating_hud.host and screen.view.floating_hud.host.find_children("ActionPreview*", "", true, false).size() <= 1, "footer refresh preserves the active preview composition without duplicates")
	var i18n = root.get_node("I18n")
	i18n.set_locale("pt_BR")
	root.size = Vector2i(1280, 800)
	await settle()
	check(screen.view.hand == hand and screen.selected_card == "card", "language and resize preserve local state in alternative composition")
	i18n.set_locale("en")
	reads = gateway.inspections
	router.toggle_pause()
	screen.replace_composition(load("res://scenes/ui/combat_composition.tscn"), load("res://data/themes/bottom_preview.tres"))
	await settle()
	check(screen.view.hand == hand and router.pause_layer.is_ancestor_of(root.gui_get_focus_owner()), "pause remains modal during replacement")
	router.toggle_pause()
	await settle()
	check(screen.view._feedback_column.get_parent() == screen.view.turn_view, "return composition has only one preview authority")
	router.apply_visual_profile(load("res://data/themes/preserved.tres"))
	await settle()
	check(screen.selected_card == "card" and screen.view.hand == hand and gateway.inspections == reads, "return profile has no reset or extra request")
	var before: Control = screen.view.gameplay_column
	screen.controller.state.submitting = true
	screen.apply_interaction(null)
	screen.replace_composition(load("res://scenes/ui/combat_lateral_test.tscn"), load("res://data/themes/lateral_preview_test.tres"))
	await settle()
	check(screen.view.gameplay_column == before, "critical submission defers geometric replacement")
	screen.controller.state.submitting = false
	screen.apply_interaction(null)
	await settle()
	check(screen.view.gameplay_column != before, "deferred composition applies at a safe presentation point")
	router.queue_free()
	await settle()
	playback.clear()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
