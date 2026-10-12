extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var audio = root.get_node("GameAudio")
	var presenter = load("res://scripts/presentation/combat_audio_presenter.gd")
	var catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(audio.MANIFEST))
	check(audio._voices.size() == 8, "bounded pool preallocated once")
	check(audio._music.stream == null, "old generated background tone removed")
	check(audio._streams.size() == 23, "selected licensed samples loaded once")
	for event_id in catalog.events:
		var event: Dictionary = catalog.events[event_id]
		check(not event.clips.is_empty() and event.gain_db <= -10 and event.cooldown_ms >= 60, "event has restrained mix and rate limit: " + event_id)
		for path in event.clips:
			var stream = audio._streams.get(path)
			check(stream is AudioStreamOggVorbis and stream.get_length() > 0 and stream.get_length() < 3 and not stream.loop, "imported short non-looping sample: " + path)
	check(presenter.cues({"applications": []}).is_empty(), "empty lifecycle frames are silent")
	check(presenter.cues({"applications": [{"effectType": "DAMAGE", "resourceId": "mana", "previousValue": 5, "currentValue": 3}]} ) == ["impact"], "damage cue does not assume health")
	check(presenter.cues({"applications": [{"effectType": "DAMAGE", "previousValue": 5, "currentValue": 5}]}).is_empty(), "zero applied change is silent")
	check(presenter.cues({"applications": [{"effectType": "MODIFY_RESOURCE", "resourceId": "energy", "previousValue": 3, "currentValue": 2}]}).is_empty(), "paying a cost is not falsely presented as a hit")
	check(presenter.cues({"applications": [{"effectType": "MODIFY_RESOURCE", "resourceField": "Maximum", "resourceId": "block", "previousValue": 1, "currentValue": 2}]}, {"block": "guard"}).is_empty(), "capacity changes are not current defense gain")
	check(presenter.cues({"applications": [{"effectType": "MODIFY_RESOURCE", "resourceId": "focus", "previousValue": 1, "currentValue": 2}]}, {"focus": "guard"}) == ["guard"], "resource feedback configured in presentation")
	check(presenter.cues({"applications": [{"effectType": "HEAL", "previousValue": 1, "currentValue": 2}, {"effectType": "HEAL", "previousValue": 2, "currentValue": 3}]}) == ["heal"], "one cue per type per frame")
	check(not audio.play_event("unknown"), "missing events fail quietly")
	var prefs = root.get_node("Preferences")
	prefs.master_volume = 1; prefs.sfx_volume = 1; prefs.apply_audio(); audio._apply()
	seed(1234); var expected := randi(); seed(1234)
	check(audio.play_event("card_play"), "card sound accepted")
	check(randi() == expected, "presentation variants do not advance global RNG")
	check(not audio.play_event("card_play"), "burst repeat suppressed")
	check(audio.play_event("ui"), "UI does not interrupt playing card sound")
	check(audio._voices.filter(func(voice): return voice.playing).size() == 2, "distinct cues overlap in independent players")
	check(not audio._voices[0].stream.loop, "effects cannot loop indefinitely")
	await create_timer(.35).timeout
	check(audio.play_event("card_play") and audio._variant.card_play == 2, "next sample selected without immediate repetition")
	prefs.sfx_volume = 0; audio._apply()
	check(AudioServer.is_bus_mute(AudioServer.get_bus_index("SFX")) and not audio.play_event("reward"), "SFX mute prevents new voices")
	prefs.sfx_volume = .7; prefs.music_volume = .2; audio._apply()
	check(is_equal_approx(db_to_linear(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("SFX"))), .7) and is_equal_approx(db_to_linear(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("Music"))), .2), "music and effect gains remain independent")
	var count: int = audio._voices.size()
	check(audio._events.card_hover.clips.size() == 3 and audio._events.card_hover.cooldown_ms == 65, "hover has soft variants and rapid-sweep rate limit")
	check(audio.play_event("card_hover") and not audio.play_event("card_hover"), "hover accepts entry but suppresses bursts")
	var context = load("res://scripts/ui/visual_context.gd").new()
	var zone := {"zoneId": "hand", "ownerId": "hero", "scopeId": "combat", "contentsVisible": true, "orderVisible": true, "count": 2, "cards": [{"cardInstanceId": "a", "definitionId": "basic_attack"}, {"cardInstanceId": "b", "definitionId": "defend"}]}
	var card_presenter = load("res://scripts/presentation/combat_presenter.gd").new({"runId": "r", "sequence": 1}, {}, [], context.translator, {}, {"zones": [zone]})
	var model: Dictionary = load("res://scripts/presentation/card_collection_presenter.gd").build(zone, card_presenter, context.translator)
	var hand = load("res://scripts/ui/card_hand_view.gd").new(context)
	root.add_child(hand); hand.size = Vector2(1000, 500); hand.sync_models(model.cards, "hover-test")
	for i in 8: await process_frame
	var hovered: Array = []
	hand.card_hovered.connect(func(id): hovered.append(id))
	hand.interaction.hovered = ""
	var motion := InputEventMouseMotion.new()
	motion.position = hand._layout.hitRects[0].get_center() - Vector2(hand.scroll.scroll_horizontal, 0)
	hand.pointer_input(motion); hand.pointer_input(motion)
	check(hovered == ["a"], "stationary motion inside same card does not retrigger")
	motion.position = hand._layout.hitRects[1].get_center() - Vector2(hand.scroll.scroll_horizontal, 0)
	hand.pointer_input(motion)
	check(hovered == ["a", "b"] and hand.interaction.selected.is_empty(), "crossing cards emits new hover without selecting")
	hand._focus("a")
	check(hovered == ["a", "b"], "programmatic focus is not pointer hover")
	hand.reading_modal = true; hand.interaction.hovered = ""
	hand.pointer_input(motion)
	check(hovered == ["a", "b"], "reading modal suppresses hover feedback")
	hand.reading_modal = false; hand.interaction.hovered = ""; hand.interaction.dragging = true
	hand.pointer_input(motion)
	check(hovered == ["a", "b"], "dragging suppresses hover chatter")
	hand.queue_free(); await process_frame
	for i in 100: audio.play_event("impact")
	check(audio._voices.size() == count, "burst never allocates extra players")
	audio.shutdown()
	check(audio._streams.is_empty() and audio._voices.all(func(voice): return not voice.playing and voice.stream == null) and not prefs.changed.is_connected(audio._apply), "shutdown releases playback and preference subscription")
	check(not audio.play_event("ui"), "shutdown prevents accidental restart")
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
