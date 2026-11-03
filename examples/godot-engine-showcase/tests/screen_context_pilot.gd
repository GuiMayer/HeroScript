extends SceneTree
## Etapa 2: ScreenContext & Narrow Ports Verification with Pilot Screen (codex.tscn).
## Verifies that the pilot screen opens in isolation with fake services and in runtime with real services,
## preserving navigation, focus, and async cancellation without creating a second session service.

const ScreenContext = preload("res://scripts/ui/context/screen_context.gd")
const SessionPort = preload("res://scripts/ui/context/session_port.gd")

var failures := 0

class DelayedFakeSession extends RefCounted:
	var tree: SceneTree
	var calls: Array[String] = []
	func _init(p_tree: SceneTree) -> void:
		tree = p_tree
	func content(kind: String, _limit := 200) -> Dictionary:
		calls.append(kind)
		if kind == "entities":
			await tree.process_frame
			await tree.process_frame
			return {"ok": true, "data": {"items": [{"definitionId": "delayed_entity"}]}}
		return {"ok": true, "data": {"items": [{"definitionId": "immediate_card"}]}}

class RuntimeGateway extends RefCounted:
	func content(kind: String, _revision := "", _setting_id := "", _limit := 200) -> Dictionary:
		return {"ok": true, "data": {"items": [{"definitionId": "runtime_" + kind, "kind": kind}]}}

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok:
		failures += 1

func settle(frames := 4) -> void:
	for _i in frames:
		await process_frame

func _exercise_isolated() -> void:
	var http_before: int = root.get_node("GameServices").diagnostics().size()
	var ctx = ScreenContext.isolated()
	ctx.session.set_fake_content("cards", {
		"ok": true,
		"data": {"items": [{"definitionId": "isolated_strike", "damage": 8}]}
	})
	ctx.session.set_fake_content("relics", {
		"ok": true,
		"data": {"items": [{"definitionId": "isolated_ember", "rarity": "rare"}]}
	})

	check(ctx.session.target == null, "isolated ScreenContext does not bind to global GameSession singleton")

	var codex = load("res://scenes/ui/screens/codex.tscn").instantiate()
	codex.setup(ctx)
	root.add_child(codex)
	await settle(3)

	check(codex.list.item_count == 1 and codex.list.get_item_text(0) == "isolated_strike",
		"pilot screen renders fake session items in complete isolation")
	check(codex.detail.text.contains('"damage": 8'),
		"pilot screen detail view formats selected item from fake session")

	var tabs = codex.get_node("TabsScroll/Tabs")
	var relics_tab: Button = tabs.get_child(4)
	relics_tab.pressed.emit()
	await settle(2)

	check(codex.list.item_count == 1 and codex.list.get_item_text(0) == "isolated_ember",
		"pilot screen switches content category through injected SessionPort")
	check(ctx.feedback.audio_events.has("ui"),
		"pilot screen emits UI audio intent through injected FeedbackPort")

	var menu_btn: Button = codex.get_node("Header/Menu")
	menu_btn.pressed.emit()
	check(ctx.navigation.history.size() == 1 and ctx.navigation.history[0].get("route") == "menu",
		"pilot screen routes back to menu through injected NavigationPort")

	codex.queue_free()
	await settle(2)

	# Verify request_epoch cancellation on dispose during in-flight async request
	var delayed_session := DelayedFakeSession.new(self)
	var cancel_ctx = ScreenContext.isolated({"session": SessionPort.new(delayed_session)})
	var cancel_codex = load("res://scenes/ui/screens/codex.tscn").instantiate()
	cancel_codex.setup(cancel_ctx)
	root.add_child(cancel_codex)
	await settle(2)
	check(cancel_codex.list.get_item_text(0) == "immediate_card", "initial immediate load succeeds before cancellation test")

	var entities_tab: Button = cancel_codex.get_node("TabsScroll/Tabs").get_child(1)
	entities_tab.pressed.emit()
	cancel_codex.dispose()
	await settle(4)
	check(cancel_codex.list.get_item_text(0) == "immediate_card",
		"disposed pilot screen ignores stale in-flight async response via request_epoch")
	cancel_codex.queue_free()
	await settle(2)

	check(root.get_node("GameServices").diagnostics().size() == http_before,
		"isolated pilot screen issues zero HTTP requests")

func _exercise_runtime() -> void:
	var session = root.get_node("GameSession")
	session.configure(RuntimeGateway.new())
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle(3)

	router.show_codex()
	await settle(4)

	var screen = router.host.get_child(0)
	check(screen.scene_file_path == "res://scenes/ui/screens/codex.tscn", "router mounts canonical codex scene")
	check(screen.screen_context != null and screen.screen_context.session.target == session,
		"runtime ScreenContext wraps the existing GameSession without creating a second session service")
	check(screen.list.item_count == 1 and screen.list.get_item_text(0) == "runtime_cards",
		"runtime pilot screen loads content from authoritative GameSession")

	var menu_btn: Button = screen.get_node("Header/Menu")
	check(menu_btn.focus_mode == Control.FOCUS_ALL, "focus navigation remains active on runtime pilot screen")
	menu_btn.pressed.emit()
	await settle(3)
	check(router.current_screen == "menu", "runtime pilot screen navigates back to main menu")

	router.queue_free()
	await settle(2)

func _run() -> void:
	await _exercise_isolated()
	await _exercise_runtime()
	root.get_node("GameAudio").shutdown()
	await create_timer(0.05).timeout
	quit(0 if failures == 0 else 1)
