extends SceneTree
## Actual header/view nodes with local projections; no engine commands or real saves.
const Context = preload("res://scripts/ui/visual_context.gd")
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const Zones = preload("res://scripts/presentation/card_zone_presenter.gd")
var failures := 0
func _init() -> void: run.call_deferred()
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 12: await process_frame
func model(context, name: String) -> Dictionary:
	return {"actorName": name, "actorDefinitionId": "spire_adept", "round": context.translator.text("ROUND %s") % 42,
		"turn": context.translator.text("Turn: %s") % name, "support": "", "supportHint": "",
		"log": context.translator.text("Combat log"), "pause": "%s [%s]" % [context.translator.text("PAUSE"), context.action_label("pause_game")],
		"inspect": "%s [%s]" % [context.translator.text("Inspect card"), context.action_label("inspect_card")]}
func present(view, data: Dictionary, context) -> void:
	var cards = Zones.new(data.zones, context.translator)
	var presenter = Presenter.new(data.run, data.combat, [], context.translator, data.presentation, data.zones)
	view.apply_projection(data.presentation, presenter, cards,
		{"run": data.run, "combat": data.combat, "actions": [], "zones": data.zones, "input_actor": "hero", "scope": "header-test", "setting": {}, "setting_id": "prototype-test"}, context)
	view.apply_interaction({"synchronized": true})
