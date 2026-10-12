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
	presentation_driver = preload("res://scripts/ui/controllers/combat_presentation_driver.gd").new(Playback, Preferences, func(): return router.get_tree().paused)
	presentation_driver.changed.connect(_update_controls)
	add_child(presentation_driver)
	view = preload("res://scripts/ui/combat_view.gd").new()
	view.scenery = router.shell.run_scenery.acquire_combat()
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	view.intent_requested.connect(_on_intent)
	add_child(view)
	Playback.frame_presented.connect(_on_frame)
	_render(data)

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
	controller.bind(presenter, _hand_scope(), zone_presenter.playable_cards().map(func(card): return str(card.get("cardInstanceId", ""))))
	view.apply_projection(presentation, presenter, zone_presenter,
		{"run": _run_snapshot, "combat": _combat_snapshot, "actions": _action_snapshot, "zones": _zone_snapshot,
		"scope": _hand_scope(), "input_actor": GameSession.input_actor_id(), "setting": GameSession.selected_setting,
		"setting_id": GameSession.selected_setting_id,
		"scenery": _resolve_scenery()}, visual_context)
	_update_controls()
	_load_inspection.call_deferred(render_epoch)

func _hand_scope() -> String:
	return JSON.stringify([_run_snapshot.get("runId", ""), _run_snapshot.get("branchId", ""),
		_combat_snapshot.get("combatId", ""), GameSession.input_actor_id(),
		_zone_snapshot.get("zones", []).filter(func(zone): return zone.get("allowsCardPlay", false)).map(func(zone): return zone.get("zoneId", ""))])


func _on_intent(action: String, arguments: Array) -> void:
	if action == "inspect_zone": _open_collection(arguments[0])
	elif action == "timeline": router.show_timeline()
	elif action == "pause": router.toggle_pause()
	elif action == "select_card":
		if controller.select_card(str(arguments[0])):
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
	view.present_frame(frame, index, total)
	GameAudio.hit()
func apply_model(model) -> void: refresh_state(model)
func apply_visual_profile(_profile) -> void: refresh_state(presentation)
func apply_interaction(_state) -> void: _update_controls()
func capture_local_state() -> Dictionary: return view.capture_local_state()
func restore_local_state(state: Dictionary) -> void: view.restore_local_state(state)
func dispose() -> void:
	if is_instance_valid(_collection_dialog): _collection_dialog.queue_free()
	if controller: controller.dispose()
	if inspection_coordinator: inspection_coordinator.dispose()
	if presentation_driver: presentation_driver.dispose()
	if is_instance_valid(view): view.dispose()
	if is_instance_valid(view) and is_instance_valid(view.scenery) and is_instance_valid(router): router.shell.run_scenery.release_combat(view.scenery)
	if Playback.frame_presented.is_connected(_on_frame): Playback.frame_presented.disconnect(_on_frame)

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
	var success: bool = await controller.submit(candidate)
	if not is_inside_tree():
		return
	if success:
		GameAudio.card()
		var still_present := zone_presenter.playable_cards().any(func(card): return str(card.get("cardInstanceId", "")) == id)
		await view.animate_card_use(id, presenter.targets(candidate), still_present)
		if is_inside_tree():
			router.open_game()
	else:
		_update_controls()


func _end_turn() -> void:
	_execute_system(view._system_candidate(["END_TURN"]))


func _execute_system(candidate: Dictionary) -> void:
	var accepted: bool = await controller.submit(candidate)
	if is_inside_tree() and accepted: router.open_game()


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
	GameAudio.ui()
	view._clear_choices()
	view._show_preview(id)
	_update_controls()
	if matches.all(func(candidate): return view._targets(candidate).is_empty()):
		_offer_candidates(matches)


func _cancel_selection() -> void:
	if not controller.cancel(): return
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
		router.open_game()


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
	if is_inside_tree() and accepted: router.open_game()
