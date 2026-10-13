extends "res://scripts/ui/screen_view.gd"
## Application adapter: owns coordination, not rendering or local layout.
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const CardZonePresenter = preload("res://scripts/presentation/card_zone_presenter.gd")
const CombatView = preload("res://scripts/ui/combat_view.gd")
var view: CombatView
var router
var controller
var inspection_coordinator
var presentation_driver
var presenter: Presenter
var zone_presenter: CardZonePresenter
var presentation: Dictionary
var render_epoch := 0
var _projection_key: Array = []
var _run_snapshot: Dictionary
var _combat_snapshot: Dictionary
var _action_snapshot: Array
var _zone_snapshot: Dictionary
var _scenery_key := ""
var _zone_reading_states: Dictionary = {}
var _collection_dialog: Window
var _prepared_card: Dictionary = {}
var _intro_handle
var _transfer_models: Dictionary = {}
var _active_motion
var _transits
var _motion_zones: Array = []
var selected_card: String:
	get: return controller.state.selected_card if controller else ""
var selected_target: String:
	get: return controller.state.selected_target if controller else ""
var submitting: bool:
	get: return controller.state.submitting if controller else false

func setup(owner, data: Dictionary) -> void:
	router = owner
	mount(owner.visual_context)
	controller = preload("res://scripts/ui/controllers/combat_controller.gd").new(GameSession, Playback, func(): return router.get_tree().paused)
	controller.changed.connect(_update_controls)
	inspection_coordinator = preload("res://scripts/ui/controllers/combat_inspection_coordinator.gd").new(controller)
	presentation_driver = preload("res://scripts/ui/controllers/presentation_director.gd").new(Playback, Preferences, func(): return router.get_tree().paused)
	presentation_driver.changed.connect(_update_controls)
	add_child(presentation_driver)
	view = preload("res://scripts/ui/combat_view.gd").new()
	view.scenery = router.shell.run_scenery.acquire_combat()
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	view.intent_requested.connect(_on_intent)
	add_child(view)
	Playback.frame_presented.connect(_on_frame)
	Playback.executor = _execute_beat
	Playback.settled.connect(_on_settled)
	GameSession.presentation_ready.connect(_on_presentation_ready)
	_render(data)
	if Playback.envelope != null and not Playback.dispatch_ready: _on_presentation_ready(Playback.envelope)

func refresh_state(data: Dictionary) -> void: _render(data)
func _render(data: Dictionary) -> void:
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	_run_snapshot = GameSession.run
	_combat_snapshot = GameSession.combat
	_action_snapshot = GameSession.legal_actions
	_zone_snapshot = GameSession.card_zones
	var scenery_key := JSON.stringify([visual_context.scenery_profile.presentation_key(),
		visual_context.setting_id, _run_snapshot.get("currentNodeId", ""), _combat_snapshot.get("encounterDefinitionId", "")])
	if scenery_key != _scenery_key and is_instance_valid(view.battlefield_view):
		view.scenery_model = _resolve_scenery()
		view.scenery.apply_model(presenter.combat_model(GameSession.input_actor_id()).read("actors"),
			str(view.scenery_model.get("backgroundId", "combat")), visual_context)
	_scenery_key = scenery_key
	var key: Array = [_run_snapshot, _combat_snapshot, _action_snapshot, _zone_snapshot, data, visual_context.presentation_key()]
	if key == _projection_key:
		_update_controls()
		return
	_projection_key = key.duplicate(true)
	render_epoch += 1
	presentation = data.duplicate(true)
	zone_presenter = CardZonePresenter.new(_zone_snapshot, visual_context.translator)
	presenter = Presenter.new(_run_snapshot, _combat_snapshot, _action_snapshot, visual_context.translator, presentation, _zone_snapshot)
	presenter.accept_evaluations(GameSession.card_evaluations)
	controller.bind(presenter, _hand_scope(), zone_presenter.playable_cards().map(func(card): return str(card.get("cardInstanceId", ""))))
	view.apply_projection(presentation, presenter, zone_presenter,
		{"run": _run_snapshot, "combat": _combat_snapshot, "actions": _action_snapshot, "zones": _zone_snapshot,
		"scope": _hand_scope(), "input_actor": GameSession.input_actor_id(), "setting": GameSession.selected_setting,
		"setting_id": GameSession.selected_setting_id,
		"scenery": _resolve_scenery()}, visual_context)
	_update_controls()
	# Real session refreshes already include the inspection in the same revision.
	# Standalone/fixture-mounted combat views can still request enrichment.
	if not GameSession.card_evaluations_ready: _load_inspection.call_deferred(render_epoch)

