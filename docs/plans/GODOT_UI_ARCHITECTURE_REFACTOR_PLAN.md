# Reestruturação da interface Godot para estilos substituíveis

Data: 2026-10-11.

Status: plano; sem implementação de código nesta etapa.

Escopo: cliente em `examples/godot-engine-showcase/`. Complementa o [plano visual Marfim](GODOT_IVORY_VISUAL_REDESIGN_PLAN.md), mas não depende dessa paleta. A arquitetura deve permitir outras direções visuais no futuro sem refazer integração, seleção, mão ou playback.

## 1. Resultado esperado

Uma mudança visual poderá ocorrer em três níveis independentes:

| Mudança | Onde editar | O que não precisa mudar |
| --- | --- | --- |
| Cores, fontes, bordas, ícones e feedback visual | Perfil de tema e recursos de estilo. | Controllers, presenters, REST e comandos. |
| Posições, agrupamentos, proporções e áreas flutuantes | Cena/layout da tela e seus componentes. | Regras de input, candidatos publicados e estado da sessão. |
| Ilustrações, retratos, cenário e placeholders | Manifesto de artes e slots. | Anatomia funcional, tradução e hitboxes. |

Uma nova direção artística não deve exigir procurar cores em dezenas de scripts, duplicar uma tela de combate ou copiar a lógica de selecionar/jogar cartas.

A reestruturação será incremental. Primeiro manter a aparência e comportamento atuais; depois aplicar Marfim sobre as novas fronteiras. Não reconstruir a engine nem introduzir um framework genérico de frontend.

## 2. Diagnóstico que orienta o plano

- `AppTheme` fornece um tema comum, mas muitas telas possuem cores, fontes e estilos locais; histórico/replay também usam cores em BBCode.
- O inventário da base registra 83 construções `Color(...)` e 148 chamadas de override na pasta de UI. São pontos de triagem, não 231 defeitos.
- `CombatScreen` concentra aproximadamente 966 linhas: composição, seleção, input, preview, sidebar, atualização e feedback de playback.
- A UI chama métodos internos do `CombatPresenter`, como `_targets`, `_preview` e `_intent_for`, sem contrato público claro.
- A instância de `ActivityScreen` permanece, mas seus filhos são recriados a cada refresh. O combate preserva a mão, porém reconstrói grande parte da estrutura.
- Vários componentes acessam diretamente `Preferences`, `I18n` ou `Playback`, dificultando seu uso isolado.
- A interface é majoritariamente montada em código. Isso é válido, mas dificulta ajustes visuais pelo editor e favorece dimensões repetidas em scripts.
- A aplicação recursiva de preferências armazena fontes e estilos-base nos nodes. Um tema substituível precisa impedir que esses caches restaurem estilos de outro perfil.

Pontos já sólidos a conservar: fronteira HTTP, sessão injetável, presenters passivos, slots de arte, estilos especializados de carta/mão, identidade das cartas, hitboxes estáveis, preview separado, proteção de pause e testes de regressão.

Base existente: [capturas, métricas e inventário](../design/interface-baseline/2026-10-11/README.md), commits `d0d42cb` e `0566e8e`. Não repetir essa preparação nem misturar suas alterações com a refatoração.

## 3. Decisões de arquitetura

### 3.1. Separar controle, apresentação e desenho

```text
Engine REST → EngineGateway → GameSession
                                  │ snapshots / ações publicadas
                                  ▼
                             Presenters
                                  │ modelos de apresentação
                                  ▼
Input da View → ScreenController → View / componentes
                     │                 ↑
                     │          tema + layout + artes
                     ▼
              casos de uso de GameSession

Receipts → Playback existente → feedback visual da View
```

O desenho não conhece transporte. O controller coordena intenção e ciclo local de interação, não decide regras de jogo. O presenter transforma fatos em informações exibíveis; não conhece nodes, tema ou HTTP.

Esse arranjo é uma separação pragmática de responsabilidades, não uma implementação obrigatória de MVVM com binding bidirecional.

### 3.2. Cada estado tem uma única autoridade

