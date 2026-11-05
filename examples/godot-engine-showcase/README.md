# HeroScript: showcase de settings

Demo jogável em Godot 4 que usa a HeroScript como máquina de regras por meio exclusivo da API REST. A interface é inspirada no gênero de *deckbuilder roguelike*, sem reutilizar arte, texto ou código de outro jogo. O mesmo cliente executa settings completos diferentes sem embutir suas regras.

## Campanha tática do MVP

No menu principal, selecione **Tactical campaign MVP** e inicie uma nova campanha.
O lançamento normal usa `prototype-test` / `prototype_chapter`, com as capacidades
de gameplay normal. O setting tem progressão própria; não reutiliza o save do
Ascendant. Continue retoma a campanha salva desse setting.

São três encontros (o último é o chefe), diálogos curtos, duas decisões exclusivas
de recompensa, preparo e workshops opcionais. Abra Loadout fora do combate para
alterar inscrições do personagem, composição 15+5, reserva, módulo e rotina do
suporte. A derrota mantém o nó pendente e permite preparar outra tentativa; itens,
modificações, materiais e recompensas anteriores permanecem. Calor substitui a
energia universal neste preset; Void paga cartas da mão e Necro paga do descarte.

O cenário curto `prototype_trial` e os settings Payment/Support/Reward Workshop
continuam provas isoladas de regressão. O laboratório unificado usa os mesmos
componentes da campanha, com uma captura somente leitura para inspeção visual.

Consulte as [regras executáveis](../../docs/game-design/PROTOTYPE_TEST_RULES.md),
[o roteiro de validação](../../docs/guides/tactical-mvp-ui-validation.md) e
[o relatório](../../docs/validation/TACTICAL_CAMPAIGN_MVP.md). O capítulo está
implementado, mas o gate de latência dos turnos e o playtest externo continuam
abertos; não está declarado um jogo final balanceado ou um export standalone pronto.

## Executar

No PowerShell, a partir deste diretório:

```powershell
.\Start-Demo.ps1
```

O launcher inicia a API em `http://127.0.0.1:5271`, guarda os dados do jogador
em `%APPDATA%\HeroScript`, abre a Godot e encerra apenas o processo que ele próprio
iniciou. Também é possível abrir `project.godot` no editor e pressionar F5:
no Windows, o menu verifica a API local e inicia o servidor em segundo plano
se ele estiver desligado, aguardando readiness antes de conectar.

A inicialização pelo editor reutiliza `Start-Demo.ps1 -ServerOnly -AutoStart`,
o SDK .NET e os mesmos dados no AppData. Não abre outra Godot. O servidor criado
fica vinculado ao processo do jogo e encerra junto dele; uma API previamente
ligada é reutilizada e não é encerrada pelo cliente. Uma porta ocupada por um
servidor desconhecido não é liberada à força pela inicialização automática.

Esse recurso exige o checkout com `src/API` e o launcher; não equivale a um host
empacotado no export final. Endpoints remotos continuam exigindo um servidor
externo. Para desabilitar explicitamente, passe `--no-auto-server` nos argumentos
do jogo. Testes offline com `--layout-smoke` nunca iniciam servidores.

### Dados locais e migração

```text
%APPDATA%\HeroScript\
  heroscript_showcase.cfg       preferências e referências de Continuar por setting
  card_animation_profile.json  override opcional do laboratório de animação
  logs\                        logs nativos da Godot
  engine\
    runs\                      commits, histórico e provas de progressão
    content\                   revisões imutáveis usadas pelos saves
    telemetry\                 diagnóstico operacional
    migration-v1.json          inventário SHA-256 da migração inicial
    legacy-logs\               logs antigos da API, se existirem
  qa\<porta>\engine\           saves isolados das execuções em outras portas
```

Na primeira abertura pelo launcher, os diretórios `runs`, `content` e `telemetry`
de `.runtime` são copiados e verificados antes de ativar o novo armazenamento.
Preferências antigas em `%APPDATA%\Godot\app_userdata\HeroScript- Ember Archive`
também são copiadas quando ainda não existe arquivo no destino. Os originais
não são apagados. Conflitos com um destino desconhecido ou uma API gravando
impedem a migração; não há sobrescrita silenciosa. Uma segunda abertura não
reimporta saves antigos sobre progressão nova.

A separação por setting é lógica, mantida pela autoridade da engine
(`playerId + settingId`) e pelo índice de Continuar
(`API + personagem + setting + modo`). Não se divide ou reescreve o journal por
pastas de setting: isso preserva IDs, hashes, branches, replay e desbloqueios.
Faça backup de `engine/runs` e `engine/content` juntos, além do `.cfg`.
Packages JSON de autoria continuam no repositório, não no armazenamento do jogador.

