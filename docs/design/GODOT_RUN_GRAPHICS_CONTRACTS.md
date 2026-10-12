# Contratos visuais de combate e jornada

Atualizado em 2026-10-12. Regras e fatos da partida continuam na engine; estes contratos são do cliente Godot.

## Fronteiras

| Dono | Responsabilidade | Não faz |
| --- | --- | --- |
| CombatScreen / ActivityScreen / MapScreen / DialogueScreen | Adaptar a sessão, conectar intenções e navegar | Construir cartas/atores/mapa ou calcular efeitos |
| CombatController / ActivityController | Validar opções atuais, seleção, submissão, recuperação e verificações | Criar nodes/temas |
| CombatView / JourneyView / ActivityOptionsView / MapView / DialogueView | Layout, conteúdo visual, foco, scroll, modais de leitura e intenções | Consultar sessão/HTTP/preferências globais ou executar comandos |
| CombatInspectionCoordinator | Cache de leitura por contexto, revisão e alvos; coalescência e descarte | Guardar cenas ou produzir comandos |
| Playback / CombatPresentationDriver | Cursor único / ritmo desse cursor | Refazer resultados de combate |
| ApplicationShell.RunSceneryHost | Ambiente e instância visual de combate | Controlar a run |
| CombatSceneryView | Fundo e artes por instanceId | Receber input ou decidir alvos |
| BattlefieldView | Indicadores e superfícies de alvo, agrupados por equipe/relação | Possuir imagens dos atores ou inferir permissões |
| FloatingHUD | Slots externos e seu ciclo de vida | Decidir pagamentos/ações |
| MapLayout / RouteView | Geometria pura / desenho e escolhas publicadas | Deduzir travel pela adjacência |

## Contexto e atualização

VisualContext possui sua própria instância da ViewStyleFactory e Theme nativo cacheado. AppTheme é uma fachada da mesma implementação para consumidores não migrados. Uma aplicação global de preferências respeita componentes com contexto explicitamente diferente.

Perfis de UI, CardVisualStyle, HandVisualStyle, CombatLayoutProfile, MapLayoutProfile e SceneryVisualProfile têm donos diferentes. Raridade, comparação numérica e anatomia de custo continuam na carta, não no cenário.

Views recebem cópias/modelos de leitura. Componentes com script usam reconcile_from/apply_model; a reconciliação genérica não copia parcialmente seu estado privado. Nós interativos não são identificados apenas pela posição. Escolhas de atividade usam referência do payload; opções de combate usam referência contextual do presenter. O controller resolve contra a projeção atual antes de submeter.

## Cenário e coordenadas

O ambiente procedural é EmberBackground, injetado com contexto/perfil de cenário. O shell adquire/libera uma CombatSceneryView por tela de combate. Sair do combate remove as artes externas; esconder a view também esconde o palco.

Na implementação inicial Control, o layout do battlefield publica seus slots como anchors. O adaptador de cenário converte uma única vez posição/tamanho para o espaço global da viewport. Arte e hitbox não são o mesmo node. Arrastar uma carta envia um ponto de viewport; a coordenação combina o hit test visual com os candidatos publicados, nunca com a relação Ally/Enemy/Neutral.

Múltiplas equipes permanecem distintas mesmo quando possuem a mesma relação. Relações desconhecidas têm legenda neutra, mas seu valor original permanece na projeção.

## Mapping JSON de cenário

Arquivo inicial: `examples/godot-engine-showcase/data/scenery.json`. Prioridade:

1. defaults globais;
2. defaults do setting;
3. mapping global do nó;
4. mapping do nó no setting;
5. mapping global do encontro e mapping do encontro no setting, quando o definition ID estiver publicado.

O fallback usa o backgroundId `combat`. Não se deduz definição de encontro a partir de instance IDs. Artes de atores/fundos usam o manifesto do SceneryVisualProfile; outras artes usam o perfil de UI. Hotreload de uma textura na mesma rota ainda depende da reimportação/cache de recursos Godot; não se promete recarregamento de bitmap pela simples atualização de JSON.

## Mapa

MapLayout retorna rects, markers, arestas com paths, bounds, indicador de ciclos e diagnostics. O cache usa topologia ordenada por IDs e perfil. A largura disponível limita o painel/scroll, não reduz fontes ou modifica permissões. Estado atual/resolvido/risco/travel não muda a geometria.

