class_name HandPointerCoordinator
extends RefCounted
## Pure face-aware hit resolution and auto-scroll helpers for CardHandView.


static func is_pickable(id: String, cards: Dictionary, slots: Dictionary, transit: Dictionary) -> bool:
	var card = cards.get(id)
	return is_instance_valid(card) and card.is_visible_in_tree() and not transit.has(id) and card.get_parent() == slots.get(id)


static func contains_card(card: Control, point: Vector2, padding := 0.0) -> bool:
	return Rect2(Vector2.ZERO, card.size).grow(padding).has_point(card.get_global_transform().affine_inverse() * point)


static func draw_rank(id: String, cards: Dictionary, slots: Dictionary) -> int:
	return int(slots[id].z_index) + int(cards[id].z_index)


static func is_above(a: String, b: String, cards: Dictionary, slots: Dictionary) -> bool:
	var rank_a := draw_rank(a, cards, slots)
	var rank_b := draw_rank(b, cards, slots)
	return rank_a > rank_b or (rank_a == rank_b and slots[a].get_index() > slots[b].get_index())


static func pick_global(
	point: Vector2,
	stable: bool,
	order: Array,
	cards: Dictionary,
	slots: Dictionary,
	transit: Dictionary,
	layout: Dictionary,
	reading_modal: bool,
	hit_surface: Control,
	reading_proxy: Control,
	hovered_id: String,
	hover_point: Vector2,
	hysteresis: float
) -> String:
	if layout.is_empty() or reading_modal: return ""
	if not Rect2(Vector2.ZERO, hit_surface.size).has_point(hit_surface.get_global_transform().affine_inverse() * point): return ""
	if is_instance_valid(reading_proxy) and reading_proxy.is_visible_in_tree():
		var proxy_id := str(reading_proxy.model.get("cardInstanceId", reading_proxy.model.get("labId", "")))
		if is_pickable(proxy_id, cards, slots, transit) and Rect2(Vector2.ZERO, reading_proxy.size).has_point(reading_proxy.get_global_transform().affine_inverse() * point):
			return proxy_id
	var best := ""
	for id in order:
		if not is_pickable(id, cards, slots, transit) or not contains_card(cards[id], point): continue
		if best.is_empty() or is_above(id, best, cards, slots): best = id
	if stable and is_pickable(hovered_id, cards, slots, transit) and (best.is_empty() or hovered_id == best or is_above(hovered_id, best, cards, slots)):
		if contains_card(cards[hovered_id], point, hysteresis): return hovered_id
		var index := order.find(hovered_id)
		if point.is_equal_approx(hover_point) and index >= 0 and index < layout.rotations.size():
			var card: Control = cards[hovered_id]
			var resting := Transform2D(float(layout.rotations[index]), Vector2.ZERO)
			resting.origin = card.pivot_offset - resting.basis_xform(card.pivot_offset)
			var transform: Transform2D = slots[hovered_id].get_global_transform() * resting
			if Rect2(Vector2.ZERO, card.size).grow(hysteresis).has_point(transform.affine_inverse() * point):
				return hovered_id
	return best


static func auto_scroll_direction(hit_surface: Control, global_point: Vector2, edge_band: float) -> float:
	var point: Vector2 = hit_surface.get_global_transform().affine_inverse() * global_point
	var band := clampf(edge_band, 1.0, maxf(1.0, hit_surface.size.x * .25))
	return clampf((point.x - (hit_surface.size.x - band)) / band, 0.0, 1.0) - clampf((band - point.x) / band, 0.0, 1.0)