| Estado | Dono | Restrições |
| --- | --- | --- |
| Run, combate, ações legais e versões | Engine / `GameSession`. | Nunca persistir cópia autoritativa em componente visual. |
| Dados exibíveis de um snapshot | Presenter/modelos de apresentação. | Cópias defensivas; não reavaliar regras ou fórmulas. |
| Carta/alvo selecionados, hover, foco e scroll | Estado local do controller/view, conforme seu alcance. | Não são dados de run; limpar/revalidar por contexto. |
| Gestos e geometria da mão | `HandInteraction` / `HandLayout` existentes. | Não criar um segundo sistema de drag ou seleção por índice. |
| Cursor de reprodução | `Playback` existente. | O tema não avança, reinicia ou reconstrói frames. |
| Preferências persistentes | `Preferences`. | Componentes recebem a projeção necessária; não salvam preferências diretamente. |
| Tema/layout/artes ativos | Composição visual do cliente. | Não entram em comandos, RNG, revisão de conteúdo de regras ou identidade determinística. |

O controller guarda a seleção de gameplay; o destaque de hover da mão permanece transitório. Não manter dois `selected_card` independentes que possam divergir.

### 3.3. Contratos públicos, não dependência da árvore

Contrato mínimo proposto para uma view de tela:

- `mount(context)`: recebe tema e dependências visuais explicitamente.
- `apply_model(model)`: atualiza dados exibidos, sem executar ações.
- `apply_interaction(state)`: aplica seleção, bloqueio e estados locais.
- `apply_visual_profile(profile)`: reaplica aparência, sem consultar a engine.
- `capture_local_state()` / `restore_local_state(state)`: somente quando a composição for substituída; restaurar por identidade semântica.
- `dispose()`: encerra tweens, conexões e trabalho diferido pertencentes à view.

As views emitem sinais tipados como `card_selected`, `target_selected`, `action_requested`, `inspection_requested` e `sidebar_toggled`. O controller ou ancestral conecta as intenções. A API não expõe `find_child()` como forma normal de executar funcionalidades.

GDScript pode usar classes-base pequenas e testes de contrato; não construir interfaces abstratas extensas para todos os controles da Godot. Nomes acima são propostas a validar na primeira extração.

### 3.4. Modelos de apresentação explícitos e defensivos

Começar pelos contratos mais reutilizados: carta, ator, recurso, opção de ação e estado da tela de combate.

- Usar tipos/enums e validação nos campos estáveis da apresentação.
- Conservar IDs canônicos, seções opcionais e coleções para recursos/tags/componentes desconhecidos.
- Dados extensíveis do contrato REST podem continuar em dictionaries na borda; não criar uma classe para cada formato possível da engine.
- Nomes/textos podem chegar localizados pelo presenter; mudanças de idioma regeneram a projeção textual, não iniciam nova run.
- Modelos não contêm nodes, tweens, callbacks de envio, referências mutáveis à sessão ou cores de um tema específico.
- Separar `CardViewModel` do preview de uma ação/alvo e do conteúdo técnico de inspeção.
- Explicar ausência de dados: unknown, carregando, indisponível e não aplicável não são o mesmo estado.

Imutabilidade aqui significa contrato de leitura e cópias defensivas. Um `RefCounted` ou `Dictionary` não se torna profundamente imutável apenas por receber o nome `ViewModel`; os testes devem provar que view e controller não alteram os dados recebidos.

Uma opção de ação pode usar uma referência local opaca para localizar o candidato no snapshot atual. Essa referência não é um novo ID de comando da engine e não pode sobreviver arbitrariamente a mudanças de versão/contexto. Pagamentos alternativos e combinações de alvos continuam separados; não usar “primeiro candidato” como seleção implícita.

### 3.5. Estilo por papéis semânticos

Introduzir `VisualThemeProfile`, construindo um `Theme` da Godot e referenciando os estilos especializados existentes.

Papéis iniciais:

- Superfícies: canvas, painel, painel elevado, superfície invertida e scrim modal.
- Texto: principal, secundário, invertido, desabilitado e diagnóstico.
- Ações: primária, secundária, destrutiva, foco, selecionada e indisponível.
- Tipografia: título de tela, seção, corpo, legenda e número.
- Espaçamento: escala curta e margens do shell/componentes.
- Estados: hover, pressed, disabled, focus e selected, com geometria estável.

Usar variações de tipo do `Theme`, por exemplo `PrimaryAction`, `SecondaryAction`, `DangerAction`, `ScreenTitle` e `Caption`. O botão não define uma cor arbitrária ao ser criado.

