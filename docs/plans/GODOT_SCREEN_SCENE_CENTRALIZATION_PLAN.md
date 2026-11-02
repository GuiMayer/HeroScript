# Plano de centralização das telas em cenas editáveis da Godot

Data: 2026-11-01. Estado: proposto; nenhuma etapa de implementação deste plano foi executada.

Escopo: examples/godot-engine-showcase. Referência: [auditoria atual](../architecture/GODOT_EDITABLE_UI_SCENES_AUDIT.md). Este plano substitui a afirmação de que a migração híbrida inicial já cobria toda a interface. Os exemplos web e a engine C# não fazem parte da migração visual.

## 1. Resultado esperado

Cada tela deve ter uma cena completa, aberta e ajustável no editor, composta pelos mesmos componentes usados durante o jogo. Alterar margens, alinhamento, estilo ou elementos de uma cena compartilhada deve refletir em todos os seus consumidores.

Centralizar não significa criar uma cena gigantesca. Significa ter uma definição visual por tela/componente, com composição explícita em PackedScenes. Uma cena que contém apenas um host e continua construindo todos os seus filhos no script não conta como migrada.

A migração deve manter o visual branco/programmer art atual, disposição, dimensões, variantes responsivas, animações e durações configuradas, interação, sons, localização e integração REST. Não inclui redesign, novos modos, implementação de companions ou alteração de regras/conteúdo.

## 2. Diagnóstico confirmado

- CardView, ActorPanel, ResourceView e StatusBadge já possuem cenas compartilhadas efetivamente consumidas. Não recriar suas faces ou estruturas nas próximas etapas.
- A bancada UICatalogWorkbench instancia esses componentes e já usa o perfil atual. O estilo antigo da carta e o caminho alternativo de renderização foram removidos.
- main.gd ainda instancia várias telas por Script.new(), configura-as antes de anexar à árvore e controla a navegação. Não existe atualmente um registro central de PackedScenes para todas essas telas.
- screen_view.gd já oferece mount, apply_model, apply_interaction, capture_local_state, restore_local_state e dispose. Os controllers e presenters existentes devem continuar sendo usados.
- ApplicationShell possui ScreenHost, mas cria pause, histórico de conversa, confirmação e notificações por código.
- CombatComposition tem apenas CombatBody/CombatGameplay. CombatView ainda monta cabeçalho, HUD, mão, controles, drawers e alternativas; BattlefieldView e FloatingHUD também criam suas estruturas.
- MapView constrói cabeçalho, workspace, canvas, scroll e inspector. RouteView gera controles dos nós; o posicionamento do grafo e a câmera são comportamentos que devem continuar em código.
- DialogueScene contém apenas a raiz com script. DialogueView e DialoguePanel ainda constroem o painel e as opções.
- Loadout unificado, inscrições, atributos, deck/compartimentos, atividades, settings, histórico, timeline, replay, codex e sandbox possuem construção procedural relevante.
- Alguns laboratórios já são arquivos .tscn, mas sua estrutura interna continua procedural. Ter extensão .tscn não comprova centralização.
- Perfis de geometria, estilo e movimento já existem: CombatLayoutProfile, MapLayoutProfile, CardVisualStyle, VisualContext e políticas/executores de animação. Devem ser preservados, com autoridade explícita.

## 3. Fronteiras de responsabilidade

| Camada | Deve possuir | Não deve possuir |
| --- | --- | --- |
| Cena .tscn | Árvore estática, containers, anchors, flags, gaps, margens, controles, layers e instâncias de componentes | Regras, endpoints e escolha de consequências |
| Recursos .tres | Theme, paleta, tipografia, métricas compartilhadas, variantes e políticas visuais já configuráveis | Estado da run ou cópias de definições da engine |
| Script da view | Binding dos nós, preenchimento, sinais de intenção, listas variáveis, acessibilidade e aplicação das políticas | Construção paralela do esqueleto estático ou acesso direto ao transporte |
| Controller/coordenador | Conexão entre view, sessão, ações, inspeção, cancelamento e apresentação | Criar labels, containers ou faces de cartas |
| Presenter | Transformar read models publicados em modelos de apresentação | Recalcular gameplay ou inventar efeitos não publicados |
| Algoritmos/executores | Fan, hit testing, drag, câmera, desenho de conexões, shaders, transições e cancelamento | Decidir resultados da engine |
| Engine/API | Regras, comandos, disponibilidade de ações, persistência, branches e resultados | Conhecer a hierarquia de nodes da Godot |