DAGs usam camadas; ramos paralelos usam faixas. Componentes fortemente conectados agrupam ciclos, e sua condensação mantém os nós posteriores em camadas posteriores. Arestas de retorno/self edges são preservadas. Referências ausentes não criam nós fictícios. IDs vazios/duplicados geram diagnóstico visual, não correção da run.

MapScreen é uma cena própria. Selecionar/focar um nó inspeciona; travel exige sua referência publicada atual. Pan, legenda, overview e destaque por tipo são leitura local. Estado de leitura é limitado a 16 contextos setting/run/branch. O avanço automático ocorre somente quando ADVANCE_NODE é a única opção de gameplay; a conclusão de conversa estabelece uma barreira para permitir ler o mapa antes de viajar. Depois de travel explícito, um único START_ENCOUNTER/CREATE_CARD_SELECTION/CREATE_SHOP/CREATE_PREPARATION publicado pode abrir a atividade. Nenhuma escolha de prêmio/custo é automatizada.

## Diálogo dedicado

DialoguePresenter preserva run/branch/sequence/instância/nó/revisão e só associa opções a comandos da mesma instância/nó. DialogueController envia o payload publicado; não interpreta consequências. DialogueView/DialoguePanel apresentam retrato, fala, escolhas/custos/indisponibilidade e histórico em modal somente leitura.

DialogueReading controla exclusivamente páginas e revelação opcional (instantâneo por padrão). O atalho remapeável confirm_action revela/avança a leitura, nunca escolhe. As opções ficam bloqueadas até concluir a última página e enquanto a sessão estiver bloqueada. Restaurar leitura não envia REST; outro branch/nó/revisão/idioma não reutiliza uma página incompatível. A leitura permanece local ao cliente; não entra no journal ou hash da run.

## Inspeção e apresentação

### Combate de referência e coleções

CombatLayoutProfile possui largura proporcional/limite da mão e área segura. CombatLayoutMetrics calcula somente geometria. Adaptive fan mantém inclinação com duas cartas; a altura reservada não depende da quantidade, evitando mover HUD/dock quando a mão esvazia. HandVisualStyle possui sua chave de apresentação para mudanças em runtime.

O dock usa ícones SVG e keylines angulares com Button nativo, tooltip/nome acessível e remaps existentes. Hand overview foi removido do combate e das ações remapeáveis; o laboratório conserva seu consumidor local. Pilhas usam endereços zoneId/ownerId/scopeId e presentation.iconId/dock opcionais; desconhecidas possuem fallback visual, sem semântica na engine.

CardCollectionPresenter/CardCollectionView/CardZoneDialog renderizam instâncias com CardView, sem seleção de combate. ContentsVisible=false não vira Empty; OrderVisible=false não revela próxima compra. Busca/ordenação são locais. Clicar uma carta solicita a avaliação individual REST existente; abrir a grade não faz uma request por carta. O coordenador limita concorrência, compartilha/cacha leituras e descarta callbacks fora do contexto. Custo ausente não é interpretado como zero. Páginas/pilhas não têm outro playback nem enviam comandos.

A certificação desta composição e as capturas estão em [reference-combat/2026-10-12](reference-combat/2026-10-12/README.md).

A chave de inspeção contém run/branch/combate/ator/zonas de jogo, sequência, step, revisão de conteúdo e os alvos efetivamente enviados. Há até 16 respostas cacheadas; requests iguais simultâneos compartilham uma leitura. Dispose libera esperas e descarta respostas atrasadas. Arrays/dictionaries retornam por cópia.

O driver controla apenas o intervalo entre avanços. Uma nova geração de receipt reinicia o intervalo; refresh/tema não o reinicia. Pause bloqueia avanço manual e automático. Frames, journal e resultados continuam pertencendo à engine/Playback existente.

## Validação

Fixtures não iniciam runs na API pessoal. O runner usa diretório de usuário isolado. A [certificação da run Marfim](ivory-run/2026-10-12/README.md) registra testes offline/online em QA normal isolado, capturas e limites, incluindo ferramentas privilegiadas não habilitadas e diagnósticos de encerramento.