func _hand_scope() -> String:
	return JSON.stringify([_run_snapshot.get("runId", ""), _run_snapshot.get("branchId", ""),
		_combat_snapshot.get("combatId", ""), GameSession.input_actor_id(),
		_zone_snapshot.get("zones", []).filter(func(zone): return zone.get("allowsCardPlay", false)).map(func(zone): return zone.get("zoneId", ""))])


func _on_intent(action: String, arguments: Array) -> void:
	if action == "card_hover":
		if is_inside_tree() and not get_tree().paused and not view.hand.reading_modal: GameAudio.play_event("card_hover")
	elif action == "inspect_zone": _open_collection(arguments[0])
	elif action == "timeline": router.show_timeline()
	elif action == "pause": router.toggle_pause()
	elif action == "skip_presentation": presentation_driver.skip()
	elif action == "select_card":
		if controller.select_card(str(arguments[0])):
			GameAudio.select_card()
			view._clear_choices(); view._show_preview(str(arguments[0])); _update_controls()
	elif action == "_play_option":
		for option in presenter.action_options(str(arguments[1])):
			if option.reference == str(arguments[0]):
				if str(arguments[1]).is_empty(): _execute_system(option.read("candidate"))
				else: _play_candidate(option.read("candidate"))
				return
	elif action in ["_drop_card", "_select_target", "_end_turn", "_next_frame", "_choose_card", "_cancel_selection", "_reconnect", "_execute_zone_flow"]:
		callv(action, arguments)

func _update_controls() -> void:
	if not is_instance_valid(view): return
	view.apply_interaction(controller.interaction_model().to_dictionary())
	if is_instance_valid(_collection_dialog):
		var address: Dictionary = _collection_dialog.collection.model.address
		var zone: Dictionary = preload("res://scripts/presentation/card_collection_presenter.gd").find(GameSession.card_zones, address)
		if zone.is_empty() or str(_collection_dialog.get_meta("context")) != presenter.context_key(): _collection_dialog.queue_free()
		elif zone != _collection_dialog.get_meta("zone_snapshot"):
			var state: Dictionary = _collection_dialog.collection.capture_local_state()
			_collection_dialog.collection.apply_model(preload("res://scripts/presentation/card_collection_presenter.gd").build(zone, presenter, visual_context.translator))
			_collection_dialog.collection.restore_local_state(state)
			_collection_dialog.set_meta("zone_snapshot", zone)

func _open_collection(address: Dictionary) -> void:
	if not is_inside_tree() or get_tree().paused or view.hand.reading_modal: return
	var zone: Dictionary = preload("res://scripts/presentation/card_collection_presenter.gd").find(_zone_snapshot, address)
	if zone.is_empty(): return
	var model: Dictionary = preload("res://scripts/presentation/card_collection_presenter.gd").build(zone, presenter, visual_context.translator)
	var scope := JSON.stringify([GameSession.selected_setting_id, _run_snapshot.get("runId"), _run_snapshot.get("branchId"), _combat_snapshot.get("combatId"), address])
	var dialog = preload("res://scripts/ui/card_zone_dialog.gd").new(visual_context)
	_collection_dialog = dialog; dialog.setup(model); add_child(dialog)
	dialog.set_meta("context", presenter.context_key()); dialog.set_meta("zone_snapshot", zone)
	dialog.collection.restore_local_state(_zone_reading_states.get(scope, {}))
	dialog.card_requested.connect(func(id): _inspect_collection_card(dialog, id))
	dialog.tree_exiting.connect(func():
		if _zone_reading_states.size() >= 16: _zone_reading_states.erase(_zone_reading_states.keys()[0])
		_zone_reading_states[scope] = dialog.capture_local_state()
		for button in view.find_children("Pile_*", "Button", true, false):
			if button.get_meta("zone_address") == address and button.is_inside_tree(): button.grab_focus())
	view.hand.track_reading_modal(dialog); dialog.popup_centered_clamped(Vector2i(1100, 720), .9)
	GameAudio.play_event("open")