Fluxo alvo: engine/API → sessão → presenter/controller → dados da cena → nodes persistentes. Input percorre o caminho de volta como intenção; a cena não aplica diretamente uma consequência de gameplay.

### Autoridade visual e edição

1. Geometria fixa pertence à cena. Perfis existentes continuam responsáveis apenas pelas dimensões/variantes explicitamente configuráveis e pelo comportamento responsivo.
2. Cada propriedade precisa ter um proprietário identificado. Evitar que o script sobrescreva, em todo refresh, uma margem que deveria ser editada na cena.
3. Cores semânticas, tradução, escala de texto e contraste continuam dependentes do contexto. Documentar quais overrides do Inspector são dirigidos por esses recursos.
4. Não inferir papel semântico apenas por RGB: painel e canvas podem ter a mesma cor. Preservar metadata/roles explícitos.
5. Não copiar constantes de CombatLayoutProfile/MapLayoutProfile para várias cenas. Vincular os recursos existentes às composições.
6. Um Container controla a posição de seus filhos. Ajustes pelo editor devem usar tamanho mínimo, flags, alinhamento, gap e margens; não prometer posicionamento livre onde há layout gerenciado.
7. O estilo corrente deve ser o padrão do componente, do jogo e do preview. Fixtures de contraste/troca de perfil não entram como default de produção.

### Estrutura estática e coleções

- Labels e botões fixos são nodes da cena que os contém; não é necessário criar uma cena para cada Label isolado.
- Linhas repetidas e itens com comportamento próprio usam templates compartilhados: slot de inscrição, oferta, linha de regra, nó de mapa, opção de diálogo etc.
- Quantidades variáveis de cartas, atores, estados, ofertas e branches permanecem dinâmicas. Instanciar PackedScenes e reconciliar por identidade estável, sem reconstruir toda a tela.
- APIs nativas como ItemList.add_item e Tree.create_item continuam válidas; esses itens não são árvores de Control que precisam de .tscn próprio.
- Shaders, desenho de grafos e efeitos procedurais permanecem em nodes/scripts apropriados. Não converter algoritmos de desenho em centenas de nodes estáticos.
- Ghosts, cartas em trânsito e histórico reutilizam CardView/ActorPanel e os executores atuais; não ganham uma segunda implementação visual.

## 4. Organização proposta

Preservar os caminhos dos componentes já migrados. Novas pastas organizam cenas sem exigir reorganização simultânea de todos os scripts:

    scenes/ui/
      application_shell.tscn
      screens/
        main_menu.tscn
        settings.tscn
        combat_screen.tscn
        map_screen.tscn
        activity_screen.tscn
        dialogue_screen.tscn
        unified_loadout.tscn
        timeline_screen.tscn
        run_history.tscn
        run_replay.tscn
        sandbox_screen.tscn
        codex_screen.tscn
      overlays/
        pause_menu.tscn
        confirmation_dialog.tscn
        inspection_dialog.tscn
        card_zone_dialog.tscn
        card_zone_tools.tscn
        conversation_history.tscn
        toast.tscn
      components/
        card_view.tscn, actor_panel.tscn, resource_view.tscn, status_badge.tscn
        combat/, map/, settings/, dialogue/, loadout/, activities/, replay/
      workbenches/
        ui_catalog_workbench.tscn
        screen_catalog_workbench.tscn

Nomes de componentes adicionais são definidos nas etapas. Os nomes de novos caminhos são alvos de organização, não uma API pública imutável. Atualizar referências/UIDs ao mover arquivos e remover caminhos obsoletos após a substituição.

O registro de cenas proposto contém somente a associação rota → PackedScene, adotando os mesmos IDs de navegação atuais. main.gd continua controlando a navegação e seu contexto. Não criar um segundo router ou estado global de tela.

## 5. Contrato de montagem e ciclo de vida