`AppTheme` permanece inicialmente como fachada de migração. Ao terminar, seus helpers usam o perfil e papéis comuns; eliminar constantes antigas e caches que deixarem de ter consumidores. Não manter dois sistemas de tema em paralelo permanentemente.

Donos únicos:

- `CardVisualStyle`: política numérica e raridade.
- `HandVisualStyle`: geometria e parâmetros específicos de interação/animação da mão.
- Perfil global: superfícies, tipografia comum e estilos de controles.
- Manifesto de artes: associação visual de IDs.
- Perfil de layout: proporções, agrupamentos e limites da composição, não regras de jogo.

Cada propriedade deve ter uma fonte. Overrides especializados são explícitos, não cópias de defaults espalhadas. Estilos/temas compartilhados não podem ser mutados quando um componente recebe hover ou seleção.

### 3.6. Layout substituível sem duplicar comportamento

Criar cenas `.tscn` para o shell, a composição de combate e os componentes que se beneficiam de edição visual. Manter geração dinâmica para listas e conteúdo variável.

Proposta inicial de composição do combate:

- `CombatHeaderView`.
- `BattlefieldView`, com atores reconciliados por ID.
- `CardHandView` existente.
- `ActionPreviewView`.
- `TurnControlsView`.
- `CharacterSidebarView`.
- View de opções de pagamento/ação.

Esses nomes descrevem responsabilidades; não exigem sete novas classes complexas. Uma extração pode ser um pequeno script/cena com um contrato de dados.

Um `CombatLayoutProfile` define margens, respiro, alinhamentos e proporções. Uma cena alternativa altera agrupamentos usando os mesmos componentes e controller. Não implementar uma linguagem JSON genérica de layout, editor de skins, descoberta automática de plugins ou injeção de scripts arbitrários.

Containers cuidam dos seus filhos; posições manuais ficam nas áreas livres e nos overlays apropriados. Não posicionar manualmente um filho e simultaneamente entregar sua posição a um container.

### 3.7. Dependências explícitas e curtas

O shell monta o contexto visual. Componentes básicos recebem modelo, estilo, escala e política de movimento necessários, preferencialmente já resolvidos.

- Controller recebe sessão e playback por injeção.
- Tradução e resolução de arte são dependências delimitadas, não acesso irrestrito a todos os serviços.
- Router é usado pela coordenação de telas; cartas/atores não recebem o router inteiro.
- UI de configurações emite mudanças; o contexto proprietário aplica/persiste as preferências.
- Contexto visual não contém gateway/transporte, URL, estado de run mutável ou objetos de comando.
- Não substituir vários autoloads por um `ServiceLocator` genérico que apenas esconda as mesmas dependências.

Os autoloads atuais continuam como pontos de composição onde fazem sentido. Não movê-los por estética arquitetural.

### 3.8. Atualização incremental e invalidação

Criar a estrutura estável uma vez e reconciliar conteúdo por identidade:

- Atores por `instanceId`; cartas por `cardInstanceId` e escopo; opções por referência contextual; nós da jornada por `nodeId`.
- Atualizar labels, barras, modelos e disponibilidade sem substituir nodes não afetados.
- Separar mudanças de dados, idioma, escala, tema e topologia da atividade.
- Evitar `JSON.stringify()` do conjunto completo de snapshots como único detector de alteração. Usar versão/contexto e chaves pequenas por componente, acompanhando também inspeções e mudanças locais de apresentação que não alteram a sequência da run.
- Não atualizar a sessão por um timer de UI para manter o desenho vivo.
- Coalescer layout/foco para uma passagem por atualização efetiva; evitar recalcular o grafo de foco a cada callback independente.
- Manter epochs/tickets para descartar inspeções, histórico e outras respostas de contexto antigo.

Troca real de tipo de atividade pode substituir o painel correspondente. Não exigir retenção de todos os nodes quando a estrutura realmente mudou; preservar apenas o que mantém identidade/contexto.

### 3.9. Tema, modalidade e animações

Colorir não é reconstruir o combate. Ao trocar perfil, reaplicar aparência sem enviar comandos ou reiniciar a fila.

