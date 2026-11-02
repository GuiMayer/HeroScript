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

## Etapa 3 — Inspeção e coleções de cartas

CardDetails usa cena própria com seções e linhas/templates editáveis. Tooltip passivo e inspeção persistente reusam os mesmos nós; hosts inativos são destacados apenas em runtime, mantendo a propriedade dos nós intacta no editor. Coleção, diálogo de pilha e formulário de flows também são cenas; busca/ordenação, visibilidade e sinais permanecem com os mesmos contratos. Modos preservam nós e espaçamento; nenhum conteúdo oculto é fabricado. Consumidores migrados para as fábricas canônicas.

Testes: edição/inspeção 12, apresentação 86, coleções 9, overlays 9, seleção 19, estabilidade 34, menus 90 e itens modificadores 33 aprovados. Eliminados os warnings de owner na alternância de hosts. Tipos, efeitos e cálculos continuam vindo dos presenters existentes.

## Etapa 4 — Atributos compartilhados e drawer

CharacterAttributesView e CombatAttributesDrawer possuem cenas reais; linhas e grupos variáveis instanciam templates de atributos. Loadout, laboratório e combate usam a mesma cena. Preservados autosave/adapters, atalho I, hover da borda, animação de abertura e projeção dos dados publicados. Fontes-base de edição são mantidas em cache privado, sem gravar metadados de preview no asset. Atualizações preservam padding, estado de abertura e sinais.

Testes: nós de atributos 10, drawer do combate 23, layout editorial 187, input do loadout 6, laboratório de inscrições 146 e registro/catálogo 7 aprovados. Corrigido o teste antigo que ainda esperava a entrada de laboratório anterior ao shell unificado. Comparação com as classes da baseline em escalas 1.0 e 1.6 confirmou retângulos equivalentes para painel, toggle, título, scroll, nome e linhas de atributos. Relatório em docs/architecture/baselines/godot-scene-centralization/attributes.geometry.json.

## Etapa 5 — Shell, pause e notificações

ApplicationShell possui hosts estáticos de cenário, tela e notificações; o ambiente é uma subcena com desenho procedural compartilhado. PauseMenu é uma cena passiva completa, com todos os controles, painel e layer editáveis. A shell liga as intenções aos mesmos callbacks de aplicação, sem reconstruir chrome no refresh. Foco, histórico de conversa, abandono e descarte do modal continuam no coordenador. Toast usa a cena existente, com superfície local editável.

Testes: shell editável 10, pause/layers 24, menus 90, leitura/retomada de diálogo 38, arquitetura gráfica 28, contextos 8 e overlays 9 aprovados. O teste do histórico consulta o RichTextLabel pelo binding canônico, não pela posição antiga entre os filhos. Comparação com a baseline em escalas 1.0 e 1.6 confirmou retângulos equivalentes de overlay, painel, resume, trace e toast. Relatório em docs/architecture/baselines/godot-scene-centralization/shell.geometry.json.

## Etapa 6 — Menu principal e seleção de setting

Menu principal centralizado em cena editável, registrada no router e no catálogo offline. Atualizações reutilizam os controles, preservando a invalidação de consultas assíncronas de continuar. Paper e shell também são instâncias autoradas. Testes: menu editável 8, layout 41, menus 90, progressão por setting 30, registro 7 e pause 24 aprovados. Comparação com a baseline em três larguras e duas escalas confirmou geometria equivalente; relatório menu.geometry.json.

Etapas 7–20: pendentes.
