extends RefCounted
## Purpose-free transfer executor. Addresses and visibility come from published zones.
const Pose = preload("res://scripts/ui/card_motion_pose.gd")
static func address_key(address: Dictionary) -> String:
	return "%s::%s::%s" % [address.get("scopeId") if address.get("scopeId") != null else "$run", address.get("ownerId", ""), address.get("zoneId", "")]
static func resolve(steps: Array, snapshots: Array, anchor: Callable, policy, role: Callable = Callable()) -> Array:
	var facts: Array = preload("res://scripts/presentation/presentation_envelope.gd").visible_zone_steps(steps, snapshots)
	var transfers: Array = []
	for step in facts:
		var ids: Array = []
		for key in ["instanceIds", "createdInstanceIds", "destroyedInstanceIds"]:
			for id in step.get(key, []):
				if id not in ids: ids.append(id)
		var hidden := 0
		for key in ["instanceIds", "createdInstanceIds", "destroyedInstanceIds"]: hidden += int(step.get(key + "HiddenCount", 0))
		if ids.is_empty() and hidden > 0: ids.append("")
		for id in ids:
			var source := str(step.get("sourceAddress", "")); var target := str(step.get("targetAddress", ""))
			var start: Vector2 = anchor.call(source, str(id)); var end: Vector2 = anchor.call(target, str(id))
			var operation := "zone_transfer"
			if id in step.get("createdInstanceIds", []) or (id == "" and int(step.get("createdInstanceIdsHiddenCount", 0)) > 0): operation = "create"; start = end
			elif id in step.get("destroyedInstanceIds", []) or (id == "" and int(step.get("destroyedInstanceIdsHiddenCount", 0)) > 0): operation = "destroy"; end = start
			elif role.is_valid():
				if role.call(target) == "hand": operation = "draw"
				elif role.call(source) == "hand": operation = "discard"
			var burning: bool = not str(id).is_empty() and flow_visual(str(step.get("flowId", "")), policy).get("animation") == "burn"
			# A dissolve needs the source face, not a visible destination pile.
			if burning: end = start
			if not start.is_finite() or not end.is_finite() or policy.duration("exile_burn" if burning else operation, true) <= 0: continue
			if not burning and operation not in ["create", "destroy"] and start.is_equal_approx(end): continue
			transfers.append({"id": str(id), "source": source, "target": target, "start": start, "end": end, "operation": operation, "step": step, "hidden": hidden})
	return transfers

static func schedule(transfers: Array, policy, staged: Callable = Callable()) -> Array:
	var visible: Array = transfers.slice(0, policy.limit("max_transfers"))
	var serial: Dictionary = {}
	var result: Array = []
	for i in visible.size():
		var item: Dictionary = visible[i].duplicate(true)
		var duration: float = policy.duration(item.operation, true)
		if item.operation == "discard" and staged.is_valid() and staged.call(item.id): duration = policy.duration("staged_discard", true)
		var visual := flow_visual(str(item.get("step", {}).get("flowId", "")), policy)
		if visual.get("animation") == "burn" and not item.id.is_empty(): duration = policy.duration("exile_burn", true)
		item["visual"] = visual
		var key: String = item.id if not item.id.is_empty() else "hidden:%s" % i
		var prior: float = float(serial.get(key, 0))
		# Stagger consumes this wave's duration, never extends the active batch.
		var lag: float = minf(minf(i * policy.duration("stagger", true), policy.duration("batch_span", true)), duration * .25)
		var delay: float = maxf(prior, lag)
		duration -= maxf(0.0, delay - prior)
		serial[key] = delay + duration
		item["duration"] = duration; item["delay"] = delay
		item["following"] = visible.slice(i + 1).any(func(next): return not item.id.is_empty() and next.id == item.id)
		result.append(item)
	return result

static func flow_visual(flow_id: String, policy) -> Dictionary:
	return policy.as_dictionary(policy.cue("card_flows").get(flow_id, {}))

static func uses_burn(frames: Array, id: String, policy) -> bool:
	for frame in frames:
		var steps: Array = frame.get("cardZoneSteps", []).duplicate()
		for application in frame.get("applications", []): steps.append_array(application.get("cardZoneSteps", []))
		for step in steps:
			if id in step.get("instanceIds", []) and flow_visual(str(step.get("flowId", "")), policy).get("animation") == "burn": return true
	return false