- Toda tela migrada é instanciada pela sua PackedScene, inclusive testes, previews e diálogos que a consomem.
- Preservar a assinatura pública de setup/mount quando possível. Muitos consumidores chamam setup antes de add_child; @onready ainda não estará disponível nesse momento.
- Usar binding explícito e idempotente de nodes existentes quando setup puder preceder _ready. Alternativamente, separar captura de dependências e ativação somente onde necessário, migrando todos os consumidores da tela no mesmo commit.
- Conectar sinais uma única vez. Handlers de itens retidos consultam a identidade/modelo atuais; não capturam dados de uma revisão anterior.
- Não iniciar leituras/comandos apenas porque um @tool foi carregado no editor. Preview não recebe gateways nem controllers de gameplay.
- Preservar guards de árvore, viewport, epochs, versões, sincronização, comandos pendentes e cancelamento de respostas tardias.
- dispose/_exit_tree desconectam contexto e sessão, cancelam callbacks/tweens pertencentes à view e restauram foco quando aplicável. Não interromper trabalho da engine indevidamente.
- Refresh atualiza conteúdo, interação e coleções; não reinstancia o shell da tela, reseta scroll, rouba foco ou reaplica animação de entrada.
- Estados locais são visuais: aba, scroll, seleção, posição da câmera, inspeção e leitura gradual. Não restaurar estado de uma run/personagem/setting em outro contexto.

## 6. Estratégia de preview

A cena de produção é a única definição visual. O laboratório apenas instancia essa mesma cena e injeta fixtures de apresentação; não mantém uma cópia desenhada para preview.

Para telas dependentes de sessão/controllers, criar um harness de preview que injeta dados e um receptor de intenções sem efeitos. Não executar o startup real com um sinalizador que permita cair acidentalmente no fluxo da API. Componentes já independentes podem manter seus preview_* no Inspector.

O harness deve permitir testar idioma, escala, contraste, resolução e estados relevantes, sem gravar preferências, bookmarks, saves ou conteúdo. Dados de exemplo são cópias de read models, não um simulador local das regras.

F6 deve funcionar para componentes autônomos e para os harnesses das telas. Se uma raiz de produção exige contexto de run, documentar seu harness em vez de inserir personagem/run fictícios na tela real. Coleções vazias em produção nunca recebem itens de amostra automaticamente.

## 7. Sequência de implementação

Cada etapa abaixo deve ter commit próprio, incluir seus consumidores e só avançar após as verificações relacionadas. Uma etapa maior pode usar commits menores coerentes; nenhum componente migrado permanece com duas implementações selecionadas por fallback/feature flag.

### Etapa 0 — Baseline reproduzível e escopo de arquivos

- Registrar rotas, consumidores, hierarquias, geometria global, focus order, configurações e políticas de movimento atuais.
- Inventariar também cenas acessíveis por F6, atalhos e chamadas indiretas: run_loadout.tscn/RunLoadoutScreen, RunDeckCompartments, HandOverviewDialog e views históricas. Não supor que uma tela fora do menu está morta; verificar seus consumidores antes de consolidar/remover.
- Capturar screenshots e retângulos dos elementos-chave em estados representativos. Guardar métricas de movimento com as durações configuradas, incluindo zero/reduced motion.
- Separar alterações pendentes do workspace antes de produzir os commits. Não incluir automaticamente arquivos de outras tarefas.
- Resolver ou registrar em commits próprios as dependências de apresentação/testes ainda não versionadas apontadas na auditoria. Exigir um checkout limpo capaz de carregar as telas; não usar apenas o workspace local como prova de entrega.
- Fixar fixtures de conteúdo e versões para comparação, sem copiar saves pessoais para testes.

**Aceite:** baseline visual/funcional registrada e roteiro reproduzível em cópia isolada. Nenhuma regra, dado da run ou UI é alterada nesta etapa.

### Etapa 1 — Contratos, registro de cenas e preview seguro

- Definir convenções de nomes únicos, nós obrigatórios, binding antes/depois de _ready, sinais e descarte, aproveitando screen_view.gd.
- Adicionar um registro simples de PackedScenes. Migrar as entradas conforme suas telas forem entregues; não apontar para cenas inexistentes nem criar fallback procedural para uma tela já convertida.
- Preparar ScreenCatalogWorkbench e fixtures de apresentação com intenção capturada, sem gateways.
- Criar verificações de estrutura obrigatória, identidade de nodes e atualização de contexto. Preservar os contratos públicos atuais; evitar uma abstração de UI genérica desnecessária.

