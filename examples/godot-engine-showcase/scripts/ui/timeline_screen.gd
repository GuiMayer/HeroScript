extends VBoxContainer

var router
var entries: Array = []
var list: ItemList
var details: TextEdit
var selected_sequence := -1
var branch_key: LineEdit
var tree_view: Tree

func setup(owner) -> void:
	router = owner
	add_theme_constant_override("separation", 14)
	var header := HBoxContainer.new()
	var titles := VBoxContainer.new()
	titles.add_child(AppTheme.title(I18n.text("DETERMINISTIC TIMELINE"), 32))
	titles.add_child(AppTheme.muted(I18n.text("Inspect each command or branch from a past decision to explore another outcome.")))
	header.add_child(titles)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(I18n.text("BACK TO COMBAT"), router.open_game, 190))
	add_child(header)
	var body := HSplitContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(body)
	var left := VBoxContainer.new()
	left.custom_minimum_size.x = 410
	left.add_child(AppTheme.title(I18n.text("Commands"), 20, AppTheme.GOLD))
	list = ItemList.new()
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.item_selected.connect(_select)
	left.add_child(list)
	body.add_child(AppTheme.panel(left))
	var right := VBoxContainer.new()
	right.add_child(AppTheme.title(I18n.text("Inspection and branches"), 20, AppTheme.TEAL))
	details = TextEdit.new()
	details.editable = false
	details.size_flags_vertical = Control.SIZE_EXPAND_FILL
	details.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	right.add_child(details)
	var branch_row := HBoxContainer.new()
	branch_key = LineEdit.new()
	branch_key.placeholder_text = I18n.text("branch-name")
	branch_key.text = I18n.text("alternative")
	branch_key.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	branch_row.add_child(branch_key)
	branch_row.add_child(_button(I18n.text("BRANCH HERE"), _branch, 190))
	branch_row.add_child(_button(I18n.text("SIMULATE END TURN"), _simulate, 205))
	branch_row.add_child(_button(I18n.text("VERIFY REPLAY"), _verify, 185))
	right.add_child(branch_row)
	tree_view = Tree.new()
	tree_view.custom_minimum_size.y = 130
	tree_view.columns = 2
	tree_view.set_column_title(0, "Branch")
	tree_view.set_column_title(1, "Run")
	right.add_child(tree_view)
	body.add_child(AppTheme.panel(right))
	call_deferred("_load")

func _load() -> void:
	var response := await GameSession.timeline()
	if not is_inside_tree():
		return
	if response.ok:
		entries = response.data.get("items", response.data if response.data is Array else [])
		list.clear()
		for entry in entries:
			var sequence := int(entry.get("runSequence", entry.get("sequence", 0)))
			var type := str(entry.get("commandType", entry.get("type", "COMMAND")))
			list.add_item("#%03d   %s" % [sequence, type])
		if not entries.is_empty():
			list.select(entries.size() - 1)
			_select(entries.size() - 1)
	else:
		details.text = response.error
	await _load_tree()

func _select(index: int) -> void:
	if index < 0 or index >= entries.size():
		return
	var entry: Dictionary = entries[index]
	selected_sequence = int(entry.get("runSequence", entry.get("sequence", -1)))
	details.text = JSON.stringify(entry, "  ")

func _branch() -> void:
	if selected_sequence < 0 or branch_key.text.strip_edges().is_empty():
		router.show_error(I18n.text("Select a command and enter a branch name."))
		return
	var response := await GameSession.create_branch(selected_sequence, branch_key.text.strip_edges())
	if not is_inside_tree():
		return
	if not response.ok:
		router.show_error(I18n.error(response))
		return
	var new_run_id := str(response.data.get("runId", response.data.get("branchRunId", "")))
	if new_run_id.is_empty():
		router.show_error(I18n.text("The branch was created, but its run ID is missing."))
		return
	GameAudio.reward()
	if await GameSession.continue_run(new_run_id):
		router.show_toast(I18n.text("Branch '%s' activated.") % branch_key.text)
		router.open_game()

func _load_tree() -> void:
	var response := await GameSession.branch_tree()
	if not is_inside_tree():
		return
	tree_view.clear()
	var root := tree_view.create_item()
	root.set_text(0, I18n.text("origin"))
	root.set_text(1, str(GameSession.run.get("runId", "")).left(8))
	if not response.ok:
		return
	var nodes: Array = response.data.get("nodes", response.data.get("branches", []))
	for node in nodes:
		var item := tree_view.create_item(root)
		item.set_text(0, str(node.get("branchKey", I18n.text("main"))))
		item.set_text(1, str(node.get("runId", "")).left(8))

func _verify() -> void:
	var result := await GameSession.verify()
	if not is_inside_tree():
		return
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		router.show_toast(I18n.text("All hashes match.") if valid else I18n.text("A divergence was found."), not valid)
	else:
		router.show_error(I18n.error(result))

func _simulate() -> void:
	var actor_id := GameSession.input_actor_id()
	if actor_id.is_empty():
		router.show_error(I18n.text("No actor is waiting for input to simulate."))
		return
	var response := await GameSession.simulate_end_turn()
	if not is_inside_tree():
		return
	if response.ok:
		details.text = JSON.stringify(response.data, "  ")
		router.show_toast(I18n.text("Simulation completed without changing the run."))
	else:
		router.show_error(I18n.error(response))

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.custom_minimum_size.y = 44
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
