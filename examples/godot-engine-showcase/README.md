# HeroScript: Ember Archive

Demo jogável em Godot 4 que usa a HeroScript como máquina de regras por meio exclusivo da API REST. A interface é inspirada no gênero de *deckbuilder roguelike*, sem reutilizar arte, texto ou código de outro jogo.

## Executar

No PowerShell, a partir deste diretório:

```powershell
.\Start-Demo.ps1
```

O launcher inicia a API em `http://127.0.0.1:5271`, guarda os dados de teste em `.runtime/`, abre a Godot e encerra apenas o processo que ele próprio iniciou. Também é possível abrir `project.godot` no editor depois de iniciar a API separadamente.

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

## Idiomas e controles

Em **Configurações → Idioma**, escolha **Português (Brasil)** ou **English**.
A troca é imediata e persistida. Também estão disponíveis velocidade de
animação, movimento reduzido e reprodução automática/manual dos frames.

- Selecione uma carta e clique em um alvo destacado. Um segundo clique na carta
  só confirma diretamente quando existe exatamente uma escolha legal.
- Clique direito ou **Cancelar seleção** limpa a seleção. Custos alternativos
  e conjuntos de alvos distintos são apresentados como escolhas explícitas.
- Os custos e valores da prévia vêm da engine; buffs e upgrades não são
  recalculados na Godot.
- Por padrão, **E** encerra turno, **T** abre a timeline, **Esc** pausa e
  **Espaço** avança um frame no modo manual. Os atalhos são remapeáveis.
- Novas ações ficam bloqueadas durante envio, sincronização e reprodução dos
  frames. Em caso de projeção desatualizada, use **Atualizar**.

A apresentação exibe o snapshot final autoritativo e anima as aplicações dos
frames recebidos; ela não executa novamente as regras nem altera a timeline.

## O que a demo cobre

- campanha configurada por JSON com encontro, relíquia, recompensa de carta, loja, preparação, upgrade e chefe;
- combate com energia, custos, cartas compostas, efeitos, resources genéricos, bloqueio, status, IA, fases e intents;
- laboratórios com três estilos de regras: energia, ações fixas e prioridade/stack;
- editor de cenário JSON para trocar deck, atores, recursos e seed;
- timeline por comando, frames de apresentação, simulações sem commit, branches e verificação de replay;
- códice que lê o conteúdo publicado da engine pela REST API;
- menu principal, continuar run persistida, pause, áudio, tela cheia e remapeamento de controles;
- português/inglês, entrada/seleção/uso de cartas animados, alvos destacados,
  prévias e contadores de pilhas.

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

A Godot nunca calcula dano, custo, validade de alvo, turno, IA, recompensa ou transição de mapa. `GameSession` traduz intenção de interface para envelopes versionados; `HeroAPI` é a única camada de transporte. Preferências audiovisuais ficam fora do estado da run e, portanto, não interferem em determinismo ou replay.

Arquivos principais:

- `scripts/hero_api.gd`: transporte HTTP com timeout e erros normalizados;
- `scripts/game_session.gd`: sessão, concorrência de comandos e fila de frames;
- `scripts/combat_screen.gd`: projeção visual do combate e envio das escolhas legais;
- `scripts/activity_screen.gd`: cliente genérico dos comandos de progressão;
- `scripts/timeline_screen.gd`: inspeção, branches e replay;
- `scripts/i18n.gd` e `data/locales/`: traduções da interface e nomes por ID;
- `scripts/card_view.gd`: animações exclusivamente visuais das cartas;
- `data/presentation.json`: apenas nomes, cores e presets de apresentação; regras executáveis ficam no pacote da HeroScript.

## Conteúdo da engine usado pela demo

O modo `spire_showcase`, a run `spire_showcase_run` e os atores do showcase ficam em `data/configs/default/Resources`. O conteúdo é compilado, validado e fixado por revisão quando uma run começa. Alterar esses JSONs e reiniciar a API cria uma nova revisão sem reescrever a interface.

Novos conteúdos podem acrescentar nomes em `data/locales/content.json` e
mensagens nos catálogos `pt_BR.json`/`en.json`. IDs, JSON bruto, contratos e
diagnósticos técnicos da API permanecem canônicos; o cliente não traduz regras.

Veja o [diagnóstico de desempenho e decisões de UX](../../docs/roadmap/ENGINE_PERFORMANCE_AND_DEMO_UX.md).
