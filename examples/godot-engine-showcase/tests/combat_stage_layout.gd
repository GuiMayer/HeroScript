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
	session._run = {"runId": "layout-fixture", "sequence": 1, "playerEntityId": "hero"}
	session._character_attributes = {"runId": "layout-fixture", "runSequence": 1, "actorId": "hero", "attributes": [
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
	for i in 5:
		cards.append({"cardInstanceId": "fixture-%s" % i, "definitionId": ["basic_attack", "defend", "fireball", "heal", "arcane_bolt"][i]})
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
	check(not screen.view.character_sidebar.visible and screen.view.character_sidebar_reopen.visible, "character details start collapsed, leaving enemy intents unobstructed")
	screen._toggle_character_sidebar()
	await settle()
	for resolution in [Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution
		for locale in ["en", "pt_BR"]:
			i18n.set_locale(locale)
			for scale in [1.0, 1.2]:
				prefs.text_scale = scale
				screen.refresh_state(router.presentation)
				await settle()
				var stage: Control = screen.find_child("Battlefield", true, false)
				var footer: Control = screen.find_child("EndTurnButton", true, false)
				check(stage.get_global_rect() == screen.view.gameplay_column.get_global_rect(), "scenery fills gameplay behind the hand: %s / %s / %s" % [resolution, locale, scale])
				check(stage.get_index() < screen.view.bottom_hud.get_index() and screen.view.hand == hand, "floating hand stays above scenery without reparenting")
				check(screen.view.gameplay_column.get_global_rect() == screen.find_child("CombatBody", true, false).get_global_rect(), "expanded character panel reserves zero battlefield width")
				check(screen.view.floating_hud.host.get_global_rect() == screen.view.gameplay_column.get_global_rect(), "floating panels follow gameplay bounds after resize")
				check(screen.view.character_sidebar.get_global_rect().end.y < screen.view.bottom_hud.get_global_rect().end.y - 60, "reading panel never covers bottom turn controls")
				check(hand.toolbar.get_parent() == screen.view.turn_view and not hand.overview_enabled, "tools are in the independent dock without hand overview")
				check(footer.get_global_rect().end.y <= router.get_global_rect().end.y - 23 and screen.view.bottom_hud.get_global_rect().end.x <= screen.view.gameplay_column.get_global_rect().end.x + 1, "bottom controls stay in bounds")
				check(preload("res://tests/layout_inspector.gd").vertical_text_issues(screen).is_empty(), "translated combat text never collapses vertically")
				check(screen.view.actor_portraits.values().all(func(portrait): return portrait.size.x >= 180 and portrait.size.y >= 60), "actor artwork is expanded and adapts to accessible fonts")
				var first_card_top: float = hand.scroll.get_global_rect().position.y + hand.visual_style.padding
				check(screen.view.target_buttons.values().all(func(button): return button.get_parent().get_global_rect().end.y <= first_card_top), "actor information remains above even raised cards")
				check(not screen.view.battlefield_actors.get_v_scroll_bar().visible, "authored actors fit without vertical scrolling")
				if screen.view.battlefield_actors.get_v_scroll_bar().visible:
					print("[LAYOUT] ", JSON.stringify({"stage": screen.view.battlefield_actors.size, "hand": hand.scroll.size, "dock": screen.view.bottom_hud.size, "actors": screen.view.target_buttons.values().map(func(button): return [button.size, button.get_parent().size])}))
				var before: Rect2 = screen.view.bottom_hud.get_global_rect()
				screen._choose_card("fixture-0")
				await settle()
				check(screen.view.bottom_hud.get_global_rect() == before, "selection does not move the floating hand")
				screen.view._show_target_preview("wisp")
				check(screen.view.detail_label.name == "ActionPreview" and screen.view.detail_label.text.begins_with(i18n.text("Action preview: %s").split("%s")[0]), "target preview has its own localized area outside the cards")
				check(session._run.sequence == 1 and screen.selected_target.is_empty(), "hovering/focusing a target never submits or selects a gameplay action")
				screen._offer_candidates(screen._candidates("fixture-0"))
				await settle()
				check(screen.view.candidate_panel.visible and screen.view.choices.get_child_count() == 2 and screen.view.bottom_hud.get_global_rect() == before, "alternate costs remain explicit without reserving another footer row")
				check(screen.view.gameplay_column.get_global_rect().encloses(screen.view.candidate_panel.get_global_rect()), "floating payment options stay within gameplay bounds")
				check(screen.view.candidate_panel.get_global_rect().end.y <= hand.toolbar.global_position.y, "payment options do not cover the pile and reading tools")
				var variants: Array = []
				for _option in 8: variants.append(screen._candidates("fixture-0")[0])
				screen._offer_candidates(variants)
				await settle()
				check(screen.view.choices.get_parent().get_v_scroll_bar().visible and screen.view.bottom_hud.get_global_rect() == before, "many published payment options scroll without changing composition")
				screen._cancel_selection()
				if resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)] and locale == "en" and scale == 1.0 and DisplayServer.get_name() != "headless":
					await RenderingServer.frame_post_draw
					for argument in OS.get_cmdline_user_args():
						if argument.begins_with("--lab-output="):
							var destination: String = argument.trim_prefix("--lab-output=").path_join("character-attributes-%sx%s.png" % [resolution.x, resolution.y])
							check(root.get_texture().get_image().save_png(destination) == OK, "attribute panel screenshot saved")
				if resolution == Vector2i(1280, 800) and locale == "pt_BR" and scale == 1.2 and DisplayServer.get_name() != "headless":
					await RenderingServer.frame_post_draw
					for argument in OS.get_cmdline_user_args():
						if argument.begins_with("--compact-screenshot="):
							check(root.get_texture().get_image().save_png(argument.trim_prefix("--compact-screenshot=")) == OK, "compact translated screenshot saved")
				if resolution == Vector2i(2560, 1080) and locale == "en" and scale == 1.0 and DisplayServer.get_name() != "headless":
					await RenderingServer.frame_post_draw
					for argument in OS.get_cmdline_user_args():
						if argument.begins_with("--stage-screenshot="):
							check(root.get_texture().get_image().save_png(argument.trim_prefix("--stage-screenshot=")) == OK, "combat layout screenshot saved")
				screen.view.character_sidebar.find_child("CharacterSidebarToggle", true, false).grab_focus()
				screen._toggle_character_sidebar()
				await settle()
				check(screen.view.bottom_hud.get_global_rect() == before, "collapsing character details does not move or resize any hand slots")
				check(screen.view.hand == hand and screen.view.bottom_hud.get_global_rect().end.x <= screen.view.gameplay_column.get_global_rect().end.x + 1, "sidebar toggle preserves the hand and anchored layout")
				check(not screen.view.character_sidebar.visible and screen.view.character_sidebar_reopen.visible, "collapsed sidebar displays only its arrow")
				check(screen.view.character_sidebar_reopen.has_focus(), "closing sidebar transfers keyboard focus to the arrow")
				var arrow = screen.view.character_sidebar_reopen
				var refreshed: Dictionary = router.presentation.duplicate(true)
				refreshed["sidebar_refresh_fixture"] = str([resolution, locale, scale])
				screen.refresh_state(refreshed)
				await settle()
				check(screen.view.character_sidebar_reopen == arrow and not screen.view.character_sidebar.visible and arrow.has_focus(), "snapshot refresh preserves collapsed state, arrow and keyboard focus")
				check(screen.view.gameplay_column.get_global_rect() == screen.find_child("CombatBody", true, false).get_global_rect(), "collapsed sidebar reserves zero layout space")
				var arrow_rect: Rect2 = screen.view.character_sidebar_reopen.get_global_rect()
				check(arrow_rect.size.x <= 50 and arrow_rect.size.y <= 50 and screen.view.gameplay_column.get_global_rect().encloses(arrow_rect), "reopen arrow is a small top-right overlay")
				if resolution == Vector2i(2560, 1080) and locale == "en" and scale == 1.0 and DisplayServer.get_name() != "headless":
					await RenderingServer.frame_post_draw
					for argument in OS.get_cmdline_user_args():
						if argument.begins_with("--sidebar-screenshot="):
							check(root.get_texture().get_image().save_png(argument.trim_prefix("--sidebar-screenshot=")) == OK, "collapsed sidebar screenshot saved")
				for pressed in [true, false]:
					var event := InputEventMouseButton.new()
					event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
					event.position = arrow_rect.get_center(); event.global_position = event.position
					root.push_input(event, true)
				await settle()
				check(screen.view.character_sidebar.visible and not screen.view.character_sidebar_reopen.visible, "real pointer reopens the complete sidebar")
				check(screen.view.bottom_hud.get_global_rect() == before, "opening character details does not move the hand or turn controls")
				if screen.view.bottom_hud.get_global_rect() != before: print("[DOCK_DELTA] ", before, " -> ", screen.view.bottom_hud.get_global_rect(), " hand=", hand.scroll.size, " bar=", hand.scroll.get_h_scroll_bar().visible, " layout=", hand._layout.height, " min=", hand.scroll.get_combined_minimum_size())
	# A high-z gameplay button must not receive clicks through the reading overlay.
	var clicks := [0]
	var probe := Button.new()
	probe.z_index = 500
	screen.view.gameplay_column.add_child(probe)
	probe.size = Vector2(80, 40)
	probe.global_position = screen.view.character_sidebar.global_position + Vector2(24, 72)
	probe.pressed.connect(func(): clicks[0] += 1)
	for pressed in [true, false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
		event.position = probe.get_global_rect().get_center(); event.global_position = event.position
		root.push_input(event, true)
	check(clicks[0] == 0, "floating character panel blocks input through it even above high-z cards")
	probe.queue_free()
	await settle()
	screen.hide()
	check(not screen.view.floating_hud.visible, "hiding combat also hides its independent overlays")
	screen.show()
	check(screen.view.floating_hud.visible, "showing combat restores its overlay visibility")
	# Actual input still reaches the shared hand above the background and then
	# selects an expanded portrait. The fixture has alternate costs, so choosing
	# a target only displays the published options and never issues a command.
	var point: Vector2 = hand.scroll.global_position + hand._layout.hitRects[2].get_center() - Vector2(hand.scroll.scroll_horizontal, 0)
	var motion := InputEventMouseMotion.new()
	motion.position = point; motion.global_position = point
	root.push_input(motion, true)
	for pressed in [true, false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
		event.position = point; event.global_position = point
		root.push_input(event, true)
	check(screen.selected_card == "fixture-2", "real pointer selects a floating card")
	var target_point: Vector2 = screen.view.target_buttons.wisp.get_global_rect().get_center()
	for pressed in [true, false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT; event.pressed = pressed
		event.position = target_point; event.global_position = target_point
		root.push_input(event, true)
	check(screen.selected_target == "wisp", "real pointer selects an expanded enemy portrait")
	screen._cancel_selection()
	# An empty hand keeps its reserved dimensions rather than moving all actors.
	var prior_hud: Rect2 = screen.view.bottom_hud.get_global_rect()
	session._card_zones.zones[0].cards = []
	session._card_zones.zones[0].count = 0
	screen.refresh_state(router.presentation)
	await settle()
	check(screen.view.bottom_hud.get_global_rect() == prior_hud and hand.order.is_empty(), "empty hand preserves the combat composition")
	router.toggle_pause()
	await settle()
	check(router.pause_canvas.layer > screen.view.floating_hud.layer and router.pause_layer.can_process() and not hand.can_process(), "pause remains above both floating panels and the hand")
	check(not screen.view.character_sidebar.can_process() and not screen.view.character_sidebar_reopen.can_process(), "floating details cannot receive input while paused")
	router.toggle_pause()
	router.queue_free()
	await settle()
	session._run = prior.run; session._combat = prior.combat; session._card_zones = prior.zones
	session._character_attributes = prior.attributes
	session._legal_actions = prior.actions; session.configure(prior.gateway)
	prefs.text_scale = prior.scale; prefs.reduced_motion = prior.motion
	i18n.set_locale(prior.locale)
	root.get_node("GameAudio").shutdown()
	print("COMBAT STAGE LAYOUT: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
