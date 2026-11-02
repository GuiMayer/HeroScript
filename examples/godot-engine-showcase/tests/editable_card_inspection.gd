extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print(("[PASS] " if ok else "[FAIL] ") + message)
	if not ok: failures += 1
func run() -> void:
	var details = load("res://scripts/ui/card_details_panel.gd").create()
	var data := {"name": "Guard", "effects": ["Add 8 Block to yourself."]}
	var before := JSON.stringify(data)
	details.configure(data, details.visual_context.card_style)
	var body = details.find_child("CardDetailContent",true,false)
	var rules = details.find_child("DetailedEffects",true,false)
	var scroll = details.find_child("CardDetailsScroll",true,false)
	body.add_theme_constant_override("separation",19)
	details.configure(data, details.visual_context.card_style,details.DisplayMode.TOOLTIP)
	check(details.find_child("CardDetailsScroll",true,false) == null, "Passive summary has no inaccessible scroll node")
	check(details.find_child("DetailedEffects",true,false) == rules, "Presentation mode reuses the authored effect node")
	details.configure(data, details.visual_context.card_style)
	check(details.find_child("CardDetailsScroll",true,false) == scroll and body.get_theme_constant("separation") == 19, "Persistent inspection restores its original scroll and authored spacing")
	details.configure({"presentationFacts": {"referencedRules": [{"definitionId":"custom","name":"Custom","description":"A published rule."}]}},details.visual_context.card_style)
	check(details.find_child("DetailedEffects",true,false) == null, "Facts display does not expose the hidden printed-effect section")
	check(JSON.stringify(data) == before, "Inspection preserves the source read model")
	var collection = load("res://scripts/ui/card_collection_view.gd").create()
	root.add_child(collection)
	var model := {"visible":true,"ordered":true,"count":1,"cards":[{"cardInstanceId":"one","name":"Guard","effects":["Block"]}]}
	collection.apply_model(model)
	var search = collection.get_node("%Search")
	var card = collection.cards.one
	collection.update_card("one",{"cardInstanceId":"one","name":"Guard improved","effects":["More block"]})
	check(collection.get_node("%Search") == search and collection.cards.one == card, "Collection data updates retain shell and card identities")
	collection.restore_local_state({"query":"guard","sort":1})
	check(collection.capture_local_state().query == "guard" and collection.cards.one.visible, "Collection keeps its local search interaction")
	collection.apply_model({"visible":false,"ordered":false,"count":7,"cards":[]})
	check(collection.get_node("%HiddenContents").visible and not search.editable and collection.cards.is_empty(), "Hidden collection does not fabricate card contents")
	var tools = load("res://scripts/ui/card_zone_tool_dialog.gd").create()
	var flows := [{"flowId":"create","steps":[{"operation":"Create","cardDefinitionId":"$input"}]}]
	tools.setup(flows)
	var selector = tools.get_node("%FlowSelector")
	tools.setup(flows)
	check(tools.get_node("%FlowSelector") == selector and selector.item_count == 1, "Repeated zone-tool setup reuses its authored form")
	check(tools.get_node("%Definitions").visible and not tools.get_node("%Instances").visible, "Zone form visibility follows the published flow")
	check(tools.confirmed.get_connections().size() == 2, "Zone form does not duplicate submission or disposal handlers")
	check(root.get_node("GameServices").diagnostics().is_empty(), "Inspection and collection fixtures send no HTTP")
	for view in [details,collection,tools]: view.free()
	quit(1 if failures else 0)
