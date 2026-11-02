extends Control
## Offline harness: instances production visuals, never controllers or gateways.
const Registry = preload("res://scripts/ui/scene_registry.gd")
const Context = preload("res://scripts/ui/visual_context.gd")
var visual_context = Context.new()
var preview: Node
var selected_screen := 0
const STATES := ["full", "empty", "blocked", "loading", "error", "modal"]
# These point to production visuals. Application adapters are never set up here.
const ENTRIES := [
	["Shared components","res://scenes/ui/workbenches/ui_catalog_workbench.tscn","shared"],
	["Loadout laboratory","res://scenes/unified_loadout_lab.tscn","loadout"],
	["Main menu","res://scenes/ui/screens/main_menu.tscn","menu"],
	["Settings","res://scenes/ui/screens/settings.tscn","settings"],
	["Combat","res://scenes/ui/components/combat_view.tscn","combat"],
	["Map","res://scenes/ui/components/map_view.tscn","map"],
	["Activity","res://scenes/ui/components/journey_view.tscn","activity"],
	["Dialogue","res://scenes/ui/dialogue_scene.tscn","dialogue"],
	["Run history","res://scenes/ui/screens/run_history.tscn","history"],
	["Timeline","res://scenes/ui/screens/timeline.tscn","timeline"],
	["Replay","res://scenes/ui/components/run_replay_view.tscn","replay"],
	["Sandbox","res://scenes/ui/screens/sandbox.tscn","sandbox"],
	["Codex","res://scenes/ui/screens/codex.tscn","codex"],
	["Deck compartments","res://scenes/ui/screens/deck_compartment_editor.tscn","compartments"],
	["Hand laboratory shell","res://scenes/card_hand_lab.tscn","hand_lab"],
	["Inscription laboratory shell","res://scenes/inscription_loadout_lab.tscn","inscription_lab"],
	["Gameplay event laboratory","res://scenes/gameplay_event_lab.tscn","events"],
	["Character attributes","res://scenes/ui/components/loadout/character_attributes_view.tscn","attributes"],
	["Pause overlay","res://scenes/ui/overlays/pause_menu.tscn","pause"],
	["Inspection modal","res://scenes/ui/overlays/inspection_dialog.tscn","inspection"],
	["Art effects laboratory","res://scenes/ui/overlays/art_effect_lab.tscn","art_lab"],
]
func _ready() -> void:
	theme = visual_context.theme
	get_node("%Background").color = visual_context.styles.NIGHT
	var selector: OptionButton = get_node("%ScreenSelector")
	for entry in ENTRIES: selector.add_item(entry[0])
	selector.item_selected.connect(show_preview)
	var state_selector: OptionButton = get_node("%StateSelector")
	for state in STATES: state_selector.add_item(state.capitalize())
	state_selector.item_selected.connect(func(_index): show_preview(selected_screen))
	show_preview(0)
func show_preview(index: int) -> void:
	selected_screen = index
	var host = get_node("%ContentHost")
	if is_instance_valid(preview):
		host.remove_child(preview)
		preview.queue_free()
	var entry: Array = ENTRIES[index]
	preview = load(entry[1]).instantiate()
	# Lab shells can be inspected without running their fixture/preference adapters.
	if entry[2] in ["hand_lab", "inscription_lab", "codex"]: preview.set_script(null)
	host.add_child(preview)
	if preview is Control: preview.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var state: String = STATES[get_node("%StateSelector").selected]
	get_node("%PreviewState").text = "Offline fixture · " + state + " · inputs emit local intents only"
	preload("res://scripts/ui/workbenches/screen_catalog_fixtures.gd").apply(preview,entry[2],state,visual_context)