- Mudança geométrica cancela drag/cópias transitórias sem jogar carta; preserva estado válido por identidade.
- Troca de composição durante envio/gesto crítico pode ser adiada até um ponto seguro de apresentação, sem interromper ou reenviar o comando.
- Remoção de componente cancela seu tween e callbacks; um node novo com o mesmo ID não recebe conclusão de animação antiga.
- Durações e amplitude são visuais. `Playback` permanece dono da ordem/cursor, não um novo animation manager com outra fila autoritativa.
- Layout não cria cópia interativa de carta; proxies de leitura continuam passivos.

Centralizar modalidade no shell/host de overlays: gameplay, painéis flutuantes, modais e notificações. Usar constantes de camada e contratos de foco/input, não magic numbers distribuídos. Janelas/popups da Godot também precisam de propriedade e modalidade corretas; apenas aumentar `z_index` ou um `CanvasLayer` não resolve toda janela nativa.

### 3.10. Artes e settings

Preservar `ArtCatalog`/`ArtSlot`, permitindo que o perfil forneça explicitamente o manifesto visual. Cachear texturas pelo caminho efetivamente resolvido; invalidar o catálogo quando o manifesto/perfil muda.

- IDs de conteúdo iguais em contextos diferentes não devem selecionar arte de um setting anterior.
- Fontes de apresentação/arte são mapeadas explicitamente no cliente, com base e overrides conhecidos, não caminhos adivinhados a partir de IDs.
- Manter fallback por categoria, indicação de placeholder e nome localizado fora da imagem.
- Trocar perfil visual não troca setting, não altera progressão e não dispara publicação/hotreload de regras.
- Recursos Godot `.tres`/`.tscn` configuram apresentação; JSONs de game design continuam sendo interpretados pela engine. Não são a mesma camada de configuração.

## 4. Organização proposta

Estrutura alvo indicativa, não uma movimentação obrigatória de todos os arquivos em um único commit:

```text
scripts/
  engine/                         # preservado
  application/                    # preservado
  presentation/
    presenters e modelos          # dados de leitura; sem nodes/tema
  ui/
    shell/                        # navegação, overlays, composição visual
    controllers/                  # intenção e estado local por tela
    screens/                      # montagem das views de tela
    components/                   # cartas, mão, atores, recursos, preview
    styles/                       # perfis e recursos visuais
scenes/ui/                        # composições editáveis
data/themes/                      # perfis e recursos compartilhados
assets/                           # manifestos/arte/fontes e suas licenças
tests/                            # contratos, interação, layout e integração
```

Mover arquivos quando a responsabilidade extraída estiver estabilizada. Não iniciar com uma reorganização de pastas que gere grande diff sem reduzir acoplamento.

## 5. Etapas sequenciais de implementação

Cada etapa preserva a demo executável, termina com seus testes e um commit próprio. Nenhum commit ou implementação é executado por este plano.

### Etapa 1 — Contratos públicos e testes de proteção

- Formalizar os modelos básicos e a API pública de `CombatPresenter`.
- Migrar consumidores de `_targets`, `_preview`, `_intent_for` e outros helpers para consultas públicas com cópias defensivas.
- Preservar exatamente candidatos, custos alternativos, tipos de alvo, preview e comparação de valores.
- Definir o contrato mínimo de view/contexto e convenções de IDs semânticos de foco.
- Acrescentar testes para dependências proibidas em componentes/controllers e chamadas externas a métodos internos de presenters.

Verificação: `layers.gd`, `card_presentation.gd`, `card_preview_separation.gd`; modelos não mutam inputs e não carregam estilo, nodes ou comandos executáveis.

Commit sugerido: `refactor(ui): formalize presentation contracts and public queries`.

### Etapa 2 — Extrair o estado e controller de combate

- Retirar de `CombatScreen` seleção, resolução local de opções publicadas, bloqueio de input e coordenação de envio.
- Criar `CombatUiState` e `CombatController` pequenos, com dependências injetadas.
- Mover a enumeração/política de estados de interação para a autoridade de UI, sem alterar regras de turno.
- View emite intenção; controller identifica candidato no snapshot e delega à sessão.
- Limpar seleção após uso conforme comportamento atual; não selecionar a primeira carta automaticamente.
- Preservar recuperação de sincronização, pagamentos alternativos e proteção contra envio duplo.

Verificação: testes de transição sem cena/HTTP; cliques repetidos, alvo inválido no contexto observado, mudança de actor/branch, resposta antiga e comando incerto. `card_play_selection.gd` continua aprovado.