func _inspect_collection_card(dialog, id: String) -> void:
	if not is_instance_valid(dialog) or not dialog.collection.cards.has(id): return
	var scope := presenter.context_key()
	var model: Dictionary = dialog.collection.cards[id].model.duplicate(true)
	dialog.show_detail(id, model, {}, visual_context.translator.text("Loading details…"))
	var detail: Window = dialog.detail
	var key: Array = [GameSession.selected_setting_id, scope, _run_snapshot.get("contentRevision", ""), GameSession.input_actor_id()]
	var response: Dictionary = await inspection_coordinator.inspect_card(key, id)
	if not is_inside_tree() or not is_instance_valid(dialog) or not is_instance_valid(detail) or dialog.detail != detail or not detail.visible or scope != presenter.context_key(): return
	if not response.get("ok", false):
		detail.find_child("CollectionInspectionStatus", true, false).text = visual_context.translator.text("Detailed inspection is unavailable.")
		var retry := visual_context.styles.button(visual_context.translator.text("RECONNECT"))
		retry.pressed.connect(func(): if is_instance_valid(dialog): _inspect_collection_card(dialog, id))
		detail.content.add_child(retry); return
	presenter.accept_evaluations([response.data])
	model = presenter.card_view_model(id); model.availability = visual_context.translator.text("Inspect card")
	model["collectionTags"] = response.data.get("baseContainer", {}).get("tags", []).duplicate()
	if not bool(response.data.get("isInPlayableZone", false)) and response.data.get("evaluation", {}).get("costs", []).is_empty(): model.costsKnown = false
	detail.find_child("CollectionInspectionStatus", true, false).text = visual_context.translator.text("Published definition; contextual play cost unavailable.") if not model.costsKnown else ""
	var panel = detail.find_child("CardDetails", true, false)
	if panel:
		for child in panel.get_children(): panel.remove_child(child); child.queue_free()
		panel.configure(model, visual_context.card_style)
	dialog.collection.update_card(id, model)

func _locked() -> bool: return controller.locked()
func _resolve_scenery() -> Dictionary:
	var mappings = JSON.parse_string(FileAccess.get_file_as_string(visual_context.scenery_profile.mappings))
	if not mappings is Dictionary: mappings = {}
	return preload("res://scripts/presentation/scenery_presenter.gd").build(_run_snapshot, _combat_snapshot,
		GameSession.selected_setting_id, mappings).to_dictionary()
func _candidates(id: String) -> Array: return presenter.candidates(id)
func _toggle_character_sidebar() -> void: view._toggle_character_sidebar()
func replace_composition(scene: PackedScene, profile: Resource) -> void: view.replace_composition(scene, profile)
func _on_frame(frame: Dictionary, index: int, total: int) -> void:
	if _active_motion != null: visual_context.motion = _active_motion

