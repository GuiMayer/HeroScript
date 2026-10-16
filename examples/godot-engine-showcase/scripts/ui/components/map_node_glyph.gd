extends Control
## Small native map symbols; authored non-placeholder art still takes precedence.
var kind := ""
var ink := Color("20201c")
func _draw() -> void:
	draw_set_transform(Vector2.ZERO, 0, size / 48.0)
	match kind:
		"Encounter":
			line([Vector2(10,38),Vector2(35,10),Vector2(38,8),Vector2(38,16),Vector2(14,40)])
			line([Vector2(9,30),Vector2(19,39)]); line([Vector2(10,10),Vector2(37,38)]); line([Vector2(30,38),Vector2(39,30)])
		"Shop":
			line([Vector2(9,18),Vector2(39,18),Vector2(36,40),Vector2(12,40),Vector2(9,18)])
			draw_arc(Vector2(24,18),8,PI,TAU,16,ink,2.5,true)
		"Dialogue", "Conversation":
			line([Vector2(7,9),Vector2(41,9),Vector2(41,31),Vector2(22,31),Vector2(13,40),Vector2(13,31),Vector2(7,31),Vector2(7,9)])
			for x in [16,24,32]: draw_circle(Vector2(x,20),1.7,ink)
		"CardSelection":
			line([Vector2(10,13),Vector2(10,40),Vector2(31,40)]); line([Vector2(16,7),Vector2(37,7),Vector2(37,34),Vector2(16,34),Vector2(16,7)])
			draw_circle(Vector2(26,20),4,ink)
		"CardUpgrade":
			line([Vector2(11,40),Vector2(30,17)]); line([Vector2(19,9),Vector2(27,5),Vector2(40,18),Vector2(34,25),Vector2(19,9)])
		"Preparation":
			line([Vector2(7,39),Vector2(24,10),Vector2(41,39),Vector2(7,39)]); line([Vector2(17,39),Vector2(24,26),Vector2(31,39)])
		"RelicReward", "RelicSelection", "InscriptionSelection":
			line([Vector2(24,5),Vector2(40,24),Vector2(24,43),Vector2(8,24),Vector2(24,5)])
			draw_circle(Vector2(24,24),6,ink)
		"Victory":
			line([Vector2(12,8),Vector2(36,8),Vector2(33,27),Vector2(24,33),Vector2(15,27),Vector2(12,8)])
			line([Vector2(24,33),Vector2(24,41)]); line([Vector2(16,41),Vector2(32,41)])
		_:
			draw_arc(Vector2(24,24),17,0,TAU,32,ink,2.5,true)
			line([Vector2(18,18),Vector2(21,14),Vector2(28,14),Vector2(31,19),Vector2(24,26),Vector2(24,29)])
			draw_circle(Vector2(24,35),1.8,ink)
func line(points: Array) -> void: draw_polyline(PackedVector2Array(points),ink,2.5,true)
