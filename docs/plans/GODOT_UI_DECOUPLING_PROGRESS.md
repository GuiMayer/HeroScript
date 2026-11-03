# Execução do Desacoplamento e Editabilidade da Interface Godot

Plano: [`GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md`](GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md) · Início: 2026-11-03.

## Etapa 0 — Inventário e baseline

- Cópia isolada de referência preservada em `.runtime/ui-decoupling/baseline-source` (1.290 arquivos) com hashes SHA-256 em `.runtime/ui-decoupling/baseline-hashes.json` e registro do status Git inicial em `.runtime/ui-decoupling/initial-status.txt`.
- Inventário completo documentado em [`docs/architecture/GODOT_UI_DECOUPLING_INVENTORY.md`](../architecture/GODOT_UI_DECOUPLING_INVENTORY.md): rotas de `scene_registry.gd`, subcenas, coleções variáveis, referências a autoloads e `router`, propriedades visuais escritas em código, acessos profundos entre componentes e cobertura de testes.
- Baseline geométrica (`1280×720`, `1920×1080`, `2560×1080` × escalas `1.0` e `1.6`), matriz de mãos (`0, 1, 2, 5, 10` cartas e `2, 3` atores) e baseline de poses/durações de animação (`draw`, `use`, `discard`, `staged_discard`, `exile`, `retained`, `hover`, `fan_retarget`, `reduced_motion`, cancelamento e transição de câmera do mapa) capturadas e validadas por `tests/ui_decoupling_baseline.gd` (74 checks aprovados, zero HTTP, zero escrita de preferências).
- Dados versionados em `docs/architecture/baselines/godot-ui-decoupling/baseline.before.json` e `examples/godot-engine-showcase/data/baselines/ui_decoupling_baseline.json`.
- Validação base aprovada em projeto isolado: `ui_decoupling_baseline` (74), `screen_catalog_full` (49), `editable_ui_nodes` (14), `card_draw_pose` (106) e `map_camera_motion` (51).