Antes de reutilizar uma API já aberta, o launcher verifica se ela publica os
settings `default` e `ascendant`, `setting-scoped-profiles` e as capabilities dos
fundamentos do mundo 1, incluindo `canonical-effect-identities` (engine 28),
`compact-interaction-sessions-v1` (contrato REST de interação, engine 31 mantida), e o
launch do Ascendant em `ascendant_world_one`. Se encontrar na porta uma versão antiga desta
mesma workspace, ele a encerra e inicia a build atual; processos de outros
programas nunca são encerrados automaticamente.

Recursos do personagem são lidos de `characterResources`, separados da carteira
da run. Recuperação é uma atividade legal com custo/efeito no mesmo commit;
disponibilidade atual do perfil não substitui a elegibilidade da tentativa salva.
Veja o [contrato REST](../../docs/api/world-one-foundations.md) e o
[relatório de validação](../../docs/plans/ENGINE_WORLD_1_FOUNDATIONS_VALIDATION_REPORT.md).

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

Portas diferentes de 5271 usam `%APPDATA%\HeroScript\qa\<porta>\engine`, isolando os dados da
partida normal. O launcher usa a engine em Release. Se uma API já estiver
rodando na porta escolhida, ela só é reutilizada se o launcher reconhecer seu
processo e vínculo com o novo armazenamento. APIs antigas desta workspace são
encerradas antes da migração/reinicialização; outros programas são recusados.
Reinicie a API para carregar mudanças no código da engine.

## Apresentação do combate

Nas configurações há presets Normal, Rápido e Instantâneo, além de movimento
reduzido e avanço automático/manual. O botão de skip conclui somente a parte
visual: não encerra turno nem envia outro comando. Pause congela voo, impacto,
cenário e vozes de combate; os sons do menu continuam disponíveis.

O manifesto `data/motion_manifest.json` define durações, curvas, limites e cues
com overrides por setting. A política é capturada antes de cada apresentação;
editar o arquivo afeta o próximo comando, não o comando em curso.
Replay/timeline podem apresentar um comando salvo sob demanda quando a engine
autoriza a inspeção histórica. Consulte o
[guia de autoria](../../docs/design/COMBAT_PRESENTATION_AUTHORING.md).

## Laboratório de inscrições e loadout

Arraste inscrições da coleção para as posições; entre posições ocupadas, o gesto
troca os itens. Soltar fora cancela. A seleção por clique e o botão de equipar
continuam disponíveis. Posições vazias têm fundo verde-acinzentado, `+` e
“Solte aqui”; posições ocupadas mostram nome/custo sobre marfim.
[Referências e contrato de interação](../../docs/design/INSCRIPTION_LOADOUT_UX_REFERENCES.md).

No menu principal, abra **Loadout lab / Laboratório de inscrições**. Também é
possível abrir `scenes/inscription_loadout_lab.tscn` no editor e pressionar **F6**,
sem API. A cena usa o tema marfim e oferece quinze exemplos, seis posições,
presets, busca e um rascunho local com equipar/mover/remover/restaurar.

O painel direito mostra atributos do personagem, com base e valor equipado:
vida/energia/mana máximas, armadura, esquiva (base 0%), chance crítica,
bônus de dano crítico por tier, especializações e bônus por tag. As descrições
indicam bônus aos atributos do personagem, não mudanças na base das cartas.
Relíquias e modificadores de cartas não aparecem neste laboratório.
O modo **Example (offline)** é um protótipo visual: custos ilustrativos e orçamento
livre para avaliação. A simulação soma apenas bônus de exemplo em unidades de
exibição, sem alterar run/save/preferências. As quinze amostras estão em
`data/labs/inscription_loadout_lab.json`; não são o catálogo publicado do setting.

