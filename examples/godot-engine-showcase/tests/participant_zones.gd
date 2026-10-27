extends SceneTree

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	var translator = load("res://scripts/ui/visual_context.gd").new(null, {}, load("res://data/themes/ivory_run.tres")).translator
	var presenter = load("res://scripts/presentation/card_zone_presenter.gd").new({"zones": [
		{"zoneId": "pocket", "ownerId": "hero", "allowsCardPlay": true, "playerCanPlayFrom": true, "contentsVisible": true, "cards": [{"cardInstanceId": "mine"}]},
		{"zoneId": "pocket", "ownerId": "dealer", "allowsCardPlay": true, "playerCanPlayFrom": false, "contentsVisible": true, "cards": [{"cardInstanceId": "theirs"}]}
	]}, translator)
	var ok: bool = presenter.playable_cards().map(func(card): return card.cardInstanceId) == ["mine"] and presenter.auxiliary_zones().map(func(zone): return zone.ownerId) == ["dealer"]
	print("[PASS] participant ownership projection" if ok else "[FAIL] participant ownership projection")
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if ok else 1)
