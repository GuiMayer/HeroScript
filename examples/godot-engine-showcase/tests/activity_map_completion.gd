extends SceneTree
var failures := 0
class Session extends RefCounted:
	signal release
	var busy := false
	var synchronized := true
	var accepted := true
	var delayed := false
	var run := {"runId":"qa","branchId":"main","currentNodeId":"shop","map":{"resolvedNodeIds":[]}}
	var choices: Array = []
	var calls: Array = []
	func activity_choices() -> Array: return choices.duplicate(true)
	func submit_activity(choice: Dictionary) -> bool:
		busy = true; calls.append(choice.duplicate(true))
		if delayed: await release
		busy = false
		if accepted:
			# Synthetic authoritative response, not a client-side completion rule.
			run = run.duplicate(true); run.map.resolvedNodeIds.append(choice.payload.currentNodeId)
		return accepted
class Owner extends RefCounted:
	var returns := 0
	func show_map() -> void: returns += 1
	func open_game() -> void: returns += 1
	func gameplay_route() -> String: return "map"
	func show_toast(_message: String) -> void: pass
class RewardSession extends Session:
	func submit_activity(choice: Dictionary) -> bool:
		if choice.type == "PICK_CARD_REWARD":
			calls.append(choice.duplicate(true))
			choices = [{"type":"RESOLVE_NODE","payload":{"currentNodeId":run.currentNodeId}}]
			return true
		return await super.submit_activity(choice)
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func screen_for(session, owner):
	var screen = load("res://scripts/ui/activity_screen.gd").new()
	screen.controller = load("res://scripts/ui/controllers/activity_controller.gd").new(session)
	screen.router = owner; root.add_child(screen); return screen
func _run() -> void:
	var choice := {"type":"RESOLVE_NODE","payload":{"currentNodeId":"shop"},"expectedSequence":7,"expectedStep":4}
	var session := Session.new(); var owner := Owner.new()
	session.choices = [choice]
	var original: Dictionary = session.run
	var screen = screen_for(session,owner)
	screen._on_intent("map",[])
	check(owner.returns == 1 and session.calls == [choice] and session.run.map.resolvedNodeIds == ["shop"], "return submits exact published completion once and displays authoritative completed node")
	check(original.map.resolvedNodeIds.is_empty() and session.choices == [choice], "completion never mutates the previous snapshot or published option")
	screen.queue_free(); await process_frame

	session = Session.new(); owner = Owner.new(); session.choices = [{"type":"PICK_CARD_REWARD","payload":{"cardIds":["required"]}}]
	screen = screen_for(session,owner); screen._on_intent("map",[])
	check(owner.returns == 1 and session.calls.is_empty() and session.run.map.resolvedNodeIds.is_empty(), "required reward remains pending; map inspection cannot invent completion or pick a card")
	screen.queue_free(); await process_frame

	session = Session.new(); owner = Owner.new(); session.choices = [choice.duplicate(true)]
	session.choices[0].payload.currentNodeId = "another-node"
	screen = screen_for(session,owner); screen._on_intent("map",[])
	check(session.calls.is_empty() and owner.returns == 1, "completion for a different node is never submitted")
	screen.queue_free(); await process_frame

	session = Session.new(); owner = Owner.new(); session.choices = [choice]; session.accepted = false
	screen = screen_for(session,owner); screen._on_intent("map",[])
	check(owner.returns == 0 and session.calls.size() == 1 and session.run.map.resolvedNodeIds.is_empty(), "rejected completion leaves the activity open and never marks it completed locally")
	screen.queue_free(); await process_frame

	session = Session.new(); owner = Owner.new(); session.choices = [choice]; session.synchronized = false
	screen = screen_for(session,owner); screen._on_intent("map",[])
	check(owner.returns == 0 and session.calls.is_empty(), "unsynchronized session cannot complete a visit")
	screen.queue_free(); await process_frame

	session = Session.new(); owner = Owner.new(); session.choices = [choice,choice.duplicate(true)]
	screen = screen_for(session,owner); screen._on_intent("map",[])
	check(owner.returns == 0 and session.calls.is_empty(), "ambiguous completion options are not arbitrarily chosen")
	screen.queue_free(); await process_frame

	session = Session.new(); owner = Owner.new(); session.choices = [choice]; session.delayed = true
	screen = screen_for(session,owner); screen._on_intent("map",[]); screen._on_intent("map",[])
	check(owner.returns == 0 and session.calls.size() == 1, "double return while submitting cannot duplicate completion")
	session.release.emit(); await process_frame
	check(owner.returns == 1, "map opens only after completion is accepted")
	screen.queue_free(); await process_frame

	session = Session.new(); owner = Owner.new(); session.choices = [choice]; session.delayed = true
	screen = screen_for(session,owner); screen._on_intent("map",[]); root.remove_child(screen)
	session.release.emit(); await process_frame
	check(owner.returns == 0, "late completion after leaving activity does not navigate a disposed screen")
	screen.free()

	var reward := RewardSession.new(); owner = Owner.new()
	var pick := {"type":"PICK_CARD_REWARD","payload":{"cardIds":["chosen"]}}
	reward.choices = [pick]; screen = screen_for(reward,owner)
	screen.view = load("res://scripts/ui/journey_view.gd").new(); screen.add_child(screen.view)
	screen.controller.bind(reward.run,reward.choices)
	await screen._execute(pick)
	check(reward.calls.map(func(c): return c.type) == ["PICK_CARD_REWARD","RESOLVE_NODE"] and owner.returns == 1, "reward-to-map transition completes the node before displaying the map")
	check(reward.run.map.resolvedNodeIds == ["shop"], "completed badge is sourced from the accepted authoritative response")
	screen.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
