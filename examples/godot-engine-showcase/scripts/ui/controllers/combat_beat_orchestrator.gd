class_name CombatBeatOrchestrator
extends RefCounted
## Pure/stateless beat execution helpers for CombatScreen.
## Keeps receipt validation, zone-anchor resolution, and beat execution isolated from screen routing.

const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const CardZonePresenter = preload("res://scripts/presentation/card_zone_presenter.gd")
const Envelope = preload("res://scripts/presentation/presentation_envelope.gd")
const CardZoneMotion = preload("res://scripts/ui/card_zone_motion.gd")
const CardUseMotion = preload("res://scripts/ui/card_use_motion.gd")
const EffectMotion = preload("res://scripts/ui/effect_motion.gd")
const Handle = preload("res://scripts/presentation/presentation_handle.gd")


static func extract_card_zone_steps(frame: Dictionary, deep := false) -> Array:
	var steps: Array = frame.get("cardZoneSteps", []).duplicate(deep)
	if deep and not steps.is_empty():
		return steps
	for application in frame.get("applications", []):
		steps.append_array(application.get("cardZoneSteps", []))
	return steps


static func build_payment_annotations(prepared_card: Dictionary) -> Array:
	var payments: Array = []
	for cost in prepared_card.get("costs", []):
		if cost.get("componentId") != null:
			payments.append({"componentId": cost.componentId, "sourceId": prepared_card.get("cardInstanceId", "")})
	return payments


static func collect_zone_anchors_and_roles(view, motion_zones: Array) -> Dictionary:
	var pile_anchors: Dictionary = {}
	var zone_roles: Dictionary = {}
	var buttons: Array = view.pile_buttons() if view.has_method("pile_buttons") else view.find_children("Pile_*", "Button", true, false)
	for button in buttons:
		pile_anchors[CardZoneMotion.address_key(button.get_meta("zone_address", {}))] = button
	for snapshot in motion_zones:
		for zone in snapshot.get("zones", []):
			zone_roles[CardZoneMotion.address_key(zone)] = "hand" if zone.get("allowsCardPlay", false) else "pile"
	return {
		"pile_anchors": pile_anchors,
		"zone_roles": zone_roles,
	}


static func prepare_envelope_presentation(envelope, view, visual_context, presentation: Dictionary) -> Dictionary:
	var transfer_models: Dictionary = {}
	for card in view.hand.ordered_cards():
		transfer_models[str(card.model.cardInstanceId)] = card.model
	var after: Dictionary = envelope.snapshot("after")
	var motion_zones: Array = [envelope.snapshot("before").get("zones", {}), after.get("zones", {})]
	var anchors := collect_zone_anchors_and_roles(view, motion_zones)
	var final_presenter := Presenter.new(
		after.get("run", {}),
		envelope.snapshot("combat"),
		after.get("actions", []),
		visual_context.translator,
		presentation,
		after.get("zones", {})
	)
	final_presenter.accept_evaluations(after.get("evaluations", []))
	final_presenter.accept_presentations(after.get("presentations", []))
	for zone in after.get("zones", {}).get("zones", []):
		if zone.get("contentsVisible", false):
			for card in zone.get("cards", []):
				transfer_models[str(card.cardInstanceId)] = final_presenter.card_view_model(str(card.cardInstanceId))
	var final_cards: Array = CardZonePresenter.new(after.get("zones", {}), visual_context.translator).playable_cards()
	var final_models := final_cards.map(func(card): return final_presenter.card_view_model(str(card.cardInstanceId)))
	return {
		"transfer_models": transfer_models,
		"motion_zones": motion_zones,
		"pile_anchors": anchors.pile_anchors,
		"zone_roles": anchors.zone_roles,
		"final_models": final_models,
	}


static func matches_receipt_departure(receipt: Dictionary, prepared_card: Dictionary, current_run_id: Variant, expected_run_id: Variant, expected_combat_id: Variant) -> bool:
	if prepared_card.is_empty():
		return false
	if int(receipt.get("sequence", -1)) != int(prepared_card.get("sequence", -2)) + 1:
		return false
	if current_run_id != expected_run_id:
		return false
	var receipt_state := Envelope.dictionary(receipt.get("state"))
	var combat := Envelope.dictionary(receipt_state.get("combat"))
	return combat.get("combatId") == expected_combat_id


static func can_animate_beat(
	frame: Dictionary,
	playback_frames: Array,
	motion_zones: Array,
	zone_anchor: Callable,
	zone_role: Callable,
	view,
	visual_context,
	has_prepared_card: bool
) -> bool:
	if frame.get("presentationOnly", "") == "card_departure":
		var id := str(frame.get("cardInstanceId", ""))
		return not CardZoneMotion.uses_burn(playback_frames, id, visual_context.motion) \
			and visual_context.motion.duration("card_flight", true) > 0 \
			and is_instance_valid(view.hand.card_for(id))
	var steps := extract_card_zone_steps(frame, false)
	if not steps.is_empty() and not CardZoneMotion.resolve(steps, motion_zones, zone_anchor, visual_context.motion, zone_role).is_empty():
		return true
	return EffectMotion.can_animate(
		frame,
		visual_context,
		view.actor_portraits,
		view.battlefield_view.panels,
		has_prepared_card
	)