static func execute(steps: Array, snapshots: Array, models: Dictionary, anchor: Callable, overlay: Control, context, handle, role: Callable = Callable(), registry = null, arrival: Callable = Callable()) -> void:
	var policy = context.motion if context.motion != null else context.capture_motion()
	var owner = registry if registry != null else preload("res://scripts/ui/card_transit_registry.gd").new(null, overlay, context)
	# Both the laboratory and combat reserve the same final slots at this boundary.
	if not steps.is_empty() and owner.hand != null and is_instance_valid(owner.hand) and role.is_valid():
		owner.hand.prepare_zone_beat(steps, role)
		var hand_ref: WeakRef = weakref(owner.hand)
		handle.cleanup(func():
			var hand = hand_ref.get_ref()
			if handle.result == "completed" and is_instance_valid(hand) and hand.is_inside_tree(): hand.settle_zone_beat())
	var transfers := resolve(steps, snapshots, anchor, policy, role)
	if transfers.is_empty(): handle.hit(); handle.finish(); return
	var visible := schedule(transfers, policy, owner.consume_staged)
	var remaining := {"count": visible.size()}
	# The same instance may cross several addresses in one published beat. Chain
	# only its own passages; unrelated cards travel together.
	for i in visible.size():
		var item: Dictionary = visible[i]
		if item.visual.get("animation") == "burn" and hand_source(owner, item.id):
			_execute_burn(item, owner, overlay, context, handle, remaining, arrival)
			continue
		var source_visible := false
		for zone in snapshots[0].get("zones", []):
			if address_key(zone) == item.source: source_visible = bool(zone.get("contentsVisible", false))
		if item.operation == "create": source_visible = true
		var ghost: Control = owner.acquire(item.id, models.get(item.id, {}), source_visible, item.start)
		var ghost_ref: WeakRef = weakref(ghost)
		if not source_visible:
			ghost.scale = Vector2.ONE * .35; ghost.hide()
		if item.operation == "create": ghost.scale = Vector2.ONE * .8; ghost.modulate.a = 0; ghost.hide()
		var duration: float = item.duration
		var delay: float = item.delay
		var following: bool = item.following
		if item.hidden > 1 or (i == visible.size() - 1 and transfers.size() > visible.size()):
			var count := Label.new(); count.text = "×%s" % (item.hidden if item.hidden > 1 else transfers.size() - visible.size() + 1)
			count.add_theme_font_size_override("font_size", 36); count.mouse_filter = Control.MOUSE_FILTER_IGNORE; ghost.add_child(count)
		var tween := ghost.create_tween(); handle.own(tween)
		var pose := {"center": item.start, "scale": Vector2.ONE * .35, "rotation": 0.0}
		tween.tween_callback(func():
			pose.merge(Pose.read(ghost), true)
			var target := _hand_pose(owner, item, role)
			if not target.is_empty():
				var source := Pose.read(ghost)
				pose.center = source.center; pose["global_scale"] = source.scale
				ghost.size = target.size
				Pose.apply(ghost, pose.center, target.rotation, source.scale)
			ghost.show()).set_delay(delay)
		tween.tween_method(func(raw: float):
			var progress: float = policy.progress("zone_transfer", raw)
			var target := _hand_pose(owner, item, role)
			if not target.is_empty():
				var center: Vector2 = Pose.arrival_center(pose.center, target.center, target.rotation, progress) if item.operation == "draw" else pose.center.lerp(target.center, progress)
				var scale: Vector2 = pose.get("global_scale", target.scale).lerp(target.scale, progress)
				scale *= preload("res://scripts/presentation/motion_curves.gd").settle(raw, float(policy.shape("zone_transfer").get("settle_scale", 0.0)))
				Pose.apply(ghost, center, target.rotation, scale)
				ghost.modulate.a = progress if item.operation == "create" else 1.0
				return
			var destination: Vector2 = anchor.call(item.target, item.id)
			if not destination.is_finite(): destination = item.end
			var center: Vector2 = pose.center.lerp(destination, progress)
			center.y -= sin(progress * PI) * minf(42, pose.center.distance_to(destination) * .12)
			var target_scale := 1.0 if item.operation in ["draw", "create"] else .3
			Pose.apply(ghost, center, lerp_angle(float(pose.rotation), 0.0, progress), (pose.scale as Vector2).lerp(Vector2.ONE * target_scale, progress))
			ghost.modulate.a = progress if item.operation == "create" else (1.0 if item.operation == "draw" or following else 1.0 - clampf((progress - .8) / .2, 0, 1)), 0.0, 1.0, duration)
		tween.tween_callback(func():
			if arrival.is_valid(): arrival.call(item)
			if not following: owner.release(item.id, ghost, role.is_valid() and role.call(item.target) == "hand" and item.operation != "destroy")
			remaining.count -= 1
			if remaining.count == 0: handle.hit(); handle.finish())
		handle.cleanup(func():
			if handle.result != "completed": owner.release(item.id, ghost_ref.get_ref()))

static func hand_source(owner, id: String) -> bool:
	return owner.hand != null and is_instance_valid(owner.hand) and is_instance_valid(owner.hand.card_for(id))

static func _hand_pose(owner, item: Dictionary, role: Callable) -> Dictionary:
	if item.operation in ["draw", "create"] and role.is_valid() and role.call(item.target) == "hand" and hand_source(owner, item.id):
		return owner.hand.destination_pose(item.id)
	return {}

static func _execute_burn(item: Dictionary, owner, overlay: Control, context, handle, remaining: Dictionary, arrival: Callable) -> void:
	var child = preload("res://scripts/presentation/presentation_handle.gd").new()
	handle.claimed = true
	handle.cleanup(func(): if not child.done: child.finish("cancelled"))
	child.completed.connect(func(_result):
		if handle.done: return
		if arrival.is_valid(): arrival.call(item)
		remaining.count -= 1
		if remaining.count == 0: handle.hit(); handle.finish(), CONNECT_ONE_SHOT)
	var start := func():
		if handle.done: return
		child.begin()
		preload("res://scripts/ui/card_burn_motion.gd").execute(item.id, str(item.visual.get("direction", "center")), owner.hand, owner, context, overlay, child, "normal", item.duration)
	if item.delay > 0:
		var delay := overlay.create_tween(); handle.own(delay)
		delay.tween_callback(start).set_delay(item.delay)
	else: start.call()
