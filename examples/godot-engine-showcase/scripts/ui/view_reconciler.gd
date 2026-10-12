extends RefCounted
## Reconcile a passive subtree by semantic name (fallback: type/position).
## Signals stay on the retained node; desired nodes are never attached to the live tree.
static var retained := 0
static var replaced := 0
static func children(parent: Node, desired: Node) -> void:
	var existing := parent.get_children()
	var original := existing.duplicate()
	var incoming_nodes := desired.get_children()
	for i in incoming_nodes.size():
		var incoming: Node = incoming_nodes[i]
		var current: Node = null
		for node in existing:
			if node.name == incoming.name and _semantic_name(incoming):
				current = node
				break
		if current == null and not _interactive(incoming) and i < original.size() and original[i] in existing:
			if str(incoming.name).begins_with("@") or original[i].name == incoming.name:
				current = original[i]
		if current != null and (current.get_class() != incoming.get_class() or current.get_script() != incoming.get_script()):
			current = null
		if current != null and current.get_script() != null and not current.has_method("reconcile_from"):
			# Scripted components must update their own model; generic property copying
			# cannot synchronize their private state or callback identities.
			current = null
		if current == null:
			desired.remove_child(incoming)
			parent.add_child(incoming)
			parent.move_child(incoming, mini(i, parent.get_child_count() - 1))
			replaced += 1
		else:
			existing.erase(current)
			parent.move_child(current, mini(i, parent.get_child_count() - 1))
			patch(current, incoming)
			retained += 1
	for node in existing:
		parent.remove_child(node)
		node.queue_free()

static func patch(current: Node, incoming: Node) -> void:
	if current.has_method("reconcile_from"):
		current.reconcile_from(incoming)
		return
	patch_contents(current, incoming)

static func _interactive(node: Node) -> bool:
	return node is BaseButton or (node is Control and node.focus_mode != Control.FOCUS_NONE)

static func _semantic_name(node: Node) -> bool:
	return not str(node.name).begins_with("@") and str(node.name) != node.get_class()

## Only a component itself can reconcile its passive implementation subtree.
static func patch_contents(current: Node, incoming: Node) -> void:
	if current is Control and incoming is Control:
		if current.has_meta("visual_preferences_revision"): current.remove_meta("visual_preferences_revision")
		for property in ["custom_minimum_size", "size_flags_horizontal", "size_flags_vertical", "mouse_filter",
				"tooltip_text", "visible", "clip_contents", "theme_type_variation"]:
			current.set(property, incoming.get(property))
		# Canonical style/font overrides are replaced before preferences are applied.
		for key in current.get_meta_list():
			if str(key).begins_with("base_"): current.remove_meta(key)
		for property in incoming.get_property_list():
			var key := str(property.name)
			if key.begins_with("theme_override_"):
				current.set(key, incoming.get(key))
	if current is Label or current is RichTextLabel or current is BaseButton:
		if current.text != incoming.text: current.text = incoming.text
	if current is Label:
		for property in ["horizontal_alignment", "vertical_alignment", "autowrap_mode", "text_overrun_behavior", "max_lines_visible"]:
			current.set(property, incoming.get(property))
	if current is BaseButton:
		current.disabled = incoming.disabled
	if current is Range:
		current.min_value = incoming.min_value
		current.max_value = incoming.max_value
		current.value = incoming.value
	if current is TextureRect:
		current.texture = incoming.texture
		current.stretch_mode = incoming.stretch_mode
	for key in incoming.get_meta_list():
		if not str(key).begins_with("base_") and key != "visual_preferences_revision": current.set_meta(key, incoming.get_meta(key))
	children(current, incoming)