**Aceite:** uma tela piloto pode ser montada antes/depois de entrar na árvore, receber dados e ser descartada sem duplicar sinais ou chamar a API pelo preview.

### Etapa 2 — Overlays e diálogos compartilhados

- Criar ConfirmationDialog, InspectionDialog, ConversationHistory e Toast como cenas completas, com hosts explícitos para conteúdo variável.
- Reutilizar nodes nativos de diálogo quando já usados, mantendo modalidade, tamanho, foco, Escape e ativação por teclado/controller.
- Preservar a distinção entre tooltip passivo, inspeção persistente e confirmação. Conteúdo variável entra por templates/slots; não reconstruir o modal inteiro.
- Migrar os consumidores desses shells progressivamente nas etapas seguintes, sem introduzir uma API genérica que esconda a estrutura no editor.

**Aceite:** modal cobre/bloqueia a área correta; fechar restaura foco; sinais não acumulam ao reabrir; tooltip não oferece scroll inacessível.

### Etapa 3 — Detalhes, inspeção e coleções de cartas

- Converter CardDetailsPanel em cena com seções estáticas para glossário, cálculo, mudanças, condições e comportamento. Linhas variáveis usam templates próprios.
- Converter CardCollectionView, CardZoneDialog e CardZoneToolDialog. Preservar grade/scroll, identificação da zona e visibilidade permitida pela engine.
- Usar CardView em todas as cartas, inclusive comparações, seleção, recompensas e inventários. Manter preview de ação separado da face e regras textuais atuais.
- Migrar callers e preservar seleção por identidade, conteúdo fora de ordem/oculto, atualização imediata do tooltip e cálculo apenas dos dados publicados.

**Aceite:** a mesma carta tem anatomia equivalente em mão, coleção, escolha, forge e inspeção; nenhuma lista volta a exibir somente nomes como substituto da coleção visual atual.

### Etapa 4 — Painel de atributos compartilhado

- Converter CharacterAttributesView em cena reutilizável, com templates de linhas/grupos e seções configuradas pelo modelo atual.
- Criar a cena do CombatAttributesDrawer e reaproveitar o mesmo painel dentro do loadout.
- Preservar abertura pela borda esquerda/atalho I, animação suave, foco/modalidade e comportamento responsivo atuais, sem deslocar o combate.
- Preservar origem dos números e escalas publicadas. Não recalcular armor, crítico, resource máximo ou buffs no script da view.

**Aceite:** edição do componente de atributos reflete em loadout e combate; snapshots recebidos permanecem inalterados.

### Etapa 5 — ApplicationShell, pause e notificações

- Completar application_shell.tscn com hosts de tela, cenário, overlays e notificações. Criar pause_menu.tscn com seus controles reais e histórico/abandono nos locais atuais.
- Reusar os shells da etapa 2. Expor referências de layers/hosts no editor, respeitando OverlayLayers e a composição atual.
- Migrar ApplicationShell para binding/preenchimento. Manter comandos de pause/retorno/abandono sob a autoridade atual do router/controller.
- Preservar freeze/resume, bloqueio de input da mão, tooltips e áudio de UI. Nenhuma carta fica acima do menu de pause por reparenting/z_index.

**Aceite:** abrir/fechar pause durante hover, drag, animação e replay mantém estado e restaura interação sem nós órfãos.

### Etapa 6 — Menu principal e seleção de setting

- Criar main_menu.tscn com masthead, seletores, ações, navegação e rodapé atuais.
- Migrar MainMenuView e a rota de main.gd para PackedScene. Popular opções e estados sem remontar a árvore em apply_model.
- Preservar progressão/continuar por setting, estados de conexão, recuperação, histórico, laboratórios e indicação de versão.
- Retirar construção visual fixa de main.gd; ele continua coordenando navegação e sessão.

**Aceite:** troca de setting atualiza apenas os dados e ações corretos; continuar mantém o escopo do setting, sem callbacks da seleção anterior.

### Etapa 7 — Configurações e remapeamento

