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

## Etapa 7 — Configurações e remapeamento

Shell e seis páginas de configuração possuem nodes estáticos editáveis. Scripts apenas traduzem e ligam os controles ao schema/controller existentes. Abas alternam visibilidade sem recriar sliders nem callbacks. Preview aplica tema sem iniciar sessão nem persistir opções. Testes: cenas editáveis 7, contratos 8, interação 86, recuperação 69 e menus 90 aprovados. Comparação das seis abas em duas resoluções e duas escalas confirmou geometria equivalente (settings.geometry.json).

## Etapa 8 — Shell unificado do loadout

LoadoutPage possui header, hosts, feedback, recuperação e atributos autorados. Tabs usam ModuleCatalog e template compartilhado, sem duplicar IDs na cena. A composição unified_loadout.tscn e o laboratório instanciam a mesma shell. run_loadout.tscn agora delega à composição canônica; removida a implementação antiga com polling e autoridade paralela. Testes migrados conservam verificação de inventário atual, isolamento por personagem/run, respostas atrasadas e equipar/limpar com autosave. Restaurado destaque de atributos da inscrição no painel compartilhado.

Testes aprovados: shell editável 6, navegação 9, layout 48, autosave 9, recuperação 9, fluxo de run 22, input nativo 6 e layout editorial 187. Comparação da shell em três resoluções e duas escalas confirmou geometria equivalente (loadout-shell.geometry.json).

## Etapa 9 — Inscrições, slots e inventário

Layouts editorial e de bancada usam cenas autoradas do mesmo componente passivo. Inventário e slots instanciam tiles compartilhados; preview de drag possui cena própria. Preservados filtros, contraste vazio/equipado, payloads com escopo/revisão, comparação de atributos e autosave. O módulo da run remove a coluna local de atributos; o editor mantém nodes autorados intactos.

Testes aprovados: edição 7, input nativo 6, layout editorial 187 e laboratório 146. Contratos de shell 9, autosave 9 e fluxo da run 22 também passaram na migração. Comparação com a implementação anterior confirmou geometria equivalente nas duas apresentações, em escalas 1.0 e 1.6 (inscriptions.geometry.json).

## Etapa 10 — Baralho, compartimentos e demais módulos

Baralho, resumos, relíquias, placeholder de companions, inventário de modificadores e editor de compartimentos possuem cenas completas. Cartas conservam CardView/cache; ferramentas fixas e navegação não são remontadas. Itens e colunas variáveis usam templates. O inventário reconcilia itens por identidade; critérios de compatibilidade continuam publicados pela engine. Inspeções e fontes de atributos usam overlays/componentes autorados. A cena unificada da run já compõe seus módulos diretamente e usa o registro de rotas.

Preservados workspaces, gateways, transações e guards de contexto. Versionada a dependência de apresentação de modificadores que já existia no workspace, com suas regressões, para que descrições e comparação atual também funcionem no checkout do commit. Testes aprovados: módulos editáveis 8, shell 9, layout 48, autosave 9, recuperação 9, modificadores 33, compartimentos 20, editorial 187, input 6, fluxo da run 22 e registro 7. Comparação de baralho, resumo e inventário de modificadores em duas escalas confirmou geometria equivalente (loadout-modules.geometry.json).

## Etapa 11 — Shell e controles do combate

CombatScreen e CombatView compõem cabeçalho, mão, dock de turno, pilhas, preview, alternativas e recuperação em cenas. Os controles atualizam dados sem remontar a árvore; o dock conserva identidade ao mudar a opção de passar. HandView mantém os algoritmos e executores atuais, com slots e estado vazio autorados. O desenho de keylines permanece procedural num script de node próprio. Composições alternativas continuam movendo os mesmos componentes e preservando estado.

Testes aprovados: shell editável 9, substituição de composição 13, reconciliação 4, contexto 14, pause 24, arquitetura 28, seleção 19, laboratório 118, colaboração 12, ciclo de turno 15, compra 106, contadores 24, estabilidade 34, UX 38, reorganização flow 6 e drop 5. Quatro expectativas antigas de UX já falhavam na baseline por confundir drop para jogar com reorganização; o cenário agora declara reorder desativado e os testes próprios cobrem reorder ativo. Comparação em duas resoluções e duas escalas confirmou geometria equivalente do shell/mão/dock, sem warnings de owner (combat-shell.geometry.json).

## Etapa 12 — Battlefield, cenário e hosts de efeitos

