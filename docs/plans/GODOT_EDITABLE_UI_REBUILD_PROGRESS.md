# Reconstrução da UI editável da Godot

Escopo autorizado: reconstruir StatusBadge, ResourceView, ActorPanel, CardView e UICatalogWorkbench, mantendo apresentação, animações e contratos com a engine. A cena é proprietária da hierarquia estática; scripts atualizam dados e instanciam cenas para coleções variáveis. Preview não executa gameplay.

## 1 — StatusBadge

Reconstruído com StyleBox local editável, nós únicos persistentes e preview configurável. Atualizações preservam tipografia, margens e identidade dos filhos. Testes: editable_ui_nodes (3 verificações) e actor_panel_polish (10), aprovados em cópia isolada.

Etapas 2–5 e auditoria das outras telas: pendentes.

## 2 — ResourceView

Barra, legenda e valor são nós da cena; a fábrica create() instancia a mesma cena no combate, sidebar e testes. Sem construção alternativa no construtor. Cor é dado semântico; geometria do StyleBox e dimensões ficam no editor. Mantidos present_change/settle_motion e consumo imutável do snapshot. Testes editable_ui_nodes (6), semantic_cues (6), ui_reconciliation (4) aprovados.

## 3 — ActorPanel

Painel estático editável com título, intenção, recursos, status e alvo persistentes. Variações compacto/battlefield reusam os mesmos controles; listas instanciam ResourceView/StatusBadge e um template de contador. Sinal do alvo consulta a identidade atual, evitando closures antigas. Consumidores do combate/histórico migrados à fábrica da cena. Testes: editable_ui_nodes 10, actor_panel_polish 10, semantic_cues 6, ui_reconciliation 4, resolutions 88 aprovados.
