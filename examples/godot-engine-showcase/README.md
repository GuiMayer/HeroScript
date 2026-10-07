# HeroScript: showcase de settings

Demo jogável em Godot 4 que usa a HeroScript como máquina de regras por meio exclusivo da API REST. A interface é inspirada no gênero de *deckbuilder roguelike*, sem reutilizar arte, texto ou código de outro jogo. O mesmo cliente executa settings completos diferentes sem embutir suas regras.

## Executar

No PowerShell, a partir deste diretório:

```powershell
.\Start-Demo.ps1
```

O launcher inicia a API em `http://127.0.0.1:5271`, guarda os dados de teste em `.runtime/`, abre a Godot e encerra apenas o processo que ele próprio iniciou. Também é possível abrir `project.godot` no editor depois de iniciar a API separadamente.

Antes de reutilizar uma API já aberta, o launcher verifica se ela publica os
settings `default` e `ascendant` e a capacidade `setting-scoped-profiles`. Se encontrar na porta uma versão antiga desta
mesma workspace, ele a encerra e inicia a build atual; processos de outros
programas nunca são encerrados automaticamente.

Para validar a integração sem abrir uma janela:

```powershell
.\Start-Demo.ps1 -Headless
```

Acrescente `-VerboseGodot` ao teste headless para obter o diagnóstico detalhado
do runtime da Godot.

Para testar seleção de carta/alvo, idiomas, custos, animações, pause e replay:

```powershell
.\Start-Demo.ps1 -UiSmoke -Port 5272
```

Portas diferentes de 5271 usam `.runtime/qa-<porta>/`, isolando os dados da
partida normal. O launcher usa a engine em Release. Se uma API já estiver
rodando na porta escolhida, ela é reutilizada: reinicie-a para carregar mudanças
no código da engine.

## Laboratório isolado de cartas e mão

Abra `scenes/card_hand_lab.tscn` no editor e pressione **F6**. Essa cena funciona
sem API e usa os mesmos `CardView` e `CardHandView` do combate, sem executar regras
ou alterar runs. F5 continua abrindo o jogo normal.

Há 17 modelos visuais em quatro grupos, mão de 0 a 40 cartas, textos em português
e inglês, custos variados e alterações simultâneas. Edite as amostras em
`data/labs/card_hand_lab.json`. Elas são fixtures de apresentação, não conteúdo
autoritativo dos settings.

O laboratório também alterna leque adaptativo/leitura, simula bloqueio de input,
adiciona/remove/enriquece amostras e testa drag. As preferências temporárias são
restauradas ao sair. As mesmas melhorias estão no combate: controles por
instância sobrevivem a refresh e uso de cartas, sem repetir entradas nem mudar
fontes dos vizinhos. A mão oferece contagem, overflow, revelação do foco e visão
geral persistente, sem impor capacidade de gameplay.

Pela linha de comando, a partir deste diretório:

```powershell
godot --path . res://scenes/card_hand_lab.tscn
```

O diagnóstico do layout atual, a pesquisa e as recomendações estão em
[Laboratório e pesquisa de design](../../docs/roadmap/CARD_HAND_DESIGN_LAB_AND_RESEARCH.md).

As [diretrizes de apresentação das cartas](../../docs/roadmap/CARD_PRESENTATION_GUIDELINES.md)
definem anatomia, raridade na borda e cores dos valores. A paleta compartilhada é
editável em `data/default_card_visual_style.tres`, usada pela carta, tooltip e inspeção.
Validação offline: `godot --headless --path examples/godot-engine-showcase --script res://tests/card_presentation.gd`.

Regressões da nova mão: `godot --headless --path examples/godot-engine-showcase --script res://tests/hand_ux.gd`.
Integração real em dois settings, a partir da raiz do repositório:

```powershell
godot --headless --path examples/godot-engine-showcase --script res://tests/hand_online.gd -- --layout-smoke --api-url=http://127.0.0.1:5271
```

