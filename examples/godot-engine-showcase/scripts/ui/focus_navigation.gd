extends RefCounted
## Explicit focus graph for keyboard/gamepad; no gameplay shortcuts replace UI navigation.
static func controls(root: Node) -> Array[Control]:
	var found: Array[Control] = []
	if root is Control and root.is_visible_in_tree() and root.focus_mode == Control.FOCUS_ALL:
		if not root is BaseButton or not root.disabled: found.append(root)
	for child in root.get_children():
		found.append_array(controls(child))
	return found

static func wire(root: Control, initial := false) -> void:
	if not is_instance_valid(root) or not root.is_inside_tree(): return
	var nodes := controls(root)
	if nodes.is_empty(): return
	for i in nodes.size():
		var node := nodes[i]
		node.focus_next = node.get_path_to(nodes[(i + 1) % nodes.size()])
		node.focus_previous = node.get_path_to(nodes[(i - 1 + nodes.size()) % nodes.size()])
		for pair in [["focus_neighbor_left", Vector2.LEFT], ["focus_neighbor_right", Vector2.RIGHT], ["focus_neighbor_top", Vector2.UP], ["focus_neighbor_bottom", Vector2.DOWN]]:
			var closest: Control = node
			var best := INF
			for other in nodes:
				var offset: Vector2 = other.get_global_rect().get_center() - node.get_global_rect().get_center()
				var forward: float = offset.dot(pair[1])
				if forward <= 1: continue
				var side: float = absf(offset.cross(pair[1]))
				var score := forward + side * 3
				if score < best:
					best = score
					closest = other
			node.set(pair[0], node.get_path_to(closest))
	var focused := root.get_viewport().gui_get_focus_owner()
	if initial or not is_instance_valid(focused) or (root.is_ancestor_of(focused) and focused not in nodes):
		var preferred: Array[Control] = nodes.filter(func(node): return node.has_meta("initial_focus"))
		(preferred[0] if not preferred.is_empty() else nodes[0]).grab_focus()
