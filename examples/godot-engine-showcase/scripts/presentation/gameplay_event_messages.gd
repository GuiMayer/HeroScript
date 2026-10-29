extends RefCounted
## Hierarchical reason -> kind -> generic; locale falls back to English at each address.
const EN := {
	"reason.cost.payment": "{source} paid {resource}: {applied} ({target})",
	"reason.cost.refund": "{source} refunded {resource}: {applied} ({target})",
	"kind.resource.change": "{source} changed {target}'s {resource}: {applied}",
	"kind.status": "{source} → {target}: {kind}",
	"kind.resource.transport": "{source} → {target}: resource transfer resolved",
	"generic": "{source} → {target}: {kind}",
	"environment": "Environment", "earlier_cause": "Earlier cause", "resourceId": "Resource", "operation": "Operation",
	"field": "Field", "before": "Before", "after": "After", "requestedDelta": "Requested change", "appliedDelta": "Applied change",
	"limitedDelta": "Limited change", "empty": "No recorded gameplay facts for this command.", "causes": "Causes", "details": "Details"
}
const PT := {
	"reason.cost.payment": "{source} pagou {resource}: {applied} ({target})",
	"reason.cost.refund": "{source} reembolsou {resource}: {applied} ({target})",
	"kind.resource.change": "{source} alterou {resource} de {target}: {applied}",
	"kind.resource.transport": "{source} → {target}: transferência de recurso resolvida",
	"generic": "{source} → {target}: {kind}", "environment": "Ambiente", "earlier_cause": "Causa anterior",
	"resourceId": "Recurso", "operation": "Operação", "field": "Campo", "before": "Antes", "after": "Depois",
	"requestedDelta": "Mudança solicitada", "appliedDelta": "Mudança aplicada", "limitedDelta": "Mudança limitada",
	"empty": "Nenhum fato de gameplay registrado para este comando.", "causes": "Causas", "details": "Detalhes"
}

static func word(key: String, locale: String) -> String:
	return str(PT.get(key, EN.get(key, key))) if locale.begins_with("pt") else str(EN.get(key, key))

static func resolve(reason: String, kind: String, locale: String) -> String:
	for address in ["reason." + reason, "kind." + kind]:
		var candidate: String = address
		while candidate.contains("."):
			if EN.has(candidate) or (locale.begins_with("pt") and PT.has(candidate)): return word(candidate, locale)
			candidate = candidate.left(candidate.rfind("."))
	return word("generic", locale)
