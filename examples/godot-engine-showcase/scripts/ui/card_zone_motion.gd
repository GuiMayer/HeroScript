extends RefCounted
## Purpose-free transfer executor. Addresses and visibility come from published zones.
static func address_key(address: Dictionary) -> String:
	return "%s::%s::%s" % [address.get("scopeId") if address.get("scopeId") != null else "$run", address.get("ownerId", ""), address.get("zoneId", "")]
static func execute(steps: Array, snapshots: Array, models: Dictionary, anchor: Callable, overlay: Control, context, handle) -> void:
	var facts: Array = preload("res://scripts/presentation/presentation_envelope.gd").visible_zone_steps(steps, snapshots)
	var transfers: Array = []
	var policy = context.motion if context.motion != null else context.capture_motion()
	for step in facts:
		var ids: Array = []
		for key in ["instanceIds", "createdInstanceIds", "destroyedInstanceIds"]:
			for id in step.get(key, []):
				if id not in ids: ids.append(id)
		for id in ids: transfers.append({"id": str(id), "step": step})
		var hidden: int = int(step.get("instanceIdsHiddenCount", 0))
		if ids.is_empty() and hidden > 0: transfers.append({"id": "", "step": step})
	if transfers.is_empty() or policy.duration("zone_transfer", true) <= 0:
		handle.hit(); handle.finish(); return
	var remaining := {"count": mini(transfers.size(), policy.limit("max_transfers"))}
	for item in transfers.slice(0, policy.limit("max_transfers")):
		var ghost: Control
		var source_visible := false
		for zone in snapshots[0].get("zones", []):
			if address_key(zone) == str(item.step.get("sourceAddress", "")): source_visible = bool(zone.get("contentsVisible", false))
		if source_visible and models.has(item.id):
			ghost = CardView.new(context); ghost.animate_entry = false; ghost.hand_managed = true; ghost.configure(models[item.id])
			ghost.size = context.card_style.effective_size(context.text_scale)
		else:
			ghost = Panel.new(); ghost.size = Vector2(220, 300)
			ghost.add_theme_stylebox_override("panel", context.styles.box(context.styles.PANEL, 6, context.styles.INK, 2))
		ghost.name = "ZoneTransfer"; ghost.mouse_filter = Control.MOUSE_FILTER_IGNORE; ghost.process_mode = Node.PROCESS_MODE_PAUSABLE
		overlay.add_child(ghost); ghost.scale = Vector2.ONE * .3
		ghost.global_position = anchor.call(str(item.step.get("sourceAddress", "")), item.id) - ghost.size * .15
		var destination: Vector2 = anchor.call(str(item.step.get("targetAddress", "")), item.id) - ghost.size * .15
		var tween := ghost.create_tween().set_parallel(); handle.own(tween)
		handle.cleanup(func(): if is_instance_valid(ghost): ghost.queue_free())
		tween.tween_property(ghost, "global_position", destination, policy.duration("zone_transfer", true)).set_trans(policy.curve("zone_transfer")).set_ease(policy.easing("zone_transfer"))
		tween.tween_property(ghost, "modulate:a", 0.0, policy.duration("zone_transfer", true))
		tween.chain().tween_callback(func():
			remaining.count -= 1
			if remaining.count == 0: handle.hit(); handle.finish())
