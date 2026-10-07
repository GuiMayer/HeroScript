extends SceneTree
## Offline presentation contracts: canonical procs, transformation payloads and locales.
var failures := 0

func _init() -> void: call_deferred("_run")

func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1

func _run() -> void:
	var playback = preload("res://scripts/presentation/playback.gd")
	var receipt := [{"frameId": "immutable", "effectSteps": [{"identity": {"procId": "one"}, "condensation": {}}],
		"applications": [{"identity": {"procId": "one"}, "resourceId": "mana"}, {"identity": {"procId": "one"}, "resourceId": "health"}]}]
	var original := JSON.stringify(receipt)
	var frames: Array = playback.presentation_frames(receipt)
	check(frames.size() == 1 and frames[0].applications.size() == 2 and frames[0].condensed, "one animation for one condensed proc, all outcomes retained")
	frames[0].applications.clear()
	check(JSON.stringify(receipt) == original, "presentation never mutates engine frames")
	var choices: Array = preload("res://scripts/application/activity_choices.gd").build({}, [{"type": "REPLACE_CARD_TRANSFORMATION", "validPayload": {"options": [{"cardInstanceId": "card", "upgradeId": "core_frost", "targetTransformationId": 9, "costs": [{"resourceId": "gold", "amount": 10}]}]}}])
	check(choices.size() == 1 and choices[0].payload.transformationId == 9 and choices[0].costs[0].amount == 10, "replace preserves engine-issued identity and costs")
	var i18n = root.get_node("I18n")
	var prior := str(i18n.locale)
	for locale in ["en", "pt_BR"]:
		i18n.set_locale(locale)
		check(not i18n.content_name("core_charge").is_empty(), "charge has localized identity: " + locale)
		var catalog = preload("res://scripts/presentation/art_catalog.gd")
		check(catalog.resolve("cards", "core_release").key == "core_release", "stable replaceable card art slot")
		check(catalog.resolve("effects", "core_ember_damage").key == "core_scorch_effect", "stable replaceable effect art slot")
		check(catalog.resolve("behaviors", "core_cascade").key == "core_cascade_behavior", "stable replaceable behavior art slot")
	i18n.set_locale(prior)
	root.get_node("GameAudio").shutdown()
	await create_timer(.05).timeout
	print("SHOWCASE_VOLATILE_CORE failures=", failures)
	quit(0 if failures == 0 else 1)
