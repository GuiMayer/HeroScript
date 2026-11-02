# Reconstrução da UI editável da Godot

Escopo autorizado: reconstruir StatusBadge, ResourceView, ActorPanel, CardView e UICatalogWorkbench, mantendo apresentação, animações e contratos com a engine. A cena é proprietária da hierarquia estática; scripts atualizam dados e instanciam cenas para coleções variáveis. Preview não executa gameplay.

## 1 — StatusBadge

Reconstruído com StyleBox local editável, nós únicos persistentes e preview configurável. Atualizações preservam tipografia, margens e identidade dos filhos. Testes: editable_ui_nodes (3 verificações) e actor_panel_polish (10), aprovados em cópia isolada.

Etapas 1–5 implementadas. A auditoria complementar está em conclusão.

## 2 — ResourceView

Barra, legenda e valor são nós da cena; a fábrica create() instancia a mesma cena no combate, sidebar e testes. Sem construção alternativa no construtor. Cor é dado semântico; geometria do StyleBox e dimensões ficam no editor. Mantidos present_change/settle_motion e consumo imutável do snapshot. Testes editable_ui_nodes (6), semantic_cues (6), ui_reconciliation (4) aprovados.

## 3 — ActorPanel

Painel estático editável com título, intenção, recursos, status e alvo persistentes. Variações compacto/battlefield reusam os mesmos controles; listas instanciam ResourceView/StatusBadge e um template de contador. Sinal do alvo consulta a identidade atual, evitando closures antigas. Consumidores do combate/histórico migrados à fábrica da cena. Testes: editable_ui_nodes 10, actor_panel_polish 10, semantic_cues 6, ui_reconciliation 4, resolutions 88 aprovados.

## 4 — CardView

Face reconstruída como árvore estática: título adaptativo, custo, arte, tipo/raridade, condições, efeitos, comportamento e alterações. Auxiliares de custo/badges possuem templates próprios; scripts não montam o esqueleto. Mão, coleção, laboratórios, loadout e ghosts instanciam a mesma cena. Mantidos contratos de input, executor de animações, tooltip e dados da API. Quatro comparações de geometria antes/depois (preserved/ivory, escalas 1.0/1.6) tiveram todos os retângulos de cabeçalho, nome, custo, arte e regras idênticos. Testes: apresentação 86, títulos 157, linhas 544, custo 14, motion 129, contrato de animação 8, zonas 10, ciclo de turnos 15, seleção 19, compra curvilínea 106, contexto 14 e reconciliação 4 aprovados. Teste de nós editáveis passou com 14 verificações. Corrigida a família da fonte do título do ator para manter o comportamento anterior.
## 5 — UICatalogWorkbench

Bancada reconstruída com instâncias reais das quatro cenas, amostras editáveis no Inspector e contexto visual compartilhado sem chamar a API. Corrigidos os campos das amostras de custos e limites dos recursos; testes agora verificam os dados exibidos, não apenas a existência dos nós. Atualizar o preview preserva os controles. Corrigida também a troca de contexto das artes persistentes: o contexto anterior é desconectado antes de assinar o novo, tanto no ator quanto na carta.

Validação desta etapa em projeto isolado: workbench_smoke 10, editable_ui_nodes 14, actor_panel_polish 10, run_graphics_updates 12 e component_context 14 verificações aprovadas.
