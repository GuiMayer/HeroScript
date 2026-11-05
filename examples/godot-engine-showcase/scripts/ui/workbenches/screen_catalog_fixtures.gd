extends RefCounted
## Synthetic read models only. No session, controller, transport or persistence.
class Owner extends RefCounted:
	var visual_context
	func back_to_menu(): pass
static func appearance() -> Dictionary:
	var data = JSON.parse_string(FileAccess.get_file_as_string("res://data/presentation.json"))
	return data if data is Dictionary else {}
static func run_fixture() -> Dictionary:
	return {"runId":"catalog","sequence":1,"lifecycle":"Active","playerEntityId":"hero","currentNodeId":"first","map":{"nodes":[
		{"nodeId":"first","activity":{"type":"Encounter"},"nextNodeIds":["reward","forge"],"metadata":{"displayName":"First battle"}},
		{"nodeId":"reward","activity":{"type":"CardSelection"},"nextNodeIds":["boss"],"metadata":{"displayName":"Card reward"}},
		{"nodeId":"forge","activity":{"type":"Upgrade"},"nextNodeIds":["boss"],"metadata":{"displayName":"Forge"}},
		{"nodeId":"boss","activity":{"type":"Encounter"},"nextNodeIds":[],"metadata":{"displayName":"Boss"}}]}}
