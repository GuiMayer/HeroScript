extends RefCounted
## Pure, stable geometry. Never derives travel permissions from edges.
var _key := ""
var _result: Dictionary = {}
var calculation_count := 0

func calculate(nodes: Array, profile: Resource) -> Dictionary:
	var graph: Dictionary = {}
	var diagnostics: Array[String] = []
	for node in nodes:
		var id := str(node.get("id", ""))
		if id.is_empty():
			diagnostics.append("Visible map node has no ID.")
			continue
		if graph.has(id):
			diagnostics.append("Duplicate visible map ID: " + id)
			continue
		var edges: Array = node.get("next", []).map(func(value): return str(value))
		edges.sort()
		graph[id] = edges
	var ids: Array = graph.keys(); ids.sort()
	var topology: Array = ids.map(func(id): return [id, graph[id]])
	diagnostics.sort()
	var key := JSON.stringify([topology, profile.presentation_key(), diagnostics])
	if key == _key: return _result.duplicate(true)
	calculation_count += 1
	var components: Array = _strong_components(graph, ids)
	var owner: Dictionary = {}
	var condensed: Dictionary = {}
	var incoming: Dictionary = {}
	var component_layers: Dictionary = {}
	var cyclic := false
	for component in components:
		var key_id: String = component[0]
		condensed[key_id] = []; incoming[key_id] = 0; component_layers[key_id] = 0
		for id in component: owner[id] = key_id
		if component.size() > 1: cyclic = true
	for id in ids:
		for target in graph[id]:
			if not owner.has(target): continue
			if id == target: cyclic = true
			if owner[id] != owner[target] and owner[target] not in condensed[owner[id]]:
				condensed[owner[id]].append(owner[target]); incoming[owner[target]] += 1
	var queue: Array = condensed.keys().filter(func(id): return incoming[id] == 0)
	while not queue.is_empty():
		queue.sort()
		var id: String = queue.pop_front()
		for target in condensed[id]:
			component_layers[target] = maxi(component_layers[target], component_layers[id] + 1)
			incoming[target] -= 1
			if incoming[target] == 0: queue.append(target)
	var layers: Dictionary = {}
	for id in ids: layers[id] = component_layers[owner[id]]
	var rows: Dictionary = {}
	for id in ids:
		var layer: int = layers[id]
		if not rows.has(layer): rows[layer] = []
		rows[layer].append(id)
	var rects: Dictionary = {}
	var markers: Dictionary = {}
	var bounds := Vector2.ZERO
	# Bounded barycentric sweeps. Stable ID tie-break makes input order irrelevant.
	var row_ids: Array = rows.keys(); row_ids.sort()
	for sweep in 4:
		var positions: Dictionary = {}
		for layer in row_ids:
			for lane in rows[layer].size(): positions[rows[layer][lane]] = float(lane) - (rows[layer].size() - 1) * .5
		for layer in row_ids:
			var scores: Dictionary = {}
			for id in rows[layer]:
				var adjacent: Array = []
				for other in ids:
					if (sweep % 2 == 0 and id in graph[other] and layers[other] < layer) or (sweep % 2 == 1 and other in graph[id] and layers[other] > layer): adjacent.append(float(positions[other]))
				var total := 0.0
				for value in adjacent: total += value
				scores[id] = total / adjacent.size() if not adjacent.is_empty() else float(positions[id])
			rows[layer].sort_custom(func(a, b): return str(a) < str(b) if is_equal_approx(scores[a], scores[b]) else scores[a] < scores[b])
	var widest := 1
	for layer in rows: widest = maxi(widest, rows[layer].size())
	for layer in rows:
		var row: Array = rows[layer]
		for lane in row.size():
			var id: String = row[lane]
			var position: Vector2 = profile.padding + Vector2((lane + (widest - row.size()) * .5) * (profile.node_size.x + profile.lane_gap), layer * (profile.node_size.y + profile.layer_gap))
			var rect := Rect2(position, profile.node_size)
			rects[id] = rect
			markers[id] = rect.get_center()
			bounds.x = maxf(bounds.x, rect.end.x + profile.padding.x)
			bounds.y = maxf(bounds.y, rect.end.y + profile.padding.y)
	var edges: Array = []
	for id in ids:
		for target in graph[id]:
			if not markers.has(target): continue
			var origin: Vector2 = rects[id].position + Vector2(profile.node_size.x * .5, profile.node_size.y)
			var destination: Vector2 = rects[target].position + Vector2(profile.node_size.x * .5, 0)
			var returning: bool = layers[target] <= layers[id]
			var path: PackedVector2Array
			if id == target:
				var right: float = rects[id].end.x + 8
				path = PackedVector2Array([origin, origin + Vector2(0,12), Vector2(right,origin.y+12), Vector2(right,destination.y-12), destination-Vector2(0,12), destination])
			elif returning:
				var outside: float = profile.padding.x * .5
				path = PackedVector2Array([origin, origin+Vector2(0,12), Vector2(outside,origin.y+12), Vector2(outside,destination.y-12), destination-Vector2(0,12), destination])
			else:
				path = PackedVector2Array([origin, origin + Vector2(0, profile.layer_gap * .5), destination - Vector2(0, profile.layer_gap * .5), destination])
			edges.append({"from": id, "to": target, "return": returning, "path": path})
	_key = key
	_result = {"rects": rects, "markers": markers, "edges": edges, "bounds": bounds, "cyclic": cyclic, "diagnostics": diagnostics}
	return _result.duplicate(true)


func _strong_components(graph: Dictionary, ids: Array) -> Array:
	# Iterative Kosaraju: stable IDs, no recursion depth dependence, O(V+E).
	var seen: Dictionary = {}
	var order: Array = []
	var reverse: Dictionary = {}
	for id in ids: reverse[id] = []
	for id in ids:
		for target in graph[id]:
			if reverse.has(target): reverse[target].append(id)
	for root_id in ids:
		if seen.has(root_id): continue
		seen[root_id] = true
		var stack: Array = [[root_id, 0]]
		while not stack.is_empty():
			var frame: Array = stack[-1]
			var edges: Array = graph[frame[0]]
			if frame[1] >= edges.size():
				order.append(frame[0]); stack.pop_back(); continue
			var target: String = edges[frame[1]]
			frame[1] += 1
			if graph.has(target) and not seen.has(target):
				seen[target] = true; stack.append([target, 0])
	seen.clear()
	var result: Array = []
	order.reverse()
	for root_id in order:
		if seen.has(root_id): continue
		var component: Array = []
		var stack: Array = [root_id]; seen[root_id] = true
		while not stack.is_empty():
			var id: String = stack.pop_back(); component.append(id)
			for target in reverse[id]:
				if not seen.has(target): seen[target] = true; stack.append(target)
		component.sort(); result.append(component)
	result.sort_custom(func(a, b): return str(a[0]) < str(b[0]))
	return result