Commit sugerido: `refactor(ui): extract combat interaction controller and local state`.

### Etapa 3 — Componentizar a view de combate

- Extrair cabeçalho, preview, comandos, atores e sidebar da tela.
- Montar uma cena de combate usando os componentes existentes; manter `CardHandView` e seus mecanismos.
- Definir referências explícitas aos componentes, reduzindo buscas frágeis por nomes de nodes.
- Injetar configurações visuais; componentes não acessam sessão/router/playback.
- Não alterar ainda a direção artística.

Verificação: `combat_stage_layout.gd`, `hand_ux.gd`, `pause_layers.gd`, laboratório; mesmos comandos e seleção com a composição atual.

Commit sugerido: `refactor(ui): compose combat from reusable views`.

### Etapa 4 — Atualização por identidade

- Conservar atores, preview, comandos e sidebar quando seu contexto permanece.
- Reconciliar conteúdo e atualizar somente seções afetadas.
- Substituir a chave global de serialização por invalidação explícita de snapshot, inspeção e apresentação.
- Centralizar captura/restauração de foco por ID, sem restaurar seleção inválida.
- Coalescer layout/navegação; registrar criação/destruição e configuração de nodes para teste.

Verificação: mesmo snapshot não cria novos nodes nem novas consultas; atualização de recurso não recria a mão; inspeção assíncrona atualiza a carta sem fonte intermediária. Comparar métricas com a baseline, usando cenários e ambiente equivalentes.

Commit sugerido: `perf(ui): reconcile combat views without rebuilding stable components`.

### Etapa 5 — Perfil de tema e componentes básicos

- Implementar `VisualThemeProfile` com o tema atual como primeiro perfil de preservação.
- Fazer `AppTheme` consumir papéis e variações de controles.
- Consolidar fontes/tamanhos/espaçamentos comuns e estados de botões/inputs.
- Migrar cores locais e BBCode de estilo para renderização/componentes controlados.
- Revisar alto contraste e metadados de base: escala deve partir da fonte canônica, não de um valor já escalado; caches precisam acompanhar o perfil.
- Respeitar os donos de valores numéricos, raridade e estilo da mão.

Verificação: galeria de estados, escala idempotente, tema A→B→A sem resíduos; compartilhamento de recursos não modifica outros componentes por hover.

Commit sugerido: `refactor(ui): centralize semantic themes and control variants`.

### Etapa 6 — Isolar estilos e dependências dos componentes

- Migrar cartas, detalhes, recursos, mão e artes para configuração explícita.
- A view recebe escala, política de movimento e estilo ativo, sem salvar preferências.
- Permitir renderizar esses componentes fora da cena principal com um contexto mínimo.
- Adaptar o catálogo para manifesto visual explícito e invalidação de cache por perfil.
- Manter limites/valores publicados de recursos; uma barra não deve inventar semântica de vida ou esconder valores sem limites adequados. Usar apresentação textual quando uma barra não for aplicável.

Verificação: componente isolado, recurso desconhecido, valores sem comparação, raridade desconhecida, nome longo, arte ausente e troca de manifesto sem arte stale.

Commit sugerido: `refactor(ui): inject component styles preferences and art resolution`.

### Etapa 7 — Shell, overlays e feedback

- Fazer `main.gd` delegar composição de menus/modais e navegação, mantendo um ponto de entrada claro.
- Centralizar propriedade das camadas, pausa, notificações e inspeções.
- Separar feedback visual recebido de playback da lógica de envio/seleção.
- Desconectar eventos/cancelar tweens e requests locais quando o contexto é encerrado.
- Preservar o playback histórico separado, pois seu cursor pertence a outro contexto de consulta.

Verificação: modal acima de hover/proxy/drag, nenhum click-through, foco restaurado, saída durante animação e tema/composição alterados sem avançar cursor ou emitir comando.

Commit sugerido: `refactor(ui): isolate application shell overlays and visual feedback`.

### Etapa 8 — Jornada e atividades

- Extrair coordenação de escolhas para `ActivityController`, mantendo `ActivityPresenter` passivo.
- Conservar mapa, área de ofertas e scroll quando os IDs permanecem.
- Substituir somente o painel específico ao mudar o tipo de atividade.
- Reutilizar cartão de oferta, confirmação e componentes visuais; separar preço de aquisição do custo de uso.
- Preservar avanço automático exclusivamente quando permitido pelas escolhas anunciadas; a skin não define esse fluxo.