static func apply(view, kind: String, state: String, context) -> void:
	var empty := state in ["empty","loading","error"]
	var locked := state in ["blocked","loading"]
	var long_text := state == "long_text"
	var data := appearance().duplicate(true)
	match kind:
		"shared":
			if view.has_method("apply_fixture_mode"):
				view.apply_fixture_mode(state if state in ["full", "empty", "error", "long_text"] else "full")
		"menu":
			view.mount(context); var model: Dictionary = view.preview_model.duplicate(true)
			model.available = not empty; model.connecting = state == "loading"; model.pending = state == "error"
			if long_text:
				model["setting_summary"] = "Extended chronicle summary verifying multi-line wrapping and editorial proportions without mutating shared menu resources."
			view.apply_model(model)
		"combat":
			var run := run_fixture()
			var actors: Array = []
			for entry in [["hero","spire_adept","player"],["enemy","spire_wisp","enemy"]]:
				actors.append({"instanceId":entry[0],"definitionId":entry[1],"sideId":entry[2],"resources":{"health":{"current":42,"maximum":60},"energy":{"current":3},"block":{"current":8}},"statuses":[]})
			var combat := {"combatId":"fixture","activeActorId":"hero","activation":{"round":3,"activeActorId":"hero","waitingForInput":true},"relationships":{"sameSide":"Ally","differentSides":"Enemy"},"actors":actors}
			var cards: Array = []
			if not empty:
				for i in 5: cards.append({"cardInstanceId":"fixture-%s" % i,"definitionId":["basic_attack","defend","fireball","heal","arcane_bolt"][i]})
			var zones := {"zones":[{"zoneId":"play","allowsCardPlay":true,"contentsVisible":true,"cards":cards,"count":cards.size()},{"zoneId":"draw","count":8},{"zoneId":"discard","count":2}]}
			var presenter = load("res://scripts/presentation/combat_presenter.gd").new(run,combat,[],context.translator,data,zones)
			var card_presenter = load("res://scripts/presentation/card_zone_presenter.gd").new(zones,context.translator)
			view.animate_card_entry = false
			view.apply_projection(data,presenter,card_presenter,{"run":run,"combat":combat,"actions":[],"zones":zones,"input_actor":"hero","scope":"catalog","setting":{},"setting_id":"default"},context)
			view.apply_interaction({"submitting":locked,"synchronized":not locked})
		"map", "activity":
			var run := run_fixture()
			if empty: run.map.nodes = []; run.currentNodeId = ""
			elif long_text:
				for node in run.map.nodes:
					node.metadata.displayName = str(node.metadata.displayName) + " — Sanctuary of the Long Horizon"
			var options: Array = [] if empty else [{"type":"START_ENCOUNTER","validPayload":{}}]
			var presenter = load("res://scripts/presentation/activity_presenter.gd").new(run,options,data,context.translator)
			if kind == "map": view.apply_projection(run,options,presenter,context)
			else: view.apply_projection(run,options,{},data,presenter,context)
			if view.has_method("set_read_only"): view.set_read_only(locked)
			elif view.has_method("set_choices_disabled"): view.set_choices_disabled(locked)
		"dialogue":
			var body_text := "This is a visual fixture. A choice sends only its published reference; the engine determines its consequence."
			if long_text:
				body_text += " Extended narrative passage used by the offline workbench to verify scrolling, line wrapping, and reading progress preservation across long paragraphs."
			var model := {} if empty else {"runId":"catalog","branchId":"origin","dialogueInstanceId":"conversation","nodeId":"line","title":"A conversation","speaker":"Keeper","portrait":"","text":body_text,"options":[{"id":"continue","text":"Continue along the outer archive corridor" if long_text else "Continue","available":true,"cost":"","reason":"","reference":"fixture","command":{"type":"CHOOSE_DIALOGUE_OPTION"}}]}
			view.apply_projection(model,context); view.set_read_only(locked)
		"replay":
			view.setup(context,true)
			view.apply_model({"entries":[] if empty else [{"sequence":1,"commandType":"PLAY_CARD","step":1}],"selected_index":-1 if empty else 0,"through_sequence":1,"page_error":state == "error","state_text":"Saved state" if state != "error" else "Fixture connection error","frame_text":"Recorded effects — extended inspection trace for long text verification" if long_text else "Recorded effects","has_more":state == "loading","playing":false,"verifying":false,"page_loading":state == "loading","inspection_allowed":not locked,"presentation_loading":state == "loading"})
		"history":
			view.list.clear()
			if not empty:
				view.entries = [{"runId":"catalog","sequence":12,"lifecycle":"Completed","modeId":"fixture","currentNodeId":"boss"}]
				view.list.add_item("Completed · Boss — Extended Chronicle Entry" if long_text else "Completed · Boss"); view._select(0)
			else: view.details.text = "Loading journey history…" if state == "loading" else ("Fixture connection error" if state == "error" else "No recorded journeys yet.")
		"timeline":
			view.entries = [] if empty else [{"runSequence":1,"turn":1,"phase":"middle","actorId":"hero","stateAvailable":false}]
			if not empty: view.list.add_item("#001 · PLAY_CARD"); view._select(0)
		"sandbox":
			var owner := Owner.new(); owner.visual_context = context
			view.setup(owner,{"sandbox_modes":[] if empty else [{"id":"combat_sandbox","name":"Published mode","description":"Fixture rules with extended description for wrapping verification" if long_text else "Fixture rules"}],"default_scenario":{"actors":[]}})
			view.launch_button.disabled = true # A preview never launches gameplay.
		"codex":
			var list: ItemList = view.get_node("Columns/List")
			if not empty: list.add_item("fixture_definition_with_extended_identifier" if long_text else "fixture_definition")
			view.get_node("Columns/DetailScroll/DetailBody/FormattedDetail").text = '{"definitionId":"fixture_definition"}' if not empty else ""
		"hand_lab":
			var hand = view.get_node("Margin/Outer/Hand"); hand.mount(context)
			var fixture = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json"))
			var cards: Array = []
			if not empty:
				for id in ["strike","ward","fireball","heal","arcane"]:
					var model: Dictionary = fixture.cards[id].duplicate(true); model.cardInstanceId = id; cards.append(model)
			hand.sync_models(cards,"catalog"); hand.set_read_only(locked)
		"inscription_lab":
			var inscriptions = view.get_node("Margin/LoadoutPageScroll/Page/View"); inscriptions.mount(context)
			var draft = load("res://scripts/labs/inscription_loadout_draft.gd").new()
			draft.configure(JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/inscription_loadout_lab.json")))
			var model: Dictionary = draft.snapshot(); model.canEdit = false; inscriptions.present(model)
		"pause": view.setup(context,true,true)
		"inspection":
			view.visual_context = context; view.setup("Inspection","Offline modal fixture",{"state":state})
			view.popup_centered_clamped(Vector2i(850,600),.9)
		"art_lab":
			view.setup(context); view.popup_centered_clamped(Vector2i(900,650),.9)
		"attributes":
			view.mount(context)
			var draft = load("res://scripts/labs/inscription_loadout_draft.gd").new()
			draft.configure(JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/inscription_loadout_lab.json")))
			view.present(draft.snapshot().character)
