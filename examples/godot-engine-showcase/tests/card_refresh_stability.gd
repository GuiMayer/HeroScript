extends SceneTree
## Delayed engine reads must never publish a generic intermediate card face.
const Session = preload("res://scripts/application/game_session.gd")
const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
var failures := 0
var completions: Array = []
class Fixture extends RefCounted:
	signal release
	var held := false
	var fail := false
	var stale := false
	var calls := 0
	var sequence := 1
	var version_overrides: Dictionary = {}
	func read_run(_id: String) -> Dictionary:
		return {"ok": true, "data": {"runId": "run", "sequence": sequence, "contentRevision": "revision", "activeEncounterId": "combat"}}
	func read_projections(_run: String, _combat: String, _include: bool) -> Dictionary:
		return {"combat": {"ok": true, "data": {"combatId": "combat", "step": sequence, "status": "ACTIVE", "activeActorId": "hero"}},
			"cardZones": {"ok": true, "data": {"zones": [{"zoneId": "play", "allowsCardPlay": true, "contentsVisible": true,
				"cards": [{"cardInstanceId": "card", "definitionId": "strike"}]}]}},
			"commands": {"ok": true, "data": {"commands": []}}, "capabilities": {"ok": true, "data": {}}}
	func read_legal_actions(_combat: String, _actor: String) -> Dictionary:
		return {"ok": true, "data": {"candidates": [{"command": {"cardInstanceId": "card", "targetIds": ["enemy"]}, "costs": [{"resourceId": "energy", "amount": 1}]}]}}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		calls += 1
		if held: await release
		if fail: return {"ok": false, "error": "Offline fixture"}
		var effect := {"type": "DAMAGE", "target": "TARGET", "targetResource": "health", "flatValue": 8}
		var version := {"runId": "run", "combatId": "combat", "contentRevision": "revision", "runSequence": sequence - 1 if stale else sequence, "combatStep": sequence}
		version.merge(version_overrides, true)
		return {"ok": true, "data": {"cards": [{"version": version,
			"baseContainer": {"rarity": "Common"}, "evaluation": {"cardInstanceId": "card"},
			"compiledContainer": {"components": [{"componentId": "impact", "effect": effect}]},
			"effectiveBase": {"components": [{"componentId": "impact", "effect": effect}]},
			"previewSteps": [{"applied": true, "provenance": {"componentId": "impact"}, "identity": {"parentProcId": null},
				"calculation": {"baseValue": 8, "quantity": {"incorporatedStages": [{"stageId": "source", "scope": "Actor"}]},
					"buckets": [{"stageId": "source", "output": 8 + sequence}]}}]}]}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func refresh(session) -> void: completions.append(await session.refresh())
func settle() -> void:
	for _frame in 6: await process_frame
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new()
	var hand = load("res://scripts/ui/card_hand_view.gd").new(context)
	hand.size = Vector2(1000, 500); root.add_child(hand)
	var fixture := Fixture.new()
	var session = Session.new(); root.add_child(session); session.configure(fixture)
	session._run = {"runId": "run"}
	var published: Array = []
	session.changed.connect(func():
		if not session.synchronized: hand.set_read_only(true); return
		var presenter = Presenter.new(session.run, session.combat, session.legal_actions, context.translator,
			{"cards": {"strike": {"text": "GENERIC FALLBACK"}}}, session.card_zones)
		presenter.accept_evaluations(session.card_evaluations)
		var model: Dictionary = presenter.card_view_model("card")
		published.append(model.summary)
		hand.sync_models([model], "combat"); hand.set_read_only(false))
	check(await session.refresh(), "initial refresh publishes a complete card projection")
	await settle()
	var card = hand.card_for("card")
	var rules = card.find_child("Rules", true, false)
	var name_label = card.find_child("CardName", true, false)
	var cost = card.find_child("CostSymbol", true, false)
	var initial: String = rules.get_parsed_text()
	check(initial.contains("9") and not initial.contains("GENERIC"), "first published face already contains the engine value")
	fixture.sequence = 2; fixture.held = true
	refresh(session)
	for _frame in 12:
		await process_frame
		check(rules.get_parsed_text() == initial and published.size() == 1, "delayed inspection keeps the published face, frame %s" % _frame)
	check(session.busy and session.run.sequence == 1, "pending refresh does not expose partial state or allow another command")
	fixture.release.emit(); await settle()
	check(completions == [true] and published.size() == 2 and published[1].contains("10"), "new authoritative value is published once after inspection")
	check(hand.card_for("card") == card and card.find_child("Rules", true, false) == rules and card.find_child("CardName", true, false) == name_label and card.find_child("CostSymbol", true, false) == cost, "effect, title and cost nodes survive a changed value")
	check(published.all(func(text): return not text.contains("GENERIC")), "no generic fallback was observable between revisions")
	var copies: Array = session.card_evaluations; copies.clear()
	check(session.card_evaluations.size() == 1, "inspection snapshot is defensively copied")
	var model: Dictionary = card.model.duplicate(true)
	var count: int = card.configuration_count
	model.inspectionText = "Different execution journal"
	card.configure(model)
	check(card.configuration_count == count and card.model.inspectionText == model.inspectionText and card.find_child("Rules", true, false) == rules, "journal-only updates keep the face intact but update inspection data")
	model["presentationFacts"] = {"version": {"runSequence": 42}, "impacts": [{"targetEntityId": "other"}]}
	card.configure(model)
	check(card.configuration_count == count and card.model.presentationFacts.version.runSequence == 42 and card.find_child("Rules", true, false) == rules, "presentation versions and impact traces enrich inspection without rebuilding the face")
	context._options.text_scale = 1.2
	card.configure(model)
	var font_size: int = rules.get_theme_font_size("normal_font_size")
	var face_count: int = card.configuration_count
	for index in 4:
		model.availability = "Availability %s" % index
		card.configure(model)
		check(card.configuration_count == face_count and card.find_child("Rules", true, false) == rules and rules.get_theme_font_size("normal_font_size") == font_size, "playability updates do not rebuild the printed face, update %s" % index)
	var changed_style = card.visual_style.duplicate(true)
	changed_style.rules_font_size += 2
	card.visual_style = changed_style; card.configure(model)
	check(rules.get_theme_font_size("normal_font_size") == roundi(changed_style.rules_font_size * 1.2), "retained rich text follows a new font policy without stale base metadata")
	var stable: String = rules.get_parsed_text()
	fixture.held = false; fixture.fail = true; fixture.sequence = 3
	check(not await session.refresh() and not session.synchronized and rules.get_parsed_text() == stable and hand.read_only and not session.card_zones.is_empty(), "failed inspection keeps the last face explicitly non-interactive")
	fixture.fail = false; fixture.stale = true
	check(not await session.refresh() and session.run.sequence == 2 and rules.get_parsed_text() == stable, "mismatched inspection revision cannot rewrite the face")
	fixture.stale = false
	for mismatch in [{"runId": "other-run"}, {"combatId": "other-combat"}, {"contentRevision": "other-content"}, {"combatStep": 999}]:
		fixture.version_overrides = mismatch
		check(not await session.refresh() and session.run.sequence == 2 and rules.get_parsed_text() == stable, "foreign inspection identity is rejected: %s" % mismatch.keys()[0])
	fixture.version_overrides = {}
	check(await session.refresh() and session.run.sequence == 3 and not hand.read_only, "reconnect publishes the complete new revision")
	fixture.held = true; fixture.sequence = 4; refresh(session)
	session.invalidate(); fixture.release.emit(); await settle()
	check(completions == [true, false] and session.run.is_empty() and session.card_evaluations.is_empty(), "late inspection after invalidation cannot leak a previous setting/run")
	hand.queue_free(); session.queue_free(); await settle()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
