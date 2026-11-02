# Execução da centralização de cenas Godot

Guia: [plano](GODOT_SCREEN_SCENE_CENTRALIZATION_PLAN.md). Início: 2026-11-01.

## Etapa 0 — Referência e dependências

Referência local preservada em .runtime/screen-centralization/baseline-source, com hashes e inventário do estado inicial. Capturas e métricas em duas resoluções ficam em .runtime/screen-centralization/baseline-captures. Nenhum comando HTTP real ou gravação de preferências é permitido pela captura.

O fixture de replay estava anterior ao contrato de tickets e paginação consistente. Atualizado somente no teste para representar a leitura atual. Testes base: apresentação 86, previews 15, bancada 12 e estabilidade 34 aprovados.

Versionadas dependências visuais já presentes e necessárias à apresentação atual: catálogo/arte SVG, perfis e presenters de tooltip. Não foram incluídas alterações da engine, conteúdo de gameplay, launcher, preferências ou áudio. Demais alterações pré-existentes permanecem separadas.

Captura aprovada: 20 imagens e zero falhas. Métricas versionadas em docs/architecture/baselines/godot-scene-centralization/metrics.before.json.

Baseline de componentes também validada no export limpo do commit: bancada 12, nós editáveis 14, apresentação 86 e estilo canônico 8 aprovados. Incluídas as traduções pré-existentes requeridas pelos presenters versionados.

## Etapa 1 — Contrato, registro e catálogo

Contrato em docs/architecture/GODOT_SCENE_OWNERSHIP_CONTRACT.md. Registro inicial adota a rota loadout_lab existente; main continua sendo o router. Novo ScreenCatalogWorkbench instancia componentes reais e o mesmo laboratório offline, sem declarar sua estrutura interna já migrada. Testes: registro/preview 7, previews 15, contexto 14 e shell do loadout 9 aprovados. Preferências e snapshot da run preservados; zero HTTP no preview.

## Etapa 2 — Overlays compartilhados

InspectionDialog agora tem árvore estática editável e binding idempotente. ConfirmationDialog mantém o contrato nativo numa cena compartilhada; histórico de conversa herda o mesmo shell de inspeção; toast também é uma cena. Consumidores de runtime e testes adotam as fábricas/cenas. Mantidos painel animado, foco/modalidade e metadados da inspeção. Testes: overlays 9, apresentação 86, pause/layers 24, contexto 14, menus 90 e shell do loadout 9 aprovados.

Etapas 3–20: pendentes.