Para equipamento real, crie/continue uma run Ascendant compatível e abra
**INSCRIPTIONS** no mapa/atividade. Essa rota abre `scenes/run_loadout.tscn`,
vinculada ao personagem atual da run, sem exemplos, presets ou seletor de personagem.
Falhas mostram erro e recuperação, nunca um inventário fictício. Uma mudança de
run/personagem exige reabrir a tela; respostas antigas não são reaproveitadas.
Arraste inscrições para equipar/trocar, ou clique no item e depois na posição
para equipar diretamente, sem precisar encontrar outro botão abaixo da tela.
A validação e a gravação são automáticas, sem botões de refresh, preview ou
confirmação. **Clear draft** esvazia o equipamento e também salva a alteração.
O layout marfim organiza inventário, inscrições equipadas e personagem em três
colunas, com busca/filtros visuais, sigilos do catálogo existente, custo no canto
e slots vazios em verde-acinzentado. A inscrição selecionada destaca os atributos
que declara afetar, sem acrescentar bônus localmente. A tabela mostra os valores
equipados; a inspeção conserva os detalhes de origem. Resoluções de 1280×720 a
2560×1080 e texto ampliado são cobertos por `tests/loadout_editorial.gd`.
`tests/loadout_input.gd` exerce cliques e arraste nativos. A regressão REST
`tests/loadout_input_online.gd` exige uma API isolada na porta 61987, com dados
de teste, e verifica gravação, remoção e replay. O gateway converte sequência e
step recebidos como números JSON para inteiros antes de enviar a prévia.
Essa adaptação também cobre contagens de cartas, IDs de transformação,
restauração de histórico, cenários e comandos simulados, sem converter recursos
ou outros decimais legítimos. Veja a [auditoria do contrato numérico](../../docs/architecture/API_NUMERIC_WIRE_CONTRACT.md).
A engine calcula atributos e valida posse, slots, orçamento e janela; a Godot
não soma valores no modo conectado. A tela lê o equipamento ao abrir e após
salvar, sincroniza mudanças da sessão e recupera respostas incertas pelo fluxo
idempotente existente. Uma proposta rejeitada não é apresentada como salva.

O laboratório do menu principal continua separado em
`scenes/inscription_loadout_lab.tscn`, com exemplos e seletores para testes.
Sua conexão REST opcional é uma ferramenta de laboratório, não a tela da run.

O mundo 1 distribui inscrições iniciais não equipadas, recompensas com recusa,
ofertas pagas e crafting dirigido limitado. Os dois escopos de inventário e dois
personagens podem ser explorados offline; a jornada publicada registra só o herói.
Não há importação das peças fictícias para a run.

O [plano de arquitetura e implementação](../../docs/plans/INSCRIPTIONS_LOADOUT_ARCHITECTURE_AND_PLAN.md)
registra as etapas concluídas e os limites. Consulte também os contratos de
[REST](../../docs/api/inscription-loadouts.md),
[aquisição](../../docs/content/inscription-acquisition.md) e
[migração](../../docs/content/inscription-migrations.md).

## Laboratório isolado de cartas e mão

Abra `scenes/card_hand_lab.tscn` no editor e pressione **F6**. Essa cena funciona
sem API e usa os mesmos `CardView` e `CardHandView` do combate, sem executar regras
ou alterar runs. F5 continua abrindo o jogo normal.

O laboratório aplica o perfil **Marfim / circuito impresso**, isolado do tema do
jogo. Títulos editoriais, placeholders em tinta e efeitos em faixa escura usam
os componentes compartilhados; ao sair, o perfil anterior é restaurado.
[Guia e capturas](../../docs/design/IVORY_CARD_LAB.md).

Há 17 modelos visuais em quatro grupos, mão de 0 a 100 cartas, textos em português
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

`tests/card_refresh_stability.gd` verifica a publicação atômica da mão: uma
inspeção atrasada não mostra texto genérico entre revisões, os componentes da
carta são preservados e respostas obsoletas/falhas não liberam input nem
substituem os valores autoritativos. O teste faz parte de `tools/test-godot-ui.ps1`.

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
- **I** abre a inspeção da carta ativa. A mão usa leque adaptativo mesmo com duas cartas; a visão geral foi retirada do combate.
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
O movimento da carta começa assim que o comando é aceito, enquanto as consultas
de atualização continuam. Dano, custos e textos novos aguardam a projeção completa.
Esse voo é reaproveitado na fila, sem repetir o movimento ou adicionar espera vazia.
Previews da engine usam caches limitados por snapshot imutável e entrada completa;
mudanças de estado, setting ou revisão não reutilizam resultados antigos.

## Trocar de setting

No menu principal, use **Setting do jogo** antes de iniciar uma nova jornada.
A preferência fica salva, mas cada run existente continua vinculada ao setting
e à revisão em que foi criada. Ao usar **Continuar**, o cliente adota novamente
o setting registrado pela própria run.

- **Ember Archive**: campanha ampla para cartas, recursos, status, progressão e
  diferentes atividades.
- **Ascendant Matrix**: primeiro ato em alpha, com 30 cartas, 12 transformações,
  oito relíquias, 14 paradas principais e nove combates por caminho. Rotas
  oferecem encontros alternativos, três elites opcionais, loja, descansos,
  forjas e cinco eventos. Energia paga cartas; vida persiste entre combates.
  Crítico multinível, DoT, condensação, multi-hit e defesa usam os processadores
  genéricos da engine. A duração e o balanceamento ainda precisam de playtests.