func run() -> void:
	var i18n = root.get_node("I18n")
	var original_locale: String = i18n.locale
	var session_before: Dictionary = root.get_node("GameSession").run.duplicate(true)
	var profile = load("res://data/themes/mvp_combat_theme.tres")
	var profile_before: String = profile.presentation_key()
	var header = load("res://scenes/ui/components/mvp_combat_header.tscn").instantiate()
	root.add_child(header)
	await settle()
	header.set_anchors_preset(Control.PRESET_TOP_LEFT)
	for language in ["en", "pt_BR"]:
		i18n.set_locale(language)
		for text_scale in [1.0, 1.6]:
			var context = Context.new(i18n, {"text_scale": text_scale, "action_labels": {"pause_game": "F12", "inspect_card": "Q"}}, profile)
			for width in [960.0, 1280.0, 1920.0]:
				for name in ["Ember Adept", "An exceptionally long published actor name that must never move the central round"]:
					header.size = Vector2(width, 88)
					header.apply_model(model(context, name), context)
					await settle()
					header.size.y = header.get_combined_minimum_size().y
					await settle()
					var label := "%s scale=%s width=%s long=%s" % [language, text_scale, width, name.length() > 20]
					check(absf(header.round_label.get_global_rect().get_center().x - header.get_global_rect().get_center().x) < 1, "round stays centered: " + label)
					var left: Rect2 = header.get_node("ActiveIdentity").get_global_rect()
					var middle: Rect2 = header.get_node("RoundAndTurn").get_global_rect()
					var right: Rect2 = header.get_node("HeaderActions").get_global_rect()
					check(left.end.x <= middle.position.x and middle.end.x <= right.position.x and header.get_global_rect().encloses(right), "regions do not overlap: " + label)
					check(header.actor_name_label.tooltip_text == name and header.pause_button.tooltip_text.contains("F12") and header.inspect_button.tooltip_text.contains("Q"), "full names and mapped shortcuts remain accessible: " + label)
					check(header.get_global_rect().encloses(header.active_turn_label.get_global_rect()), "text scaling remains inside header: " + label)
	check(not header.has_node("TimelineButton") and not header.has_node("ZoneToolsButton"), "MVP header has no hidden compatibility buttons")
	check(header.portrait.texture != null and header.portrait.stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED, "portrait uses existing artwork without distortion")
	var data: Dictionary = model(Context.new(), "Unknown visual")
	data.actorDefinitionId = "no-such-visual"
	header.apply_model(data, Context.new())
	check(header.portrait.texture != null, "unknown portrait uses the current catalog fallback")
	var events: Array = []
	header.pause_requested.connect(func(): events.append("pause"))
	header.log_requested.connect(func(): events.append("log"))
	header.inspection_requested.connect(func(): events.append("inspect"))
	for repeat in 3: header.apply_model(data)
	header.set_inspection_available(false); header.inspect_button.pressed.emit()
	check(events.is_empty(), "inspection is unavailable without a card")
	header.set_inspection_available(true)
	header.pause_button.pressed.emit(); header.log_button.pressed.emit(); header.inspect_button.pressed.emit()
	check(events == ["pause", "log", "inspect"], "authored header actions emit exactly once after refreshes")
	header.queue_free(); await process_frame
	i18n.set_locale(original_locale)
	var view = load("res://scenes/ui/components/combat_mvp_view.tscn").instantiate()
	view.size = Vector2(1920,1080); root.add_child(view)
	var snapshot = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/ui_editor_preview.json"))
	var context = Context.new(null, {"reduced_motion": true}, profile)
	present(view, snapshot, context)
	await settle()
	var hand_position: Vector2 = view.hand.global_position
	var card_ids: Array = view.hand.ordered_cards().map(func(card): return card.get_instance_id())
	var enemy: Dictionary = snapshot.combat.actors[1]
	snapshot.combat.activation.activeActorId = enemy.instanceId
	snapshot.run.supportCombat = {"supportId": "water_elemental", "routineId": "water_routine", "remainingOwnerActivations": 3.0}
	var before := JSON.stringify(snapshot)
	present(view, snapshot, context); await settle()
	check(view.header_view.actor_name_label.text == view.presenter.actor_name(enemy.instanceId) and view.header_view.active_turn_label.text.ends_with(view.presenter.actor_name(enemy.instanceId)), "identity and turn follow the published active actor")
	check(view.header_view.support_label.visible and view.header_view.support_label.text.contains("Water Elemental") and view.header_view.support_label.text.contains("3"), "remaining activations are explicitly associated with the support")
	check(JSON.stringify(snapshot) == before and view.hand.global_position == hand_position and view.hand.ordered_cards().map(func(card): return card.get_instance_id()) == card_ids, "header refresh preserves inputs, card instances and hand position")
	var authoritative_before := JSON.stringify(view._combat_snapshot)
	var frame_state: Dictionary = snapshot.combat.duplicate(true)
	frame_state.activation.activeActorId = "hero"; frame_state.activation.round = 17
	view.present_combat_snapshot(frame_state); await settle()
	check(view.header_view.round_label.text.contains("17") and view.header_view.active_turn_label.text.ends_with(view.presenter.actor_name("hero")), "published intermediate snapshots update header during playback")
	check(JSON.stringify(view._combat_snapshot) == authoritative_before, "intermediate header display does not overwrite the authoritative snapshot")
	snapshot.run.erase("supportCombat"); snapshot.combat.activation.activeActorId = ""
	present(view, snapshot, context); await settle()
	check(not view.header_view.support_label.visible and not view.header_view.active_turn_label.visible, "absent support and activation data are omitted, not invented")
	check(not view.hand.toolbar.get_node("HandInspectButton").visible, "inspection is moved to the header without a duplicate dock action")
	view.hand.clear_choice(); view._refresh_header_inspection()
	check(view.header_view.inspect_button.disabled, "header inspection starts disabled without an inspection anchor")
	view.hand._focus(view.hand.order.front()); await settle()
	check(not view.header_view.inspect_button.disabled, "existing hand focus enables header inspection")
	view.header_view.inspect_button.pressed.emit(); await settle()
	var inspections: Array = view.get_children().filter(func(child): return child is InspectionDialogView)
	check(inspections.size() == 1 and inspections[0].visible and view.hand.reading_modal, "header magnifier opens the existing inspection component")
	for dialog in inspections: dialog.queue_free()
	await settle()
	check(not view.hand.reading_modal and not view.header_view.inspect_button.disabled, "closing inspection releases input and restores availability")
	var intents: Array = []
	view.intent_requested.connect(func(action, _arguments): intents.append(action))
	view.header_view.pause_button.pressed.emit()
	check(intents == ["pause"], "pause retains the existing application intent")
	view.header_view.log_button.pressed.emit()
	check(view.get_node("CombatLog").visible, "log action opens the existing combat log")
	view.get_node("CombatLog").hide()
	check(profile.presentation_key() == profile_before and root.get_node("GameSession").run == session_before, "header never mutates theme or session")
	view.queue_free(); await process_frame
	change_scene_to_file("res://scenes/ui/components/mvp_combat_header.tscn")
	await scene_changed
	await settle()
	check(current_scene.portrait.texture != null and not current_scene.round_label.text.is_empty(), "F6-style standalone header populates its own local preview")
	current_scene.queue_free(); current_scene = null
	await process_frame
	quit(1 if failures else 0)
