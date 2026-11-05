extends SceneTree
var failures := 0
func _init() -> void: run.call_deferred()
func check(value: bool, message: String) -> void:
	print("[PASS] " if value else "[FAIL] ", message)
	if not value: failures += 1
func settle() -> void:
	for frame in 8: await process_frame
func reload_deck(screen) -> void:
	screen.controller._deck_version = []
	await screen.controller.ensure_deck()
	await settle()
func run() -> void:
	var session = load("res://tests/fixtures/optional_deck_session.gd").new()
	var screen = load("res://scenes/ui/screens/unified_loadout.tscn").instantiate()
	screen.session = session; root.add_child(screen)
	await settle()
	screen._select_tab("deck"); await settle()
	var deck = screen.deck_view
	check(deck.visible and deck._card_buttons.size() == 1, "deck opens and shows cards when the API returns profile null")
	check(not deck._sub_buttons.compartments.visible and not deck._sub_buttons.composition.visible, "disabled optional features do not appear as editable tabs")
	check(session.last_compartments.has("profile") and session.last_compartments.profile == null and screen.controller.compartments.source.profile == null, "presentation preserves the original nullable API snapshot")
	check(session.commits.is_empty() and screen.controller.can_leave(), "opening disabled features neither writes nor leaves the page blocked")
	var original_card = deck._card_cache.get("card-one")
	screen._select_tab("overview"); screen._select_tab("deck"); await settle()
	check(deck._card_buttons.size() == 1 and deck._card_cache.get("card-one") == original_card, "returning to the deck preserves the card component")
	session.compartments_enabled = true
	await reload_deck(screen)
	deck.subtab = "compartments"; deck._render(); await settle()
	check(deck._sub_buttons.compartments.visible and deck._model.profile.compartments.size() == 1 and deck._card_buttons.size() == 1, "configured compartments retain their data and working card view")
	session.composition_enabled = true
	await reload_deck(screen)
	deck.subtab = "composition"; deck._render(); await settle()
	check(deck.get_node("Body/Composition").visible and deck.get_node("Body/Composition")._cards.size() == 2, "configured composition still renders its published active and reserve cards")
	session.compartments_enabled = false; session.composition_enabled = false
	await reload_deck(screen)
	check(deck.subtab == "cards" and deck._card_buttons.size() == 1 and deck._card_cache.get("card-one") == original_card, "returning to a disabled nullable profile falls back to cards without replacing them")
	check(session.commits.is_empty() and not screen.controller.busy and not screen.controller.read_error, "optional absence is not an equipment error or a save request")
	# Also cover a caller that omits the optional profile entirely.
	var model: Dictionary = deck._model.duplicate(true); model.erase("profile")
	var before := JSON.stringify(model)
	deck.present(model)
	check(deck._card_buttons.size() == 1 and JSON.stringify(model) == before, "omitted profile remains safe without modifying its input")
	screen.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown()
	quit(1 if failures else 0)
