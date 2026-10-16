extends "res://tests/visual_baseline.gd"
## Production router and setup-before-add_child order. No live API or personal saves.
func active_screen(expected: String, label: String) -> void:
	check(router.current_screen == expected and router.host.get_child_count() == 1 and router.host.get_child(0).is_inside_tree(), label)
func _run() -> void:
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway)
	session.synchronized = true
	root.get_node("Preferences").reduced_motion = true
	root.get_node("Preferences").auto_animations = false
	router = load("res://main.tscn").instantiate(); root.add_child(router); await settle()
	for cycle in 3:
		root.get_node("I18n").set_locale("pt_BR" if cycle == 1 else "en")
		load_combat(); load_journey(false); router.open_game(); await settle()
		active_screen("map", "entry %s: active run mounts map through production router" % cycle)
		check(router.host.get_child(0).view._inspector.get_child_count() > 0, "initial map inspector is fully projected before attachment")
		check(not router.host.get_child(0).find_child("MapResumeRun",true,false).visible, "map-only run does not expose a misleading resume action")
		var map: Dictionary = session._run.map.duplicate(true)
		load_combat(); session._run["map"] = map; session._run["currentNodeId"] = "encounter"
		router.open_game(); await settle()
		active_screen("combat", "encounter entry mounts combat after detached setup")
		check(router.host.get_child(0).view.card_buttons.size() == 5, "combat hand and controls finish mounting")
		router.toggle_pause(); await settle()
		check(paused and is_instance_valid(router.pause_layer), "pause opens above combat")
		router.toggle_pause(); await settle(); check(not paused, "resume preserves active combat")
		router.show_map(); await settle(); active_screen("map", "combat map inspection mounts without lifecycle errors")
		var return_to_combat = router.host.get_child(0).find_child("MapResumeRun",true,false)
		check(return_to_combat.visible, "map exposes return to active combat")
		return_to_combat.pressed.emit(); await settle(); active_screen("combat", "map return button restores the active encounter")
		load_journey(true); router.open_game(); await settle(); active_screen("activity", "completed combat routes to card reward")
		for activity in ["reward", "shop", "preparation", "upgrade"]:
			load_journey(true)
			match activity:
				"shop":
					session._run.map.nodes[1].activity.type = "Shop"
					session._available_commands = [{"type":"BUY_SHOP_ITEM","validPayload":{"shopInstanceId":"qa-shop","itemIds":["qa-item"]}}]
				"preparation":
					session._run.map.nodes[1].activity.type = "Preparation"
					session._available_commands = [{"type":"APPLY_PREPARATION_OPTION","validPayload":{"preparationInstanceId":"qa-preparation","optionIds":["qa-rest"]}}]
				"upgrade":
					session._run.map.nodes[1].activity.type = "CardUpgrade"
					session._available_commands = [{"type":"UPGRADE_CARD","validPayload":{"options":[{"cardInstanceId":"baseline-0","cardDefinitionId":"basic_attack","upgradeId":"qa-upgrade"}]}}]
			router.open_game(); await settle(); active_screen("activity", activity + ": activity mounts")
			var before := JSON.stringify([session.run,session.activity_choices()])
			var buttons: Array = router.host.get_child(0).find_children("*","Button",true,false).filter(func(button): return button.text == root.get_node("I18n").text("MAP"))
			check(buttons.size() == 1, activity + ": UI exposes a map inspection button")
			if not buttons.is_empty(): buttons[0].pressed.emit()
			await settle(); active_screen("map", activity + ": UI map button opens inspection")
			var return_button = router.host.get_child(0).find_child("MapResumeRun",true,false)
			check(return_button != null and return_button.visible, activity + ": map exposes return to pending activity")
			check(return_button.text == root.get_node("I18n").text("Return to activity"), activity + ": resume label follows current locale")
			router.host.get_child(0).view._inspect("boss")
			router.host.get_child(0).view._set_inspector_open(false)
			check(return_button.visible, activity + ": return remains reachable while inspecting another node with details collapsed")
			if return_button != null: return_button.pressed.emit()
			await settle(); active_screen("activity", activity + ": return button retains pending activity")
			check(JSON.stringify([session.run,session.activity_choices()]) == before, activity + ": navigation preserves snapshot and choices")
		load_journey(false); session._run.map.nodes[0].activity.type = "Dialogue"
		session._run["dialogues"] = [{"dialogueInstanceId":"qa-dialogue","nodeId":"speech","activityNodeId":"encounter","title":{"text":"QA conversation"},"speaker":{"text":"QA guide"},"text":{"text":"Read this before choosing."},"choices":[{"choiceId":"listen","text":{"text":"Listen"},"available":true}]}]
		session._available_commands = [{"type":"CHOOSE_DIALOGUE_OPTION","validPayload":{"dialogueInstanceId":"qa-dialogue","nodeId":"speech","choiceIds":["listen"]}}]
		router.open_game(); await settle(); active_screen("dialogue", "dialogue mounts independently")
		router.host.get_child(0).view.find_child("Conversation_map",true,false).pressed.emit(); await settle()
		active_screen("map", "dialogue map button opens local inspection")
		var resume = router.host.get_child(0).find_child("MapResumeDialogue",true,false)
		check(resume != null, "same-node dialogue has a resume action")
		if resume != null: resume.pressed.emit()
		await settle(); active_screen("dialogue", "resume action restores conversation")
		session._run.map["resolvedNodeIds"] = ["encounter"]
		session._available_commands = [{"type":"ADVANCE_NODE","targetNodeIds":["reward","boss"]}]
		router.dialogue_return_barrier = true; router.open_game(); await settle()
		active_screen("map", "resolved dialogue returns to map with its return barrier")
		check(router.dialogue_return_barrier, "navigation preserves dialogue return barrier")
	# Replace screens before deferred focus/layout/workflow callbacks run.
	for _i in 6:
		load_journey(true); router.show_activity(); router.show_map(); router.open_game()
	await settle(); active_screen("activity", "rapid replacement safely drains deferred callbacks")
	print("Fixture reads: ", gateway.calls)
	check(gateway.calls.all(func(call): return call == "inspect_hand"), "navigation submits no commands; only isolated combat-hand reads are allowed")
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