- Criar settings.tscn com navegação lateral, hosts das páginas, feedback e rodapé atuais.
- Criar páginas estáticas existentes e templates reutilizáveis de toggle, slider, seletor, binding e conexão quando as linhas forem geradas pelo schema.
- Manter SettingsController, SettingsSchema e Preferences como autoridades existentes. Não duplicar o catálogo de opções em outro JSON ou array de view.
- Preservar autosave, categoria/scroll/foco ao modificar valores, resolução ultrawide, monitor, acessibilidade, idiomas, áudio e remapeamento.

**Aceite:** arrastar slider não retorna à aba General; reabrir o jogo recupera valores; tradução/escala não remontam a página durante a interação.

### Etapa 8 — Shell do loadout e navegação de módulos

- Criar unified_loadout.tscn e LoadoutPage como composições completas: header, tabs, host de módulos, feedback de autosave, atributos e recuperação.
- Preservar ModuleCatalog como catálogo de IDs/labels; tabs variáveis usam template, sem replicar o catálogo na cena.
- Migrar UnifiedLoadoutScreen e os consumidores do laboratório. A cena de produção usa apenas personagem/setting/run atuais; fixtures permanecem fora dela.
- Consolidar as entradas de RunLoadoutScreen/run_loadout.tscn no mesmo componente visual atual, preservando os usos ainda necessários. Não manter uma tela antiga separada apenas para satisfazer um teste; atualizar o teste para o caminho canônico.
- Preservar abas e foco local, sem restabelecer botões de preview/refresh que já foram removidos do fluxo.

**Aceite:** navegação e feedback de salvamento continuam iguais; o shell não cria uma segunda autoridade de draft ou persistência.

### Etapa 9 — Inscrições, slots e inventário

- Converter LoadoutView, InscriptionTile, slots e inventário em cenas compartilhadas.
- Preservar contraste de slot vazio/equipado, drag/drop, limites, filtro, comparação, limpar draft e autosave vigente.
- Manter controllers/workspace/gateway de inscrições, guards de versão e escopo de inventário. As inscrições continuam separadas de cartas, modificadores e relíquias.
- Usar os mesmos templates no laboratório e na run, mudando apenas os modelos/adapters de aplicação.

**Aceite:** equipar, substituir, remover e recuperar de erro não perdem foco/estado nem enviam transações duplicadas.

### Etapa 10 — Deck, compartimentos e demais módulos do loadout

- Converter DeckModule, DeckCompartmentEditor, resumos, relíquias e atributos detalhados; usar CardView nos itens e templates de compartimento/alocação.
- Preservar separação entre compartimentos de construção e zonas de compra/mão/descarte. A view não interpreta componentes herdados nem decide compatibilidade de gameplay.
- Preservar autosave em todas as abas editáveis e a confirmação/recuperação de transações atuais.
- Criar apenas a UI planejada de companions que já existe; não implementar uma mecânica nova nesta migração.

**Aceite:** trocar de aba/run/personagem/setting não mistura drafts, inventários ou progressão; reabrir exibe o que foi realmente equipado/alocado.

### Etapa 11 — Shell e controles fixos do combate

- Criar combat_screen.tscn e completar a composição atual com HeaderView, CardZoneControls, TurnControls, ActionPreview, alternativas, recuperação e referências do HUD.
- Converter esses componentes em cenas editáveis, preservando seus sinais, textos, ícones e posição.
- Converter a raiz estável de HandView em cena, mantendo HandLayout/HandInteraction/HandHitSurface/HandArrangement como comportamentos. Cartas continuam instâncias de CardView.
- Preservar o fluxo de seleção, mouse/controller, drag-to-play sempre disponível e reorganização visual; não mover regras para a mão.

**Aceite:** layout e fan idênticos de 0 a muitas cartas; clique escolhe a carta correta; nenhuma seleção automática extra; sem texto vertical ou mudança temporária de fonte.

### Etapa 12 — Battlefield, cenário e hosts de animação