Verificação: jornadas lineares/ramificadas, compras repetidas, atualização de recurso, recompensa/recusa, crafting, diálogo e retorno; sem piscar, perder scroll ou duplicar requests.

Commit sugerido: `refactor(ui): separate activity coordination and incremental views`.

### Etapa 9 — Menus, configurações, histórico e ferramentas

- Aplicar os mesmos contratos às telas restantes, sem extrair controller para telas que não precisam de estado/coordenação próprios.
- Separar schema das opções de configuração de sua aparência; setter/persistência pertence à coordenação.
- Remover helpers de botão divergentes quando puderem usar variantes comuns, mantendo tamanho/contexto intencionais.
- Migrar histórico/replay/timeline e combate histórico para componentes compartilhados e perfis.
- Manter a informação técnica completa e as permissões existentes do sandbox/códice.

Verificação: preferências persistentes, input remapeado, troca de setting sem perder isolamento de progressão, replay somente leitura e branches conforme permissões.

Commit sugerido: `refactor(ui): unify menus archive and tools on shared view contracts`.

### Etapa 10 — Prova de substituição visual

- Criar um perfil contrastante de teste, limitado à galeria/laboratório; não precisa de novas artes finais.
- Criar uma segunda composição de teste do combate, por exemplo preview lateral versus inferior.
- Usar os mesmos modelos, controller, mão e fontes de dados nas duas composições.
- Testar troca antes/depois de seleção, sidebar, pausa, mudança de idioma, resize e chegada de resposta assíncrona.
- Comparar intenções e chamadas de sessão para a mesma sequência de input semântico: devem ser equivalentes.
- Remover a antiga montagem concorrente e helpers sem consumidores. Não oferecer um menu público de skins por consequência desta prova.

Verificação: trocar visual exige apenas escolher perfil/cena; zero alterações em gateway, sessão, regras ou controller. As duas views não existem simultaneamente comandando a mesma sessão.

Commit sugerido: `test(ui): prove theme and layout replacement without gameplay changes`.

### Etapa 11 — Certificação e documentação

- Executar regressões offline e integração REST quando a API estiver disponível.
- Atualizar arquitetura, contratos de componentes, slots e guia de como criar outro perfil/layout.
- Registrar capturas do tema preservado e resultados comparáveis à baseline original, sem sobrescrever as referências antigas.
- Investigar avisos de encerramento ObjectDB; distinguir resíduos preexistentes de novos vazamentos da migração. Não declarar ciclo de vida resolvido somente por código de saída zero.
- Auditar overrides: cada exceção tem um papel e justificativa, não uma cor/tamanho arbitrário repetido.

Commit sugerido: `docs(ui): document replaceable presentation architecture and validation`.

## 6. Testes e critérios de aceite

### Contratos e fronteiras

- Componentes básicos não importam HTTP/gateway/sessão/router e não enviam comandos.
- Controllers não constroem nodes, stylesheets ou URLs e não calculam regras de jogo.
- Presenters não dependem de tema, nodes ou controllers.
- Tema, escala, arte e layout não entram em payloads, seed ou identidade de comandos.
- Configuração visual não altera snapshots, receipts ou definições da engine.
- Nenhum consumidor usa método interno de outro componente como API implícita.

### Estabilidade funcional

- Uso de carta termina sem seleção automática de outra carta.
- Preview contextual permanece separado da face e tooltip.
- Mão vazia mantém geometria; overflow não reduz a fonte.
- Sidebar recolhida mostra apenas a alça; expandir não reorganiza a mão.
- Pausa e inspeção respeitam modalidade/foco; nenhum comando atravessa overlay.
- Novos snapshots não repetem animações de entrada de componentes existentes.
- Respostas de contexto antigo não atualizam a tela ativa.
- A fila de animações continua manual/automática conforme preferência existente.
- Recursos, tags, cartas, atores e opções desconhecidos usam fallback, sem semântica hardcoded.

### Matriz visual