func _on_presentation_ready(value) -> void:
	if not is_inside_tree() or value != Playback.envelope: return
	controller.presentation_pending = true
	_active_motion = visual_context.capture_motion()
	presentation_driver.watchdog_seconds = visual_context.motion.duration("watchdog")
	var payments: Array = []
	for cost in _prepared_card.get("costs", []):
		if cost.get("componentId") != null: payments.append({"componentId": cost.componentId, "sourceId": _prepared_card.get("cardInstanceId", "")})
	Playback.annotate({"payments": payments})
	_transfer_models.clear()
	for card in view.hand.ordered_cards(): _transfer_models[str(card.model.cardInstanceId)] = card.model
	var after: Dictionary = value.snapshot("after")
	_motion_zones = [value.snapshot("before").get("zones", {}), after.get("zones", {})]
	var final_presenter = Presenter.new(after.get("run", {}), value.snapshot("combat"), GameSession.legal_actions,
		visual_context.translator, presentation, after.get("zones", {}))
	final_presenter.accept_evaluations(after.get("evaluations", []))
	for zone in after.get("zones", {}).get("zones", []):
		if zone.get("contentsVisible", false):
			for card in zone.get("cards", []): _transfer_models[str(card.cardInstanceId)] = final_presenter.card_view_model(str(card.cardInstanceId))
	var final_cards: Array = CardZonePresenter.new(after.get("zones", {}), visual_context.translator).playable_cards()
	view.hand.begin_presentation(final_cards.map(func(card): return final_presenter.card_view_model(str(card.cardInstanceId))))
	if _transits != null: _transits.dispose()
	_transits = preload("res://scripts/ui/card_transit_registry.gd").new(view.hand, view.feedback.effects, visual_context)
	var prefix: Array = []
	if not _prepared_card.is_empty():
		var intro := _prepared_card.duplicate(true)
		intro["presentationOnly"] = "card_departure"
		prefix.append(intro)
	Playback.activate(prefix)
	if not prefix.is_empty(): presentation_driver.advance()
	elif not Playback.has_frames(): _on_settled("completed", Playback.generation)
	_update_controls()

func _execute_beat(frame: Dictionary, handle) -> void:
	if _active_motion != null: visual_context.motion = _active_motion
	if frame.get("presentationOnly", "") == "card_departure":
		_intro_handle = handle
		var id := str(frame.cardInstanceId)
		view.hand.clear_choice(); view.reset_preview()
		var card = view.hand.card_for(id)
		if not is_instance_valid(card): handle.finish("fallback"); return
		var ghost: Control = _transits.acquire(id, card.model, true, card.get_global_rect().get_center())
		var transits = _transits
		var destination: Vector2 = view.battlefield_view.get_global_rect().get_center()
		destination.y = view.battlefield_view.global_position.y + view.battlefield_view.size.y * .72
		GameAudio.card()
		var final_zones: Dictionary = Playback.envelope.snapshot("after").get("zones", {}) if Playback.envelope != null else {}
		var retained: bool = CardZonePresenter.new(final_zones, visual_context.translator).playable_cards().any(func(item): return str(item.get("cardInstanceId", "")) == id)
		var continued := false
		for beat in Playback.frames:
			var steps: Array = beat.get("cardZoneSteps", []).duplicate()
			for application in beat.get("applications", []): steps.append_array(application.get("cardZoneSteps", []))
			if steps.any(func(step): return id in step.get("instanceIds", [])): continued = true; break
		handle.completed.connect(func(_result):
			if not is_inside_tree(): return
			if not retained and not continued:
				view.hand.sync_models(view.hand.ordered_cards().filter(func(item): return item.model.cardInstanceId != id).map(func(item): return item.model), view._hand_scope())
			if retained or not continued or _result != "completed": transits.release(id, ghost, retained)
			view.card_buttons = view.hand.cards(), CONNECT_ONE_SHOT)
		var duration: float = visual_context.motion.duration("card_flight", true)
		if duration <= 0: handle.hit(); handle.finish(); return
		var tween := ghost.create_tween().set_parallel(); handle.own(tween)
		tween.tween_property(ghost, "global_position", destination - ghost.size * .5, duration).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		tween.tween_property(ghost, "rotation", 0.0, duration)
		tween.tween_property(ghost, "scale", Vector2.ONE * .82, duration)
		if retained:
			tween.chain().tween_property(ghost, "global_position", view.hand.destination_center(id) - ghost.size * .5, visual_context.motion.duration("retain", true)).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN_OUT)
			tween.parallel().tween_property(ghost, "scale", Vector2.ONE, visual_context.motion.duration("retain", true))
		tween.finished.connect(func(): handle.hit(); handle.finish(), CONNECT_ONE_SHOT)
	else:
		handle.hit(); GameAudio.play_beat(frame, visual_context.motion)
		if frame.get("stateAfter") is Dictionary: view.present_combat_snapshot(frame.stateAfter)
		view.present_frame(frame, Playback.index - 1, Playback.total)
		for application in frame.get("applications", []):
			var target := str(application.get("targetEntityId", ""))
			if view.battlefield_view.panels.has(target): view.battlefield_view.panels[target].present_resource(application, visual_context.motion.duration("impact", true))
		var steps: Array = frame.get("cardZoneSteps", []).duplicate(true)
		if steps.is_empty():
			for application in frame.get("applications", []): steps.append_array(application.get("cardZoneSteps", []))
		if not steps.is_empty() and Playback.envelope != null:
			preload("res://scripts/ui/card_zone_motion.gd").execute(steps,
				_motion_zones, _transfer_models, _zone_anchor, view.feedback.effects, visual_context, handle, _zone_role, _transits,
				func(item):
					if _zone_role(item.target) == "hand": GameAudio.play_event("card_move"))
			return
		if not frame.get("hasVisibleFacts", false) or visual_context.motion.duration("impact") <= 0: handle.finish(); return
		var tween := view.create_tween()
		handle.own(tween)
		tween.tween_interval(visual_context.motion.duration("impact"))
		tween.tween_callback(func(): handle.finish())