- Converter BattlefieldView, CombatSceneryView, FloatingHUD e hosts de feedback/efeitos em cenas com referências estáveis.
- Instanciar ActorPanel para a quantidade real de participantes; preservar retratos e anchors usados pelos executores. Cenário permanece separado de HUD/input.
- Reusar os executores atuais de compra, uso, burn, impacto, retarget e reorder. Não trocar por AnimationPlayer genérico se isso mudar trajetória, temporização ou cancelamento.
- Preservar identidade dos anchors durante atualização e redimensionamento; coordenadas devem considerar CanvasLayer, transformação e escala. Não reiniciar animação em refresh.
- Zero duração/ausência de efeito não adiciona espera; manter reduced motion e configurações capturadas para animações em andamento.

**Aceite:** laboratório e combate têm o mesmo resultado para a mesma animação/configuração; pause, resize, cancelamento e saída de tela não deixam ghost/contador/outline presos.

### Etapa 13 — Mapa, nós e inspector flutuante

- Criar map_screen.tscn com header, workspace, canvas, scroll e inspector flutuante atuais.
- Criar MapNodeView, MapInspector e templates de detalhes, mantendo MapNodeGlyph/desenho de conexões como nodes procedurais.
- Preservar RouteView/navigation model, MapLayoutProfile e MapCamera. O JSON da engine continua determinando nós, conexões e disponibilidade.
- Manter pan/zoom, centralização do nó atual, lerp existente, seleção, conclusão e volta das atividades. O inspector não participa do Container que determina o tamanho do mapa.

**Aceite:** sair do primeiro combate não perde topologia, o nó atual concluído mostra ambos os estados e abrir detalhes não desloca os demais elementos.

### Etapa 14 — Atividades, ofertas, lojas, recompensas e forge

- Converter ActivityScreen/JourneyView/ActivityOptionsView em shell e templates de opção/oferta, reutilizando CardView e os diálogos compartilhados.
- Converter confirmação de upgrade/comparação antes/depois, inventário de modificadores, compras e escolhas de recompensa sem layouts paralelos por caller.
- Incluir estados de fim de combate/run e mensagens de resultado atualmente exibidos pelas views de jornada/atividade, sem criar regras de avanço ou recompensa no cliente.
- Preservar exibição do efeito da carta e do item modificador, preço, indisponibilidade, recusa e regras de avanço automático existentes.
- Consequências e opções vêm da engine; a cena não decide uma regra por conhecer o tipo de atividade.

**Aceite:** entrar/concluir/voltar ao mapa mantém a mesma sequência de comandos, estado e conclusão automática atual, incluindo falhas/retentativas.

### Etapa 15 — Diálogo RPG/visual novel

- Completar DialogueScene/DialoguePanel com retrato, identidade do falante, texto, opções, feedback e controles atuais; criar ChoiceItem.
- Reusar DialogueController e DialogueReading para revelação gradual/acessibilidade, avanço e estado de leitura.
- Separar o conteúdo visual do diálogo do efeito da escolha e manter o histórico acessível no pause, via componente compartilhado.
- Preservar fluxo mapa → diálogo → escolha/conclusão → mapa, cancelamento e retomada; nenhum @tool deve iniciar atividade ou escolher opção.

**Aceite:** troca de linha, locale, pause e retomada não repetem comandos nem perdem a posição de leitura; a view não sabe a consequência da opção.

### Etapa 16 — Histórico e timeline

- Converter RunHistoryScreen e TimelineScreen com listas/árvore, detalhes, scrubber, filtros e ferramentas existentes.
- Usar Tree/ItemList nativos para dados; criar cenas próprias apenas para seções e itens que de fato sejam Controls customizados.
- Preservar escopo por setting, seleção estável, estados de carregamento, paginação, capacidades de branch/cheat do modo e permissões atuais.
- Separar estado local do inspector da seleção autoritativa de branch/run. Um refresh visual não executa navegação na timeline.

**Aceite:** abrir histórico/timeline, trocar seleção e voltar ao jogo mantém run/branch corretas, sem perder sincronização ou refazer leituras desnecessárias.

### Etapa 17 — Replay e inspeção de eventos

- Converter RunReplayScreen/View, HistoricalCombatView, HistoricalBeatView e GameplayEventView, compartilhando controles de reprodução e atores/cartas reais.
- Preservar carregamento progressivo, scrub, epochs, cancelamento, verificação e separação entre snapshot histórico e sessão jogável.
- Manter a mesma fila/executor de apresentação nos lugares onde já são compartilhados. Um replay não ganha uma simulação local de combate.
- O inspetor causal reutilizado pela timeline e laboratório de eventos recebe sua própria cena, sem duplicar a formatação de eventos.