Usar 1280×720, 1280×800, 1920×1080 e 2560×1080; inglês/português; escala 1,0 e 1,2; alto contraste/movimento reduzido; mouse/teclado/controle; mão vazia, normal e grande; textos extensos; desconexão e falhas de sincronização.

### Instrumentação proposta

- `ui_contracts.gd`: modelos, intenção, cópias defensivas e dependências.
- `combat_controller.gd`: transições de estado, delegação e chamadas esperadas.
- `ui_reconciliation.gd`: identidade/foco, contadores de reconstrução e atualização assíncrona.
- `theme_contracts.gd`: perfis, estados, contraste semântico, escala e troca reversível.
- `view_replacement.gd`: duas composições com comportamento equivalente.

Nomes de novos testes são propostas. Reusar as fixtures e asserts atuais em vez de substituir testes funcionais por testes que apenas confirmem a nova estrutura.

Não estabelecer um tempo absoluto com base em uma única medição. Comparar medianas/p95 e contagens de chamadas no mesmo ambiente; a refatoração não deve aumentar requests por ação nem reconstruções não relacionadas. Medidas síncronas de UI não substituem tempo clique→resposta, GPU ou benchmark da engine.

## 7. Ordem em relação ao Marfim

1. Executar as etapas 1–4 deste plano preservando o visual atual: contratos, controller, componentes e reconciliação.
2. Executar as etapas 5–6: infraestrutura visual única e componentes configuráveis.
3. Aplicar o perfil Marfim e seus circuitos/placeholder por cima dessa infraestrutura, seguindo as etapas visuais do plano Marfim.
4. Combinar as extrações das etapas 7–9 com a migração visual das respectivas telas, em commits separados quando lógica e aparência forem alterações distintas.
5. Concluir provas de substituição e certificação antes de considerar a repaginação completa.

Não implementar dois `VisualThemeProfile`, dois catálogos ou duas pipelines de preferência em cada plano. As entregas compartilhadas são feitas uma vez e referenciadas pelo outro documento.

Marfim continua a direção escolhida. A base atual é um perfil de preservação durante a migração, não uma exigência de manter suporte público permanente ao tema antigo. A fixture alternativa serve como teste, não como segundo frontend de produção.

## 8. Riscos e limites

| Risco | Controle |
| --- | --- |
| Refatoração virar nova engine de UI | Contratos pequenos, recursos nativos e extrações justificadas por consumidores reais. |
| Controller virar novo supermódulo | Componentes possuem desenho; presenters possuem projeção; controller somente intenção/estado local. |
| Estado duplicado entre view/controller/sessão | Tabela de autoridades e testes de transição por contexto. |
| Modelos rígidos quebrarem settings extensíveis | Campos estáveis tipados, seções opcionais e dados desconhecidos preservados na borda. |
| Troca de tema restaurar styles/fonts antigos | Aplicação idempotente e invalidação por perfil; testes A→B→A. |
| Quebrar mão/pausa durante movimentação de nodes | Preservar mecanismos existentes; testar cada extração antes de mudar a composição. |
| Atualização parcial deixar dados antigos | Modelos de snapshot coerentes, invalidadores de inspeção/idioma e epochs explícitos. |
| Testes ficarem presos a paths de nodes antigos | Verificar contratos, IDs e comportamento; paths estruturais só onde o layout é o objeto do teste. |
| Limpeza apagar o fallback necessário | Retirar código duplicado, não suporte a conteúdo desconhecido ou mensagens de erro. |
| Refatoração esconder regressões com testes offline | Integração real permanece gate de liberação; registrar claramente API indisponível. |

## 9. Referências de implementação

- [Arquitetura atual do cliente](../../examples/godot-engine-showcase/ARCHITECTURE.md).
- [Diretrizes de cartas](../roadmap/CARD_PRESENTATION_GUIDELINES.md).
- [Godot — temas, herança, variações e overrides](https://docs.godotengine.org/en/stable/tutorials/ui/gui_skinning.html).
- [Godot — organização de cenas e dependências explícitas](https://docs.godotengine.org/en/stable/tutorials/best_practices/scene_organization.html).
- [Godot — containers e propriedade do layout](https://docs.godotengine.org/en/stable/tutorials/ui/gui_containers.html).

Essas referências orientam o uso dos recursos nativos. A estrutura de controllers, modelos e etapas é uma decisão proposta para o código atual do projeto, não uma exigência literal da Godot.
