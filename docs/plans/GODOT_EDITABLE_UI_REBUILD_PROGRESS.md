# Reconstrução da UI editável da Godot

Escopo autorizado: reconstruir StatusBadge, ResourceView, ActorPanel, CardView e UICatalogWorkbench, mantendo apresentação, animações e contratos com a engine. A cena é proprietária da hierarquia estática; scripts atualizam dados e instanciam cenas para coleções variáveis. Preview não executa gameplay.

## 1 — StatusBadge

Reconstruído com StyleBox local editável, nós únicos persistentes e preview configurável. Atualizações preservam tipografia, margens e identidade dos filhos. Testes: editable_ui_nodes (3 verificações) e actor_panel_polish (10), aprovados em cópia isolada.

Etapas 2–5 e auditoria das outras telas: pendentes.