Battlefield, grupos variáveis, cenário, slots de arte, HUD flutuante e feedback possuem cenas próprias. CombatView vincula os hosts autorados; atores continuam instâncias do ActorPanel existente, preservadas por identidade. Posicionamento por anchors, executores, cancelamento, pause, durações e desenho procedural continuam nos mesmos comportamentos. Labels decorativos instanciam template somente quando há duração válida.

Testes aprovados: hosts editáveis 8, arquitetura 28, shell 9, composição 13, contratos de animação 8, limites 4, responsividade 2, cues 6, peças táticas 126, interrupções de reorder 15, compra 106 e pause 24. A regressão confirma duração zero sem node decorativo nem tail. Comparação do shell, mão, dock e anchors de atores em duas resoluções e duas escalas confirmou geometria equivalente (battlefield.geometry.json).

## Etapa 13 — Mapa, nós e inspector

MapScreen, MapView, RouteView, nós e inspector flutuante possuem cenas próprias. Labels fixos do inspector são preenchidos; ações variáveis instanciam templates. Mantidos navigation model, layout profile exportado, câmera, desenho das conexões, referências opacas e guards existentes. O inspector continua fora do layout que calcula a área do mapa.

Testes aprovados: edição 8, grafo 10, geometria 12, seleção 26, topologia 30, conclusão 30, câmera 51, primeira saída de combate 8 e conclusão de atividade 12. O teste da câmera já apresentava falhas na baseline por avançar manualmente um tween após aguardar tempo real; agora pausa na criação antes do avanço controlado, sem mudar a duração em produção. Teste REST protegido para exigir API isolada; execução real permanece para a certificação final. Comparação em duas resoluções e duas escalas confirmou geometria equivalente de mapa, nós, captions e inspector (map.geometry.json).

## Etapa 14 — Atividades, ofertas, loja e forge

ActivityScreen e JourneyView compõem header, resumo, encerramento e host de opções autorados. Ofertas de carta, ações secundárias e ofertas genéricas usam templates reconciliados por referência opaca; CardView permanece canônico. Confirmação de transformação possui Before/After editáveis com as mesmas cartas; inventário de modificadores usa overlay próprio. Controllers, validação, autoavanço e comandos permanecem nos mesmos serviços.

Testes aprovados: cenas editáveis 7, reconciliação 21, ingresso 11, conclusão de atividade 12, modificadores 33 e menus 90. Testes com engine real continuam reservados para API/store isolados na certificação final. Comparação de encontro, recompensa e encerramento em duas resoluções e duas escalas confirmou geometria equivalente (activity.geometry.json). A separação inicial menor do encerramento é exposta como propriedade visual, preservando a apresentação anterior.

## Etapa 15 — Diálogo

DialogueScreen, DialogueScene, DialoguePanel e itens de escolha possuem composição nativa editável. A view vincula retrato, texto, paginação e recuperação; escolhas são reconciliadas por identidade com comandos atuais. DialogueController e DialogueReading permanecem autoridades existentes, sem consequências interpretadas pela view.

Testes aprovados: contratos 6, coordenação 6, leitura/acessibilidade/pause/histórico 38, lifecycle 7 e edição/reconciliação 7. Regressão verifica identidade, espaçamento editado, referência opaca, disponibilidade atual, imutabilidade e ausência de requisições no preview.

## Etapa 16 — Histórico e timeline

Histórico e timeline usam composições completas com ItemList, Tree, scrubber, detalhes e ferramentas editáveis. Scripts vinculam os nodes, traduzem e conectam sinais uma vez. Preservados escopo por setting, paginação, cursor local, permissões, epochs e operações de branch.

Testes aprovados: edição/seleção passiva 7, lifecycle de replay 11, leitura integrada 38 e progressão por setting 30. O preview nativo não consulta a API e selecionar um registro duplica a projeção sem mutá-la.

## Etapa 17 — Replay e inspeção histórica

Replay usa shell e controles nativos; visualização histórica, hosts de feedback e inspector causal têm cenas compartilhadas pela timeline e laboratório. Atores continuam ActorPanel; playback, director, snapshots e cancelamento permanecem nos comportamentos atuais. Refresh histórico conserva heading/scroll/host em vez de recriar a estrutura estática.

Testes aprovados: edição/reconciliação 6, lifecycle 11, carregamento progressivo 13, transporte 5, apresentação histórica 6 e arquitetura 28. Fixtures de eventos passaram em EN/PT, fallback hierárquico e uso do mesmo inspector. As novas regressões verificam sinais únicos, identidade, capacidades e modelos imutáveis, sem tráfego no preview.

Etapas 18–20: pendentes.
