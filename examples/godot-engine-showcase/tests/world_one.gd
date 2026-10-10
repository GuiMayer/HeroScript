extends SceneTree
## Offline presentation contract for the published act; no rules, runs or preferences changed.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func _run() -> void:
	var translator = root.get_node("I18n")
	var original: String = translator.locale
	var appearance: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/world_one_presentation.json"))
	check(appearance.cards.size() == 30, "thirty cards share the normal presentation catalog")
	for locale in ["en", "pt_BR"]:
		translator.set_locale(locale)
		for id in appearance.cards:
			check(not str(translator.content_names.get(id, {}).get(locale, "")).is_empty(), "%s has %s name" % [id, locale])
		var published := {"currentNodeId": "modded_forge", "map": {"nodes": [{"nodeId": "modded_forge", "activity": {"type": "CardUpgrade"}, "metadata": {"displayName": "Modded Forge", "description": "One operation", "risk": "High"}, "nextNodeIds": ["boss"]}, {"nodeId": "boss", "nextNodeIds": []}]}}
		var presenter = preload("res://scripts/presentation/activity_presenter.gd").new(published, [], {}, translator)
		check(presenter.node_name("modded_forge") == "Modded Forge", "unknown settings use authored names, not hardcoded campaign labels")
		check(presenter.route()[0].risk == "High" and presenter.route()[0].description == "One operation", "route exposes authored risk and description")
		check(presenter.route()[1].travel.is_empty(), "displayed edges never create navigation authority")
		check(translator.content_name("s12_rest.description").contains("25"), "rest explains its fixed recovery and opportunity cost")
	translator.set_locale(original)
	root.get_node("GameAudio").shutdown()
	print("WORLD_ONE_PRESENTATION: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
