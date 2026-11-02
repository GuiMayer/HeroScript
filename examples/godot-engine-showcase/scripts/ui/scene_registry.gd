extends RefCounted
## One production scene per route. Add entries only when adopting that scene.
## Routing, sessions and gameplay remain with the existing application adapters.
const ROUTES := {
	"settings": preload("res://scenes/ui/screens/settings.tscn"),
	"menu": preload("res://scenes/ui/screens/main_menu.tscn"),
	"loadout_lab": preload("res://scenes/unified_loadout_lab.tscn"),
}
static func instantiate(route: String) -> Node:
	assert(ROUTES.has(route), "Unregistered UI scene: " + route)
	return (ROUTES[route] as PackedScene).instantiate()