**Aceite:** sair durante carregamento/reprodução não aplica resposta tardia; replay terminado não altera run ativa nem grava preferências/saves de gameplay.

### Etapa 18 — Sandbox, codex e ferramentas auxiliares

- Converter SandboxScreen, CodexScreen e editores/inspectores visuais auxiliares ainda usados por essas rotas.
- Criar templates para seletores de modo, cenários, filtros e ações. Preservar estados/capacidades publicados e o catálogo atual.
- Preservar uso REST para operações de gameplay; o fato de estar num laboratório não autoriza mutação local do estado.
- Não aproveitar a migração para adicionar ferramentas ou regras novas.

**Aceite:** cada rota aberta pelo menu tem uma cena visual definida e usa os mesmos componentes das telas de produção.

### Etapa 19 — Laboratórios e catálogo completo

- Converter as estruturas visuais restantes de CardHandLab, InscriptionLoadoutLab, UnifiedLoadoutLab, DeckCompartmentLab e GameplayEventLab; reutilizar as views já migradas.
- Colocar controles próprios do laboratório em shells editáveis, separados dos componentes de produção.
- Completar ScreenCatalogWorkbench com estados vazio, cheio, bloqueado, erro, modal e loading das telas migradas.
- Diferenciar explicitamente laboratório visual offline e laboratório que cria uma run dedicada via API. Abrir um preview offline não deve iniciar o fluxo online.

**Aceite:** mudar um componente compartilhado no editor aparece no laboratório e no jogo, sem cópia visual ou defaults antigos.

### Etapa 20 — Limpeza, certificação e documentação final

- Concluir o registro de rotas e substituir Script.new() nos consumidores das views migradas por PackedScene. Remover construtores alternativos de estrutura estática.
- Remover helpers de construção visual que não tenham mais consumidores legítimos; ViewStyleFactory permanece responsável por estilos, não por esconder uma segunda árvore de tela.
- Auditar carregamentos por path/UID, scenes de exemplo, testes e código morto. Não remover genericamente todo .new(): RefCounted, controllers, recursos e algoritmos continuam usando construtores.
- Executar a matriz abaixo em checkout limpo e cópia isolada, além do fluxo com API real em dados próprios de teste.
- Atualizar README do showcase, auditoria e guia de edição com cena, script, componentes, perfil, propriedade de métricas e harness de cada tela.

**Aceite:** todas as rotas/overlays inventariados usam cenas completas; não há montagem alternativa da estrutura estática; visual e comportamento foram comparados com a baseline.

## 8. Matriz de validação

| Grupo | Verificações obrigatórias |
| --- | --- |
| Montagem | Instanciar a cena real; bind antes/depois da árvore; refresh repetido; dispose; ausência de sinais duplicados/órfãos |
| Edição | Alterar gap, margem e estilo de um componente no fixture e confirmar preservação nas atualizações previstas; políticas explicitamente dirigidas continuam funcionando |
| Geometria | Retângulos globais, alinhamento e ordem de draw/hit testing; componentes flutuantes não deslocam a composição |
| Resolução | 1280×720, 1920×1080, 2560×1080, 2560×1440; janela/fullscreen e resize durante overlays/animações |
| Acessibilidade | EN/PT-BR, escalas de texto 1.0/1.2/1.6, alto contraste, teclado/controller e reduced motion; casos de título longo |
| Dados variáveis | 0/1/2/5/12 cartas e stress da mão; vários atores, recursos desconhecidos, ausência de limites, muitos statuses/ofertas, grafo maior |
| Animação | Compra/uso/exile, hover, drag, reorder, impacto, cancelamento e pause; duração normal, zero e velocidade configurada |
| Áudio | Mesmos eventos e volumes; hover toca uma vez por entrada, refresh não repete som e preview não grava configurações |
| Sessão | Loading, falha HTTP, reconnect, comando pendente, resposta tardia, troca de setting/run/personagem/branch |
| Autoridade | Mesmas intenções, payloads e resultados para o mesmo roteiro; nenhuma regra, disponibilidade ou consequência derivada pela view |
| Imutabilidade | Read models de entrada preservados, sem escrever campos de UI no snapshot; recursos compartilhados não recebem override de instância |
| Preview | Não inicia servidor/API, não envia comandos e não escreve preferências, save ou bookmark; dados de amostra não invadem produção |