func _zone_anchor(address: String, id: String) -> Vector2:
	for button in view.find_children("Pile_*", "Button", true, false):
		if preload("res://scripts/ui/card_zone_motion.gd").address_key(button.get_meta("zone_address", {})) == address: return button.get_global_rect().get_center()
	for snapshot in _motion_zones:
		for zone in snapshot.get("zones", []):
			if zone.get("allowsCardPlay", false) and preload("res://scripts/ui/card_zone_motion.gd").address_key(zone) == address: return view.hand.destination_center(id)
	return Vector2.INF

func _zone_role(address: String) -> String:
	for snapshot in _motion_zones:
		for zone in snapshot.get("zones", []):
			if preload("res://scripts/ui/card_zone_motion.gd").address_key(zone) == address:
				return "hand" if zone.get("allowsCardPlay", false) else "pile"
	return ""

func _on_settled(_result: String, generation: int) -> void:
	if generation != Playback.generation or not is_inside_tree(): return
	_prepared_card = {}; _intro_handle = null
	if _transits != null: _transits.dispose(); _transits = null
	view.hand.end_presentation()
	_transfer_models.clear()
	_update_controls()
	_finish_navigation.call_deferred(generation, router.navigation_epoch)

func _finish_navigation(generation: int, epoch: int) -> void:
	if not is_inside_tree() or generation != Playback.generation or epoch != router.navigation_epoch: return
	if GameSession.synchronized and not GameSession.busy:
		controller.presentation_pending = false
		router.open_game()
		Playback.release_facts()
func apply_model(model) -> void: refresh_state(model)
func apply_visual_profile(_profile) -> void: refresh_state(presentation)
func apply_interaction(_state) -> void: _update_controls()
func capture_local_state() -> Dictionary: return view.capture_local_state()
func restore_local_state(state: Dictionary) -> void: view.restore_local_state(state)
func dispose() -> void:
	if _transits != null: _transits.dispose(); _transits = null
	if is_instance_valid(_collection_dialog): _collection_dialog.queue_free()
	if controller: controller.dispose()
	if inspection_coordinator: inspection_coordinator.dispose()
	if presentation_driver: presentation_driver.dispose()
	if is_instance_valid(view): view.dispose()
	if is_instance_valid(view) and is_instance_valid(view.scenery) and is_instance_valid(router): router.shell.run_scenery.release_combat(view.scenery)
	if Playback.frame_presented.is_connected(_on_frame): Playback.frame_presented.disconnect(_on_frame)
	if Playback.settled.is_connected(_on_settled): Playback.settled.disconnect(_on_settled)
	if GameSession.presentation_ready.is_connected(_on_presentation_ready): GameSession.presentation_ready.disconnect(_on_presentation_ready)
	Playback.executor = Callable()

