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
	var incoming: Dictionary = {}
	var layers: Dictionary = {}
	for id in ids: incoming[id] = 0; layers[id] = 0
	for id in ids:
		for target in graph[id]:
			if incoming.has(target): incoming[target] += 1
	var queue: Array = ids.filter(func(id): return incoming[id] == 0)
	var visited: Array = []
	while not queue.is_empty():
		queue.sort()
		var id: String = queue.pop_front()
		visited.append(id)
		for target in graph[id]:
			if not incoming.has(target): continue
			layers[target] = maxi(layers[target], layers[id] + 1)
			incoming[target] -= 1
			if incoming[target] == 0: queue.append(target)
	# Cyclic residuals remain visible in a separate stable row. Their actual
	# directed edges are retained, including return/self edges and downstream nodes.
	var residual: Array = ids.filter(func(id): return id not in visited)
	var last_layer := -1
	for id in visited: last_layer = maxi(last_layer, layers[id])
	for id in residual: layers[id] = last_layer + 1
	var rows: Dictionary = {}
	for id in ids:
		var layer: int = layers[id]
		if not rows.has(layer): rows[layer] = []
		rows[layer].append(id)
	var rects: Dictionary = {}
	var markers: Dictionary = {}
	var bounds := Vector2.ZERO
	for layer in rows:
		var row: Array = rows[layer]
		for lane in row.size():
			var id: String = row[lane]
			var position: Vector2 = profile.padding + Vector2(lane * (profile.node_size.x + profile.lane_gap + 28), layer * (profile.node_size.y + profile.layer_gap))
			var rect := Rect2(position + Vector2(22, 0), profile.node_size)
			rects[id] = rect
			markers[id] = position + Vector2(0, profile.node_size.y * .5)
			bounds.x = maxf(bounds.x, rect.end.x + profile.padding.x)
			bounds.y = maxf(bounds.y, rect.end.y + profile.padding.y)
	var edges: Array = []
	for id in ids:
		for target in graph[id]:
			if not markers.has(target): continue
			var origin: Vector2 = markers[id]
			var destination: Vector2 = markers[target]
			var returning: bool = layers[target] <= layers[id]
			var path: PackedVector2Array
			if id == target:
				path = PackedVector2Array([origin, origin + Vector2(-16, 18), origin + Vector2(-16, -18), origin])
			elif returning:
				path = PackedVector2Array([origin, origin + Vector2(-14, -44), destination + Vector2(-14, -44), destination])
			else:
				path = PackedVector2Array([origin, origin + Vector2(-14, 12), destination + Vector2(-14, -12), destination])
			edges.append({"from": id, "to": target, "return": returning, "path": path})
	_key = key
	_result = {"rects": rects, "markers": markers, "edges": edges, "bounds": bounds, "cyclic": not residual.is_empty(), "diagnostics": diagnostics}
	return _result.duplicate(true)