Esse teste cria runs de QA na API indicada; prefira uma porta de QA com dados
isolados quando disponível. O [plano da mão](../../docs/plans/CARD_HAND_UX_IMPLEMENTATION_PLAN.md#9-implementação-e-verificação)
registra resultados e a falha independente de branches com commits antigos.

## Continuação e progresso por setting

Cada setting possui histórico, estatísticas, conquistas e desbloqueios separados,
derivados pela engine das suas próprias runs. Revisões de conteúdo do mesmo
setting compartilham o perfil; as regras de uma run existente continuam fixadas
à sua revisão original. Os marcos meta atuais são básicos, não uma economia
permanente nova.

O botão **Continuar** usa a última run ativa do modo publicado em `setting.launch`,
com referência separada por URL da API, jogador, setting e modo. Abrir um sandbox
não substitui a campanha. No histórico do setting é possível escolher outra run
ou modo explicitamente. Runs concluídas, abandonadas, ausentes ou pertencentes
a outro setting/jogador não são ativadas por Continuar.

As referências e contadores de seeds por setting são salvos em
`user://heroscript_showcase.cfg`. O antigo ID global é apenas uma pista de
recuperação: a engine precisa confirmar a identidade do save. Sem uma referência
antiga correspondente, a recuperação prefere a campanha ativa com mais comandos
registrados no perfil (não uma suposta data de criação). Todas as demais partidas
continuam acessíveis pelo histórico; nenhum save é apagado ou convertido.
Uma referência conhecida encerrada/ausente fica vazia naquele escopo, para não
ressuscitar automaticamente uma campanha antiga ainda ativa. O histórico permite
selecioná-la explicitamente quando desejado.

O launcher exige `setting-scoped-profiles`, para não reutilizar uma API antiga
que ainda misture progresso entre settings. Ao atualizar, feche o jogo e inicie
novamente pelo launcher. Nenhuma preferência global de idioma, vídeo ou input
é particionada: apenas dados de sessão/progressão.

Regressão offline, sem alterar preferências ou runs reais:

```powershell
godot --headless --path . --script res://tests/setting_progression.gd -- --layout-smoke
```

## Idiomas e controles

Em **Configurações → Idioma**, escolha **Português (Brasil)** ou **English**.
A troca é imediata e persistida. Também estão disponíveis velocidade de
animação, movimento reduzido e reprodução automática/manual dos frames.

Em **Configurações → Geral → Mão de cartas**, escolha leque adaptativo ou leitura
sem sobreposição, ajuste a intensidade do destaque e habilite drag opcional.
Arrastar fica desligado por padrão; clique e escolhas de alvo/custo continuam
disponíveis. Fontes nunca encolhem para caber mais cartas.

Inglês é o idioma inicial e o fallback. As chaves dos catálogos e os textos-base
de `presentation.json` são mensagens em inglês. Uma preferência de português
já salva é respeitada; idioma desconhecido, mensagem ausente ou catálogo ausente
recorrem ao inglês. Os nomes de conteúdo também priorizam o fallback inglês.

- Selecione uma carta e clique em um alvo destacado. Um segundo clique na carta
  só confirma diretamente quando existe exatamente uma escolha legal.
- Clique direito ou **Cancelar seleção** limpa a seleção. Custos alternativos
  e conjuntos de alvos distintos são apresentados como escolhas explícitas.
- Os custos e valores da prévia vêm da engine; buffs e upgrades não são
  recalculados na Godot.
- Por padrão, **E** encerra turno, **T** abre a timeline, **Esc** pausa e
  **F** avança um frame no modo manual. Os atalhos são remapeáveis.
- **I** abre a inspeção da carta ativa e **H** abre a visão geral da mão.
  No controle, os padrões são **X** e **LB**. Setas/direcional percorrem a ordem
  recebida da engine, revelando cartas fora da área; fechar os detalhes devolve
  o foco. Tooltip é resumo passivo, não um painel com rolagem.
- Teclado: setas/Tab navegam e Enter confirma. Controle: direcional navega,
  A confirma, Start pausa, Y encerra turno, Back abre a timeline e RB avança
  animações. Remapear uma entrada preserva o vínculo do outro dispositivo.
- Novas ações ficam bloqueadas durante envio, sincronização e reprodução dos
  frames. Em caso de projeção desatualizada ou comando com resposta incerta,
  use **Reconectar**. A recuperação reutiliza a identidade do comando pendente.

A apresentação exibe o snapshot final autoritativo e anima as aplicações dos
frames recebidos; ela não executa novamente as regras nem altera a timeline.

## Trocar de setting

No menu principal, use **Setting do jogo** antes de iniciar uma nova jornada.
A preferência fica salva, mas cada run existente continua vinculada ao setting
e à revisão em que foi criada. Ao usar **Continuar**, o cliente adota novamente
o setting registrado pela própria run.

- **Ember Archive**: campanha ampla para cartas, recursos, status, progressão e
  diferentes atividades.
- **Ascendant Matrix**: campanha curta de buildcraft inspirada na leitura em
  camadas de jogos como Warframe e Path of Exile. A pipeline soma bônus
  `increased`, aplica uma camada elemental e depois multiplicadores
  independentes de suporte, crítico e vulnerabilidade. Tudo está em JSON; o
  processador continua conhecendo apenas buckets genéricos.

### Atualização visual estável

`CombatScreen` e `ActivityScreen` permanecem montados enquanto o usuário
continua no mesmo contexto. Depois de um comando aceito, a sessão publica o novo
snapshot e a tela atualiza seus componentes de forma atômica; recriar a tela
inteira não faz parte do refresh normal. Transições reais — por exemplo,
Journey → Combat, Combat → Journey ou abrir a Timeline — continuam trocando de
tela.

Essa política evita que controles estáveis pisquem, percam foco ou mudem de
posição sem necessidade. Cartas já existentes não repetem a animação de entrada,
as áreas contextuais do rodapé preservam sua geometria, o Journey conserva o
scroll do mapa/lista de ações e resultados assíncronos de inspeção só são
aplicados à geração visual que os solicitou. Preferências de fonte/contraste são
aplicadas antes do próximo frame; enriquecer uma carta não altera
temporariamente sua tipografia.

## O que a demo cobre

- campanha configurada por JSON com encontro, relíquia, recompensa de carta, loja, preparação, upgrade e chefe;
- seletor de settings que troca configuração, conteúdo, modo, run e revisão sem
  trocar a interface Godot;
- combate com energia, custos, cartas compostas, efeitos, resources genéricos, bloqueio, status, IA, fases e intents;
- laboratórios com três estilos de regras: energia, ações fixas e prioridade/stack;
- editor de cenário JSON para trocar deck, atores, recursos e seed;
- timeline por comando, frames de apresentação, simulações sem commit, branches e verificação de replay;
- códice que lê o conteúdo publicado da engine pela REST API;
- menu principal, continuar run persistida, pause, áudio, tela cheia e remapeamento de controles;
- português/inglês, entrada/seleção/uso de cartas animados, alvos destacados,
  prévias e contadores de pilhas;
- telas persistentes no combate e Journey, estado vazio legível e layout estável
  durante seleção, comandos e enriquecimento assíncrono das cartas.

## Fronteira de arquitetura

```text
JSON publicado ──> HeroScript ──> snapshot + comandos legais + frames
                                         │
                                         └── REST ──> Godot
                                                      ├─ input
                                                      ├─ animação
                                                      ├─ áudio
                                                      └─ menus locais
```

A Godot nunca calcula dano, custo, validade de alvo, turno, IA, recompensa ou
transição de mapa. As telas enviam escolhas a `GameSession`; o gateway monta
os envelopes versionados e somente o transporte conhece HTTP. A fila de
animações é independente da sessão. Preferências audiovisuais ficam fora do
estado da run e, portanto, não interferem em determinismo ou replay.

Arquivos principais:

- `scripts/engine/http_transport.gd`: HTTP, timeout, JSON e erros estruturados;
- `scripts/engine/engine_gateway.gd`: rotas, contratos REST e envelopes;
- `scripts/application/game_session.gd`: sessão, sincronização e casos de uso;
- `scripts/application/activity_choices.gd`: escolhas anunciadas de progressão;
- `scripts/presentation/playback.gd`: fila e cursor de animação, sem comandos;
- `scripts/presentation/i18n.gd` e `data/locales/`: localização;
- `scripts/ui/`: telas, tema, cartas, retratos, áudio e input;
- `scripts/bootstrap.gd`: montagem das dependências e integração com preferências;
- `data/presentation.json`: apenas nomes, cores e presets de apresentação; regras executáveis ficam no pacote da HeroScript.

## Conteúdo da engine usado pela demo

O setting `default` contém o modo `spire_showcase`, a run
`spire_showcase_run` e os atores do Ember Archive. O setting `ascendant` vive em
`data/configs/ascendant`, depende do pacote base e acrescenta sua própria run,
modo, cartas, atores, upgrades, relíquia e pipeline. O conteúdo é compilado,
validado e fixado por revisão quando uma run começa. Alterar esses JSONs e
reiniciar a API cria uma nova revisão sem reescrever a interface.

Novos conteúdos podem acrescentar nomes em `data/locales/content.json` e
mensagens nos catálogos `pt_BR.json`/`en.json`. IDs, JSON bruto, contratos e
diagnósticos técnicos da API permanecem canônicos; o cliente não traduz regras.

Para validar as fronteiras sem iniciar a engine:

```powershell
godot --headless --path . --script res://tests/layers.gd
```

Esse teste usa transporte injetado, verifica isolamento dos snapshots, fallback,
envelopes, falhas de sincronização e dependências proibidas entre camadas.
Veja [como substituir a interface](ARCHITECTURE.md).

## Inspeção e histórico

Selecione uma carta e use **Inspecionar carta** para consultar escolhas,
upgrades e fontes disponibilizadas pela engine. Cartas indisponíveis também
podem ser selecionadas para inspeção. Os botões das pilhas mostram seu conteúdo
ordenado por nome, sem expor a ordem de compra.

Na timeline, selecione um comando ou use o cursor para ver o estado salvo
depois dele. A reprodução dos frames tem cursor próprio: não altera a run nem
reconstrói estados intermediários. **Criar branch aqui** abre outra run;
**Ativar branch** retoma uma existente na árvore. As permissões vêm do modo.
A simulação de fim de turno usa o estado atual da run, não o comando histórico
selecionado; para experimentar outro passado, primeiro ative uma branch dele.

Configurações incluem tamanho de texto, contraste, movimento reduzido e
restauração dos controles. Veja [etapas e verificações](UX_IMPLEMENTATION.md).

### Resoluções

Em **Configurações → Resolução da janela**, escolha presets de 1280×720 até
3840×2160, incluindo **2560×1080** e 3440×1440 ultrawide. A escolha é salva.
Em tela cheia, usa-se a resolução nativa do monitor; ao voltar ao modo janela,
a resolução escolhida é restaurada, ajustada à área disponível se necessário.
O layout expande com a proporção da tela sem deformar os elementos, seguindo
o [suporte a múltiplas resoluções da Godot](https://docs.godotengine.org/en/stable/tutorials/rendering/multiple_resolutions.html).

Teste offline de resoluções, menu, proporção, mão vazia, escala tipográfica
durante enriquecimento assíncrono e persistência:

```powershell
godot --headless --path . --script res://tests/resolutions.gd -- --layout-smoke
```

Teste offline do menu de pausa sobre a mão compartilhada (cartas elevadas,
foco, clique em Retomar e cancelamento de arraste nas três resoluções):

```powershell
godot --headless --path . --script res://tests/pause_layers.gd -- --layout-smoke
```

O menu usa um `CanvasLayer` próprio acima do gameplay: as prioridades locais
das cartas não podem cobrir o painel nem seu fundo escurecido. Executar o mesmo
teste sem `--headless` também verifica o escurecimento na imagem renderizada.

No combate, o cenário ocupa toda a área de gameplay e continua por trás da
mão flutuante, ancorada no rodapé, sem painel nem título de seção. Os retratos
verticais usam o espaço disponível acima das cartas; informações, intenções e
alvos permanecem separados da área da mão. Pilhas e ferramentas ficam abaixo
das cartas. O painel do personagem começa recolhido e é flutuante também quando
aberto: não reserva coluna nem redimensiona o campo ou a mão. Recolhido, aparece
somente a seta no canto superior direito. Aberto, oferece detalhes com rolagem
própria e bloqueia cliques através do painel. O foco de teclado acompanha a seta;
atualizações do combate preservam o estado escolhido. Um único dock inferior
agrupa preview, avanço manual da apresentação, cancelar/passar e encerrar turno.
Opções de pagamento/alvos alternativos surgem em um painel contextual flutuante,
sem deslocar a mão nem cobrir as ferramentas. O histórico conserva sua
apresentação própria, usando os mesmos componentes.

Pesquisa e decisões: [organização do HUD de combate](../../docs/roadmap/COMBAT_HUD_LAYOUT_AND_RESEARCH.md).

Teste offline dessa composição, input real, mão vazia, sidebar, idiomas e
texto a 100/120%, incluindo 1280×800 e 2560×1080:

```powershell
godot --headless --path . --script res://tests/combat_stage_layout.gd -- --layout-smoke
```

Uma jogada confirmada limpa a escolha de carta e alvo, inclusive quando a
instância permanece na mão. O foco vai para uma ferramenta neutra, não para a
primeira carta. Navegação explícita por teclado/controle retorna às cartas;
refresh ou confirmação de animação não escolhem outra carta automaticamente.
Jogadas rejeitadas preservam a escolha para recuperação.

Teste offline do fluxo de comando da sessão e da interface, com gateway
injetado (carta consumida/retida, animação, refresh, teclado e rejeição):

```powershell
godot --headless --path . --script res://tests/card_play_selection.gd -- --layout-smoke
```

Veja o [diagnóstico de desempenho e decisões de UX](../../docs/roadmap/ENGINE_PERFORMANCE_AND_DEMO_UX.md).

## Core de cartas voláteis

**Volatile Crucible** usa o mesmo cliente com composição permanente de cartas,
condensação genérica, múltiplos impactos e continuação causal. Transformações são
confirmadas com before/after consultado na API; tooltip e inspector mostram o
consumo e os procs. [Guia específico](../../docs/guides/volatile-core-godot.md).
Teste adicional offline: `godot --headless --path . --script tests/volatile_core.gd`.