Depois de atualizar, reinicie pelo launcher e escolha **Nova run** no Ascendant.
Um save anterior permanece na revisão do showcase antigo; continuar não o migra.
Veja o [catálogo e contratos do ato](../../docs/content/ASCENDANT_WORLD_ONE.md).

Validação local da apresentação do ato, sem API:

```powershell
godot --headless --path . --script res://tests/world_one.gd
```

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
modo, cartas, atores, upgrades, relíquias, serviços e pipelines. O launch atual
é `ascendant_world_one_run` / `ascendant_world_one`; a vitrine anterior permanece
como fixture. O conteúdo é compilado,
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

Para trocar apenas aparência/layout, use o [guia de perfis visuais](../../docs/design/GODOT_VISUAL_PROFILES.md): tema semântico, fontes, composição e manifestos de arte. Mão, controllers e playback são compartilhados. Marfim é o padrão da run, com perfis separados de UI/cenário; o laboratório mantém sua variante de carta mais alta. O perfil contrastante/lateral permanece uma prova de teste.

O mapa é uma tela própria: clique ou foco inspeciona; **Viajar** confirma uma opção publicada. Pan, centralização, próximas escolhas, visão geral e destaque por tipo alteram somente a leitura. Diálogos abrem outra cena, com retrato, caixa de fala e opções/custos. O texto aparece instantaneamente por padrão; **Configurações → Acessibilidade → Revelar texto aos poucos** é uma preferência local persistente. O histórico da conversa atual fica no **menu de pausa**, sem avançar a leitura ou executar escolhas; fica indisponível quando não há transcrição no contexto atual. O atalho de confirmação revela/avança páginas, mas nunca escolhe uma consequência. Ao concluir a conversa, o cliente volta ao mapa antes de viajar.

O combate usa silhuetas maiores, mão central em adaptive fan e dock de ícones. Draw/discard/exile e zonas customizadas abrem uma grade das mesmas cartas, com busca e detalhes de leitura. Visibilidade e ordem respeitam o contrato da engine; consultar a coleção não joga cartas nem altera a run. [Capturas e contratos da composição](../../docs/design/reference-combat/2026-10-12/README.md).

Regressões da arquitetura visual podem ser executadas isoladamente com
`tools/test-godot-ui.ps1`. As [capturas e certificação da refatoração](../../docs/design/interface-refactor/2026-10-11/README.md) registram limites, métricas e diagnósticos de encerramento, separadamente da baseline original.

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
O monitor usado é lembrado nas preferências locais e restaurado antes de aplicar
tela cheia. Mover a janela para outra tela atualiza essa preferência sem reconstruir
a interface. A posição da tela no desktop ajuda a resolver mudanças de índice;
se a tela não estiver disponível, usa-se uma tela válida e a janela é centralizada
na área disponível. Essa identificação não é um ID físico persistente do monitor.
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
aberto: não reserva coluna nem redimensiona o campo ou a mão. A borda esquerda abre o painel temporariamente; I alterna abertura fixa. Aberto, oferece detalhes com rolagem
própria e bloqueia cliques através do painel. O foco de teclado permanece no HUD;
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


### Atributos durante o combate

O painel de atributos reutiliza `scripts/ui/loadout/character_attributes_view.gd`, o mesmo componente do loadout. Aproxime o mouse da borda esquerda para abrir temporariamente; ao sair do painel ele recolhe. **I** abre/fecha mantendo-o visível; **Escape** fecha antes de abrir a pausa. O painel flutua sobre o combate sem reservar espaço para uma coluna e apresenta os recursos/atributos atuais do personagem do jogador, inclusive durante ativações inimigas.

A transição usa 0,22 s e respeita movimento reduzido. **Inspecionar carta** passa a **V** por padrão, conservando o acesso pelo mouse e pelo controle. Preferências antigas com a inspeção padrão em I são migradas em memória; atalhos podem ser remapeados nas configurações. A navegação do painel não envia comandos à engine nem altera a run.

## Editar a interface na Godot

Abra `scenes/ui/workbenches/screen_catalog_workbench.tscn` para inspecionar as composições reais e fixtures offline. Telas, overlays e shells dos laboratórios possuem nodes editáveis; scripts preenchem dados, reconciliam itens e mantêm os comportamentos existentes. `scripts/ui/scene_registry.gd` reúne as rotas de produção.

Consulte o [guia de edição e responsabilidades](../../docs/guides/godot-ui-scene-editing.md) e as [evidências da centralização](../../docs/plans/GODOT_SCREEN_SCENE_CENTRALIZATION_PROGRESS.md). Mão, atores, cartas, atributos e executores de animação continuam compartilhados com o combate.
