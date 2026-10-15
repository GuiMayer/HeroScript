extends RefCounted
## Presentation units only; no gameplay calculations.
static func value(stat: Dictionary, amount: float, translator, signed := false) -> String:
	return ("+" if signed and amount > 0 else "") + translator.number(amount) + ("%" if stat.format == "percent" else "")

static func bonus(stat: Dictionary, amount: float, translator) -> String:
	if stat.get("deltaUnit") == "percentage_points":
		return ("+" if amount > 0 else "") + translator.number(amount) + " " + translator.text("pp")
	return value(stat, amount, translator, true)