Não executar o produto cartesiano de todas as combinações a cada commit. Usar smoke curto por etapa, combinações de maior risco e certificação completa nos marcos e ao final. Capturas não devem congelar tempos de animação artificiais: comparar frames/poses equivalentes e registrar tolerâncias visuais justificadas.

### Testes existentes a preservar e ampliar

- Base visual: editable_ui_nodes, scene_preview_profiles, card_style_canonical, workbench_smoke, theme_contracts, shared_canvas_theme, component_context, run_visual_contexts e ui_reconciliation.
- Menus/configuração/modalidade: ivory_menus, settings_contracts, settings_interaction, settings_recovery, audio_contracts, pause_layers e ui_lifecycle.
- Cartas/combate: card_presentation, card_titles, card_refresh_stability, card_cost_layout, hand_selection, card_draw_pose, card_transit_counter, turn_hand_cycle, animation_contracts e combat_collaborators.
- Mapa: map_graph_view, map_geometry, map_selection, map_topology_refresh, map_completion_status, map_camera_motion, first_combat_map_transition e map_rest_flow.
- Atividades/diálogo: activity_reconciliation, activity_ingress, activity_map_completion, dialogue_contracts, dialogue_coordination e dialogue_reading.
- Loadout: unified_loadout_shell/layout/autosave/recovery/online, loadout_input, inscription_rest_workspace, run_loadout e deck_compartments.
- Histórico/replay/eventos: replay_lifecycle, replay_progressive, replay_transport, replay_online e gameplay_event_presentation.

Usar os nomes reais dos arquivos e sua disponibilidade versionada na etapa 0. Não converter testes de comportamento em testes que apenas procuram nomes de nodes. Adicionar regressões funcionais para bugs encontrados pela migração.

### Fluxo integrado com API real

1. Abrir o jogo, selecionar cada setting de teste, iniciar/continuar sua própria run.
2. Abrir configurações/pause; alterar apenas preferências de apresentação; confirmar que o contexto jogável permanece intacto.
3. Entrar no mapa, inspecionar e selecionar nó, concluir atividade, voltar e verificar status/centralização.
4. Abrir loadout, equipar/remover inscrição e alocar carta em compartimento quando permitido; aguardar autosave e reabrir.
5. No combate, comprar, selecionar, arrastar, reorganizar visualmente, usar, exilar, encerrar turno e abrir pilhas/atributos.
6. Cobrir diálogo, recompensa, loja e forge disponíveis nas fixtures, sem inventar ações ausentes no modo.
7. Encerrar run, abrir histórico/replay/timeline quando permitido e voltar ao menu.
8. Repetir um roteiro de comandos fixo com a mesma seed, conteúdo e configuração antes/depois; comparar estado, journal e hashes autoritativos usando os contratos existentes.

Todos os writes usam runs e diretórios de teste dedicados. Preview/hover/locale/resize não enviam comandos. O baseline de engine e revisão de conteúdo devem ser os mesmos; não comparar execuções com conteúdo diferente ou considerar IDs/telemetria local como estado de gameplay.

## 9. Marcos e critério de conclusão

- Marco A, até etapa 7: infraestrutura, inspeção compartilhada e menus completos editáveis.
- Marco B, até etapa 12: loadout e combate completos usando as cenas, sem trocar layout/executores.
- Marco C, até etapa 17: mapa, atividades, diálogo e histórico/replay centralizados.
- Marco D, até etapa 20: ferramentas, laboratórios, limpeza, documentação e certificação em checkout limpo/API real.

A migração só está concluída quando a árvore estática de cada tela puder ser inspecionada/editada na Godot e o runtime instanciar essa mesma árvore. Ter um arquivo .tscn, um preview parecido ou um perfil de cor compartilhado não é evidência suficiente.

O primeiro passo de implementação é a etapa 0, seguido dos contratos/preview. Não começar pelo shell de combate isoladamente: diálogos, inspeção e atributos compartilhados devem estar disponíveis antes de compor as telas que dependem deles.
