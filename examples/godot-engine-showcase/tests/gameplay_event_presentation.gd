extends SceneTree
const Presenter = preload("res://scripts/presentation/gameplay_event_presenter.gd")
const Messages = preload("res://scripts/presentation/gameplay_event_messages.gd")
const View = preload("res://scripts/ui/gameplay_event_view.gd")

func _initialize() -> void:
	var fixtures: Array = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/gameplay_events.json"))
	for fixture in fixtures:
		var before := JSON.stringify(fixture)
		var en: Array = Presenter.rows(fixture.batch, "en", fixture.names)
		var pt: Array = Presenter.rows(fixture.batch, "pt_BR", fixture.names)
		assert(not en.is_empty())
		assert(JSON.stringify(en) == JSON.stringify(Presenter.rows(fixture.batch, "fr", fixture.names)))
		assert(en[0].message != pt[0].message)
		assert(Presenter.details(en[0]).contains("Applied change"))
		assert(Presenter.details(en[1]).contains("Earlier cause"))
		assert(JSON.stringify(fixture) == before)
		var view = View.create()
		view.present(fixture.batch, "en", fixture.names)
		assert(view.tree.get_root().get_child_count() > 0)
		view.present({})
		assert(view.tree.get_root().get_child_count() == 0)
		view.free()
	assert(Messages.resolve("unknown.reason", "resource.change.resolved", "en").contains("{resource}"))
	assert(Messages.resolve("unknown.reason", "unknown.kind", "en").contains("{kind}"))
	assert(Messages.resolve("unknown.reason", "status.applied", "pt_BR") == Messages.EN["kind.status"])
	var environmental := {"occurrences": [{"eventId": "environment", "sourceActorId": null, "attributedActorId": null,
		"subjectActorId": null, "kind": "unknown.kind", "reasonId": "unknown.reason", "payload": {}}]}
	assert(Presenter.rows(environmental, "en")[0].message.begins_with("Environment"))
	assert(Presenter.rows(environmental, "pt_BR")[0].message.begins_with("Ambiente"))
	print("PASS gameplay event presentation: two settings, EN/PT, hierarchical fallback, read-only shared inspector")
	quit()
