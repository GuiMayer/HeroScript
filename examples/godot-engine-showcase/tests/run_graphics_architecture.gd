extends SceneTree
var failures := 0
class Fixture extends RefCounted:
	var reads := 0
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		reads += 1
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func _run() -> void:
	for file in ["combat_view", "journey_view", "route_view", "actor_panel", "card_hand_view", "card_view", "dialogue_panel"]:
		var source := FileAccess.get_file_as_string("res://scripts/ui/%s.gd" % file)
		check(not ["GameSession", "Preferences.", "I18n.", "AppTheme.", "http_transport"].any(func(token): return source.contains(token)), "passive boundary: " + file)
	for file in ["combat_controller", "activity_controller", "combat_inspection_coordinator"]:
		var source := FileAccess.get_file_as_string("res://scripts/ui/controllers/%s.gd" % file)
		check(not ["Button.new", "Control.new", "AppTheme", "StyleBox", "get_tree"].any(func(token): return source.contains(token)), "coordination has no node/style construction: " + file)
	var session = root.get_node("GameSession")
	var playback = root.get_node("Playback")
	root.get_node("Preferences").auto_animations = false
	var fixture := Fixture.new(); session.configure(fixture); session.synchronized = true
	session._run = {"runId": "architecture", "sequence": 1, "playerEntityId": "hero"}
	var actors: Array = []
	for pair in [["hero", "player"], ["enemy", "enemy"], ["neutral", "neutral"], ["third", "other-enemy"]]:
		actors.append({"instanceId": pair[0], "definitionId": "spire_wisp", "sideId": pair[1], "resources": {}, "statuses": []})
	session._combat = {"combatId": "combat", "activeActorId": "hero", "actors": actors,
		"relationships": {"sameSide": "Ally", "differentSides": "Enemy", "rules": [{"fromSideId": "player", "toSideId": "neutral", "relationship": "Neutral"}]}}
	session._card_zones = {"zones": [{"zoneId": "play", "allowsCardPlay": true, "contentsVisible": true, "cards": [{"cardInstanceId": "one", "definitionId": "basic_attack"}], "count": 1}]}
	session._legal_actions = [{"source": "Card", "command": {"cardInstanceId": "one", "targetIds": ["neutral"], "actionType": "PLAY_CARD"}}]
	var router = load("res://main.tscn").instantiate(); root.add_child(router); router.show_combat(); await settle()
	var screen = router.host.get_child(0)
	var view = screen.view
	var hand = view.hand
	var art = view.actor_portraits.neutral
	var target = view.target_buttons.neutral
	check(router.shell.run_scenery.is_ancestor_of(art) and not view.is_ancestor_of(art), "actor art belongs to shell scenery, not HUD/target tree")
	check(view.is_ancestor_of(target) and not target.is_ancestor_of(art), "target surface is independently owned by HUD")
	var groups: Array = view.battlefield_view.panels.values().map(func(panel): return panel.get_parent())
	check(groups.size() == 4 and groups[0] != groups[2] and groups[1] != groups[3], "neutral and separate enemy teams retain separate layout identities")
	screen._choose_card("one")
	check(not target.disabled and view.target_buttons.enemy.disabled, "published neutral target remains selectable; relation never creates target permissions")
	var snapshot := JSON.stringify([session.run, session.combat, session.legal_actions])
	var reads: int = fixture.reads
	var reconciler = load("res://scripts/ui/view_reconciler.gd")
	var replacements: int = reconciler.replaced
	var relayouts: int = hand.relayout_count
	var started := Time.get_ticks_usec()
	for _repeat in 20: screen.refresh_state(router.presentation)
	var refresh_usec := Time.get_ticks_usec() - started
	await settle()
	check(fixture.reads == reads and reconciler.replaced == replacements and hand.relayout_count == relayouts, "twenty identical refreshes create no replacements, hand relayouts or inspection reads")
	print("[METRIC] ", JSON.stringify({"identical_refreshes": 20, "local_refresh_mean_usec": refresh_usec / 20., "node_replacements": reconciler.replaced - replacements, "hand_relayouts": hand.relayout_count - relayouts, "inspection_reads": fixture.reads - reads}))
	var scene_profile: Resource = router.visual_context.scenery_profile.duplicate(true)
	scene_profile.colors = {"scenery.base": "#123456"}
	router.visual_context.set_scenery_profile(scene_profile)
	screen.refresh_state(router.presentation); await settle()
	check(view.hand == hand and view.actor_portraits.neutral == art and screen.selected_card == "one" and fixture.reads == reads, "scenery-only change preserves hand/art identity, selection and inspection count")
	router.apply_visual_profile(load("res://data/themes/contrast_test.tres")); await settle()
	check(router.visual_context.scenery_profile == scene_profile and screen.selected_card == "one" and snapshot == JSON.stringify([session.run, session.combat, session.legal_actions]), "UI-only change cannot alter scenery profile or gameplay snapshots")
	var invalid := PackedScene.new(); var bad := Control.new(); invalid.pack(bad); bad.free()
	var old_body = view.gameplay_column
	view.replace_composition(invalid, view.layout_profile); await settle()
	check(view.gameplay_column == old_body and view.hand == hand, "invalid composition is rejected before any reparent")
	router.back_to_menu(); await settle()
	check(router.shell.run_scenery.get_child_count() == 1 and not is_instance_valid(art), "leaving combat releases all external artwork and preserves only ambient scenery")
	router.queue_free(); await settle(); playback.clear()
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