func _drop_card(id: String, point: Vector2) -> void:
	if _locked(): return
	var actor_id := view.target_at(point)
	if not actor_id.is_empty() and presenter.candidates(id).any(func(candidate): return actor_id in presenter.targets(candidate)):
		if controller.select_card(id): _select_target(actor_id)
		return
	if view.contains_battlefield_point(point):
		var candidates := view._candidates(id).filter(func(candidate): return view._targets(candidate).is_empty())
		if not candidates.is_empty() and controller.select_card(id): _offer_candidates(candidates)


func _select_target(actor_id: String) -> void:
	var matches: Array = controller.target_options(actor_id)
	if not matches.is_empty(): _offer_candidates(matches)


func _play_candidate(candidate: Dictionary) -> void:
	if _locked() or candidate.is_empty():
		return
	var id := str(candidate.command.get("cardInstanceId", ""))
	_prepared_card = {"cardInstanceId": id, "targets": presenter.targets(candidate), "costs": candidate.get("costs", [])}
	view.hand.prepare_use(id)
	var success: bool = await controller.submit(candidate)
	if not is_inside_tree():
		return
	if success:
		var accepted_generation: int = Playback.generation
		if _intro_handle != null and not _intro_handle.done: await _intro_handle.completed
		if is_inside_tree() and accepted_generation == Playback.generation and not Playback.has_blocking_work() and GameSession.synchronized: router.open_game()
	else:
		view.hand.reject_use(id)
		_prepared_card = {}
		GameAudio.error()
		_update_controls()


func _end_turn() -> void:
	_execute_system(view._system_candidate(["END_TURN"]))


func _execute_system(candidate: Dictionary) -> void:
	var accepted: bool = await controller.submit(candidate)
	if not is_inside_tree(): return
	if accepted:
		GameAudio.turn()
		if not Playback.has_blocking_work(): router.open_game()
	else: GameAudio.error()


func _next_frame() -> void:
	presentation_driver.advance()


func _choose_card(id: String) -> void:
	if _locked():
		return
	var matches := view._candidates(id)
	if selected_card == id and matches.size() == 1:
		_play_candidate(matches[0])
		return
	if not controller.select_card(id): return
	GameAudio.select_card()
	view._clear_choices()
	view._show_preview(id)
	_update_controls()
	if matches.all(func(candidate): return view._targets(candidate).is_empty()):
		_offer_candidates(matches)


func _cancel_selection() -> void:
	if not controller.cancel(): return
	GameAudio.cancel()
	view.reset_preview()
	_update_controls()


func _offer_candidates(candidates: Array) -> void:
	view._clear_choices()
	if candidates.size() == 1:
		_play_candidate(candidates[0])
		return
	view.show_options(candidates)

func _reconnect() -> void:
	var accepted: bool = await controller.reconnect()
	if not is_inside_tree():
		return
	if accepted:
		if not Playback.has_blocking_work(): router.open_game()


func _load_inspection(ticket: int) -> void:
	var inspection_key: Array = [view._hand_scope(), _run_snapshot.get("sequence", 0), _combat_snapshot.get("step", 0),
		_run_snapshot.get("contentRevision", _run_snapshot.get("contentRevisionId", "")), GameSession.input_actor_id()]
	var representative_targets: Array = []
	if not selected_target.is_empty(): representative_targets = [selected_target]
	for candidate in _action_snapshot:
		if not representative_targets.is_empty(): break
		var targets: Array = presenter.targets(candidate)
		if not targets.is_empty():
			representative_targets = [targets[0]]
			break
	var response: Dictionary = await inspection_coordinator.inspect(inspection_key, representative_targets)
	if not is_inside_tree() or ticket != render_epoch or not response.ok:
		return
	presenter.accept_evaluations(response.data.get("cards", []))
	view.refresh_cards()
	_update_controls()


func _execute_zone_flow(payload: Dictionary) -> void:
	var accepted: bool = await controller.execute_zone_flow(payload)
	if is_inside_tree() and accepted and not Playback.has_blocking_work(): router.open_game()
