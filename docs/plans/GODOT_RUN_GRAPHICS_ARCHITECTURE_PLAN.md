# Plano de correção da arquitetura visual da run

Data: 2026-10-11. Status: em implementação. Acompanhar entregas, testes e pendências em [GODOT_RUN_GRAPHICS_IMPLEMENTATION_PROGRESS.md](GODOT_RUN_GRAPHICS_IMPLEMENTATION_PROGRESS.md).

Cliente: `examples/godot-engine-showcase`. Complementa a refatoração já entregue em [GODOT_UI_ARCHITECTURE_REFACTOR_PLAN.md](GODOT_UI_ARCHITECTURE_REFACTOR_PLAN.md), sem repetir suas etapas nem substituir a integração existente.

## 1. Objetivo e limites

Permitir alterar cenário, interface, composição e artes do combate/mapa independentemente, mantendo as regras e o estado autoritativo na HeroScript.

Esta entrega é arquitetural. Preservar inicialmente a aparência da run; aplicar Marfim ao gameplay é uma entrega artística posterior. Não reconstruir a engine, criar outro sistema de mão, outro cursor de combate ou um framework genérico de UI.

Não há mudança obrigatória na engine identificada para estas correções. Usar IDs, relações, nós, arestas, comandos e snapshots já publicados. Se um identificador necessário à seleção visual não estiver exposto, registrar a lacuna de contrato antes de propor metadata opcional; não interpretar uma regra localmente para suprir a ausência.

## 2. Diagnóstico de partida

| Achado | Consequência | Etapas |
| --- | --- | --- |
| `ArtSlot` recebe uma nova textura pelo reconciliador, mas conserva a identidade anterior. | Alterar contexto visual pode restaurar o retrato antigo. Defeito reproduzido. | 1–2 |
| `RouteView` aplica cor de risco alto sem limpar o override quando o risco muda. | Aparência obsoleta. Defeito reproduzido. | 1 |
| `CombatScreen` reúne 777 linhas/66 funções e diversas autoridades de apresentação. | Mudanças geométricas, input, inspeção, feedback e ciclo de vida se afetam. | 4–6 |
| Componentes criam nodes fora de sua árvore e a tela os remove manualmente. | Propriedade/ciclo de vida compartilhados e substituição frágil. | 2, 4 |
| `ActivityScreen` reúne mapa, ofertas, diálogo, recuperação e avanço automático. | Mudanças visuais dependem da coordenação operacional; refresh monta uma árvore temporária completa. | 7 |
| Layout da rota depende do índice do array. | Ramificações são mostradas como coluna, embora as conexões e permissões venham da engine. | 8 |
| Relações são reduzidas ao booleano `hostile`. | Neutros e aliados compartilham apresentação; múltiplas equipes perdem identidade. Limitação reproduzida. | 3, 6 |
| Fundo global, fundo do battlefield, retratos e HUD têm fronteiras incompletas. | Trocar o cenário exige conhecer a árvore de UI. | 6 |
| Componentes recebem `VisualContext`, mas ainda consultam estilos globais. | Contexto injetado não garante isolamento de aparência. | 3, 9 |

Tamanho de arquivo não é critério isolado de qualidade. A correção deve diminuir o número de responsabilidades e dependências, não apenas mover o mesmo supermódulo para outro arquivo.

## 3. Arquitetura-alvo

```text
EngineGateway → GameSession → Presenters → modelos de leitura
                                  │                    │
                  Controllers / coordenação            ├→ cenário da run
                                  ▲                    └→ HUD / mapa / atividades
                                  │                              │
                                  └──── intenções semânticas ─────┘

Receipts → Playback existente → driver de apresentação → feedback do cenário/HUD

ApplicationShell
├─ RunSceneryHost: fundo, palco e artes dos atores
├─ ScreenHost: cabeçalho, mão, controles, indicadores e atividades
├─ FloatingHost: sidebar, preview e opções contextuais
├─ ModalHost: pausa, inspeção e confirmações
└─ NotificationHost: avisos
```

Os nomes dos novos colaboradores abaixo são propostas. Consolidar responsabilidades com as classes existentes quando houver equivalência; não manter versões paralelas em produção.

### Autoridades que não mudam

- Engine/`GameSession`: run, combate, progressão, ações legais, recursos, relações e versões.
- Presenters: projeção de dados/textos; sem nodes, paleta, envio ou fórmulas de gameplay.
- Controllers: seleção, submissão, recuperação e validação contextual de intenções. Não constroem UI.
- `HandLayout`/`HandInteraction`/`CardHandView`: geometria, gestos e componentes da mão existentes.
- `Playback`: único cursor dos frames recebidos. Um driver pode controlar ritmo, nunca criar uma fila autoritativa adicional.
- Views: nodes e estado estritamente visual, incluindo hover, foco, scroll, expansão e tweens.
- Composição: injeta dependências e conecta sinais; não decide resultados do jogo.

### Contratos mínimos

- Views recebem modelos de leitura e `VisualContext`, e emitem IDs/referências semânticas de intenção. Não recebem gateway, sessão ou router inteiro.
- Modelos preservam IDs, versão/contexto e extensões desconhecidas, com cópias defensivas. Não carregam Node, Callable de submissão ou paleta.
- Atualização de dados, interação, estilo e layout são operações distintas. `apply_model` e `apply_visual_profile` não enviam comandos nem iniciam recuperação.
- Um componente possui os nodes que cria. Elementos em camadas externas são criados/possuídos pelo host dessa camada e conectados pela composição.
- O ciclo de vida encerra conexões, tweens, trabalho diferido e esperas pertencentes ao proprietário. Respostas assíncronas de um contexto antigo são descartadas.
- Posições de atores/hitboxes usam um espaço documentado de coordenadas da viewport. Conversões de escala/transformação ficam no adaptador de layout, não espalhadas em callbacks de input.

## 4. Implementação sequencial

### Etapa 0 — consolidar baseline e provas da auditoria

**Trabalho**

1. Transformar as reproduções temporárias da auditoria em testes permanentes: troca de definição de ator seguida de mudança visual; risco alto→normal; mapa em diamante; ator neutro.
2. Registrar árvores/camadas, identidade dos nodes relevantes, foco, scroll, seleção, cursor, payloads publicados e contagens de chamadas da fixture.
3. Capturar combate com mão cheia/vazia, preview, sidebar, pause; jornada, loja, recompensa, diálogo e término de run. Usar EN/PT, 1280×720, 1920×1080 e 2560×1080.
4. Acrescentar fixtures pequenas com terceira equipe, recursos desconhecidos, muitos atores/nós e conteúdo com texto longo.
5. Manter as baselines anteriores intactas; registrar a nova referência em diretório separado.

**Aceite:** falhas conhecidas estão descritas/reproduzidas, limites de design não são confundidos com falhas do transporte, e testes usam diretórios isolados. Não iniciar run no servidor pessoal para obter capturas.

### Etapa 1 — corrigir os dois defeitos reproduzidos

**Trabalho**

1. Dar a `ArtSlot` um modelo/aplicação pública contendo categoria e ID de conteúdo. Atualizar identidade e resolução de textura juntas.
2. Fazer a reconciliação de `ArtSlot` usar esse contrato, não somente copiar propriedades de `TextureRect`.
3. Em `RouteView`, derivar a aparência completa do estado atual em cada atualização. Remover/restaurar explicitamente o override de risco quando deixar de se aplicar.
4. Considerar os metadados de estilo-base usados por `AppTheme` ao restaurar cores; não permitir que alto contraste ou troca A→B→A recuperem um override antigo.

**Arquivos principais:** `art_slot.gd`, `view_reconciler.gd`, `route_view.gd`, testes de reconciliação.

**Aceite:** retrato não regride ao alternar idioma/escala/movimento/tema; risco alto→normal→alto mostra a aparência atual; updates preservam o node e o alvo correto. Nenhuma consulta REST extra por troca de estilo.

### Etapa 2 — tornar atualização e propriedade explícitas

**Trabalho**

1. Inventariar os componentes com script e definir quais têm modelo próprio. Reconciliar esses componentes por `apply_model`, sem substituir parcialmente seus campos internos por reflexão.
2. Restringir o reconciliador genérico a subárvores realmente passivas. Manter chaves semânticas para atores, cartas, ofertas e nós; não usar posição como identidade de componentes interativos.
3. Separar atualização de modelo de criação, remoção e reordenação. Retenção de identidade não deve conservar callbacks com dados obsoletos.
4. Definir contratos de mount/dispose para hosts, componentes e overlays. Modais devem guardar o escopo de origem e restaurar foco somente quando ainda válido.
5. Auditar criação, remoção e refresh sem exigir que todo componente seja uma cena `.tscn`. Construção dinâmica continua válida para conteúdo extensível.

**Aceite:** testes cobrem inserção/remoção/reordenação, payload atualizado em node retido, callbacks após descarte, cancelamento de voo/drag e contagem limitada de objetos após ciclos repetidos.

### Etapa 3 — estabelecer modelos e contextos visuais independentes

**Trabalho**

1. Estender os modelos existentes com `relationship`, `sideId`, identidade do ator controlado e chaves visuais. Não reduzir relação a um booleano como autoridade de apresentação.
2. Acrescentar modelos focados de cabeçalho, battlefield, HUD, mapa, resumo de atividade e oferta, reutilizando `view_models.gd`. Dictionaries continuam permitidos nas extensões da borda REST.
3. Definir `MapLayoutProfile` para geometria do grafo e um perfil/resolvedor de cenário para ambiente, fundo, artes e animações. Preservar `CombatLayoutProfile`, `CardVisualStyle` e `HandVisualStyle`.
4. Separar perfil de UI de perfil de cenário. Uma troca de fonte/borda da interface não deve mudar implicitamente o ambiente, e vice-versa.
5. Iniciar uma API de estilos vinculada ao contexto. Reutilizar a implementação de `AppTheme` como única fonte de construção de Theme/StyleBox, evitando um segundo sistema de estilos. A fachada global pode continuar como adaptador para telas ainda não migradas.
6. Resolver cenário por mappings JSON visuais usando IDs disponíveis: setting, run/nó e encontro quando publicado. Prioridade e fallback ficam em um resolvedor único, não em ifs dentro da view. O fallback atual de combate permanece suportado.

**Aceite:** mesmo snapshot pode ser projetado em dois contextos visuais sem contaminar cores/artes; relações desconhecidas têm representação neutra explícita; recursos/tags desconhecidos continuam visíveis. Preferências permanecem locais e não entram na identidade determinística da run.

### Etapa 4 — extrair a view do combate e corrigir donos dos overlays

**Trabalho**

1. Criar `CombatView` com composição/layout e uso dos componentes existentes. `CombatScreen` torna-se um adaptador fino de montagem/conexão, mantendo temporariamente a entrada pública do router.
2. A view recebe projeções e estado de interação; emite seleção, alvo, drop, inspeção e ações de controles por referência semântica. A coordenação resolve/submete os candidatos atuais.
3. `TurnControlsView` deixa de receber `overlay_host` e criar o botão de reconexão fora de sua árvore. Reconexão e opções contextuais pertencem ao host correspondente.
4. Fazer preview, sidebar, opções e mensagens terem aplicação de modelo própria. A composição conhece componentes públicos, não seus labels e botões internos.
5. Definir slots de composição e validar cenas inválidas antes de reparentar. Manter a prova de composição lateral sem duplicar mão/controller.
6. Reservar containers para fluxos da UI e anchors para overlays. Não disputar a posição de um node entre um container e offsets manuais.

**Aceite:** trocar layout preserva mão, seleção válida, foco, scroll, sidebar e cursor; remover controles não deixa siblings órfãos; pause bloqueia input de todas as camadas. Frames e comandos não são reenviados pela troca.

### Etapa 5 — retirar inspeção e ritmo de apresentação da tela

**Trabalho**

1. Extrair coordenação assíncrona de inspeção como colaborador do `CombatController`, sem nodes ou estilos. O cache pertence ao contexto da operação, não à árvore visual.
2. Compor sua chave com os elementos relevantes da requisição/versão: run, branch, sequência, combate/step, ator, revisão quando aplicável e alvos solicitados. Confirmar no contrato quais campos influenciam a resposta; não acrescentar dimensões arbitrárias.
3. Retornar estados explícitos de carregando/disponível/indisponível e projeções atualizadas. Inspeção atrasada de outro escopo não pode enriquecer cartas atuais.
4. Extrair `CombatPresentationDriver` que recebe o `Playback` existente, preferências de ritmo e estado de pausa. Próximo frame manual e automático usam o mesmo cursor.
5. `CombatFeedback` recebe os frames desse cursor e modelos de feedback. Fica responsável somente por efeitos transitórios, com remoção/cancelamento seguros.
6. Centralizar READY/SELECTING/SUBMITTING/ANIMATING/RECOVERING/PAUSED no estado de interação existente, evitando flags operacionais duplicadas na view.

**Aceite:** cache coerente com a requisição, nenhuma fila nova, zero comandos a partir de render/style; mudanças de tema não avançam frames. Testar saída durante inspeção/voo, pausa, reprodução manual/automática e mudança de escopo.

### Etapa 6 — separar cenário, artes de atores e HUD

**Trabalho**

1. Introduzir `RunSceneryHost` no shell e colocar nele a autoridade sobre ambiente e palco. O `EmberBackground` passa a ser uma implementação/fallback de cenário, não um fundo de gameplay administrado pelo router.
2. Criar `CombatSceneryView` para fundo, decoração e artes dos atores. Retirar o backdrop de `BattlefieldView`; múltiplas imagens no cenário são composição intencional de um único dono, não fundos concorrentes.
3. Desmembrar o ator em visual de cenário, indicadores de HUD e superfície de alvo. O cenário fornece anchors; a UI posiciona indicadores/hitboxes sem procurar nodes internos do cenário.
4. Manter a mesma intenção de alvo/carta e a validação dos candidatos publicados. Neutro/aliado/inimigo não define sozinho se um alvo pode ser selecionado.
5. Agrupar atores por identidade de equipe/relação publicada. Preservar inicialmente o layout de duas equipes, mas representar neutros e terceiros sem chamá-los “YOUR SIDE”.
6. Roteamento de feedback usa `instanceId`; animação local não altera posição/estado autoritativo nem calcula dano.
7. Preservar a ordem de camadas: cenário abaixo do HUD, floating abaixo de modal, notificações acima. A arte não captura input; superfície de alvo é explícita e estável.

**Aceite:** trocar somente cenário não reconstrói a mão/HUD ou muda a seleção; trocar somente UI não restaura/recolore o cenário. Testar atores neutros, múltiplas equipes, tamanhos de arte, pausa e alvos em telas ultrawide. Não exigir Node2D/Node3D: Control é válido para a implementação inicial.

### Etapa 7 — dividir jornada e atividades

**Trabalho**

1. Extrair `JourneyView`, cabeçalho/recursos, mapa, resumo da atividade e host de atividades. `ActivityScreen` vira adaptador de composição.
2. Extrair views focadas para ofertas de cartas, ofertas genéricas/loja, preparação, transformação, diálogo e resultado. Reutilizar `CardView`, `DialoguePanel`, `ResourceView` e inspeções existentes.
3. Cada view recebe opções/modelos e emite a referência da escolha. Confirmation, preview de transformação, submit, recover, verify e avanço automático ficam sob coordenação do `ActivityController`.
4. Remover estados duplicados de verifying/recovering/auto-advancing da tela e do controller. O router continua responsável por navegação.
5. Atualizar componentes diretamente. Deixar de criar uma segunda instância completa de `ActivityScreen` em cada alteração para usar como árvore desejada.
6. Escolher o componente de atividade por discriminadores publicados, com um fallback genérico que mantém opções desconhecidas utilizáveis. Não associar visual a uma permissão inventada.
7. Mudanças exclusivamente visuais não podem acionar recuperação/auto-advance. Uma atualização de sessão pode fazê-lo pela coordenação, conforme as opções publicadas e o comportamento atual.

**Aceite:** todas as atividades existentes mantêm os mesmos payloads, preços publicados, confirmação e recusa. Trocar tipo de atividade descarta apenas o painel correspondente; atualizar ouro/texto/oferta não reinicia o mapa nem perde foco válido. Avanço forçado continua único e respeita pause.

### Etapa 8 — implementar layout de mapa como grafo

**Trabalho**

1. Criar `MapLayout` puro: recebe nós/arestas visíveis e perfil de geometria; retorna posições, dimensões e traçados. Não recebe sessão nem decide travel.
2. Para DAGs, distribuir nós em camadas e rotas paralelas com desempate estável por ID. Evitar dependência da ordem incidental do array e uso de RNG.
3. Tratar grafos cíclicos, desconectados, atalhos e arestas de retorno com fallback visual estável. Não exigir que todo setting use o formato do mundo 1.
4. Cachear geometria pela topologia/perfil/tamanho disponível. Atualizar atual/resolvido/risco/disponibilidade sem mover o mapa a cada comando.
5. Adaptar `RouteView` para consumir layout/contexto e componentes de nó por `nodeId`. Desenhar arestas direcionais e apresentar riscos/tipos sem depender apenas de cor.
6. Definir scroll/foco quando houver overflow, sem diminuir indefinidamente textos ou hitboxes. Hotreload de topologia preserva o nó focado e posição de leitura quando possível.
7. Somente escolhas publicadas habilitam navegação. Layout, adjacência visual, nó visitado ou aparência não criam permissões.

**Aceite:** fixtures de linha, diamante, múltiplas raízes, retorno/ciclo, nós desconectados e mapa grande. Mesma topologia mantém posições ao mudar recursos e disponibilidade; ramificações são legíveis em 1280×720 e 2560×1080. IDs inválidos recebem diagnóstico de apresentação, não uma “correção” do estado da run.

### Etapa 9 — concluir migração de estilo e remover caminhos duplicados

**Trabalho**

1. Migrar componentes da run restantes para estilo/Theme/paleta do contexto. `RouteView` deixa de ler o tradutor global; efeitos e cenário deixam de consultar preferências/cores globais diretamente.
2. Preservar políticas especializadas: borda por raridade, cores configuradas de comparação numérica, recursos/custos, geometria da mão e anatomia da carta. Não transferi-las arbitrariamente para o tema geral.
3. Usar recursos de estilo locais para dimensões relevantes. Não transformar todo número de margem em configuração global, nem persistir geometria na engine.
4. Remover branches de reconciliação, helpers, flags e construção de árvore que ficaram sem consumidores após a migração. Não conservar uma “tela legado” em paralelo.
5. Atualizar testes de fronteira: views/componentes sem `GameSession`, `Preferences`, `I18n` global ou HTTP; controllers sem construção de nodes/tema; cenário sem submissão.
6. Menus já em Marfim continuam com a mesma fonte de estilos e aparência. A migração de dependências não exige redesign ou seletor de skins público.

**Aceite:** troca A→B→A sem cores/fontes/artes obsoletas, dois contextos simultâneos sem contaminação, cenário independente da skin, código antigo removido somente após provar substituição dos consumidores.

### Etapa 10 — certificação e documentação

**Trabalho**

1. Reexecutar suites de contratos, componentes, reconciliação, lifecycle, composição, mão, pause, resoluções, settings, progressão por setting e integração com fixtures de REST.
2. Cobrir default, ascendant e volatile usando fixtures/contratos disponíveis, sem presumir energia como único recurso, duas equipes ou mapa linear.
3. Testar a sequência completa: menu→run→mapa→atividade→combate→recompensa→mapa→resultado, além de pause, inspeção, reconexão e respostas atrasadas. Usar servidor de QA isolado para qualquer teste que crie runs reais.
4. Comparar capturas e métricas com a etapa 0: objetos criados/substituídos, relayouts, consultas de inspeção e tempo de refresh local. Não apresentar tempo de layout como latência de rede ou benchmark de GPU.
5. Incluir testes adversariais das falhas da auditoria e de navegação durante awaits, topologia alterada, modal ativo e redução de movimento.
6. Atualizar `GODOT_VISUAL_PROFILES.md`, contratos de componentes/cenário/mapa e a documentação de testes. Registrar encerramentos nativos/avisos de recursos independentemente do resultado dos asserts.

**Aceite final:** regras/payloads/identidades determinísticas não mudam; nenhuma regressão funcional; metas de identidade, input, ciclo de vida e legibilidade comprovadas. O mundo visual pode mudar sem substituir a integração ou copiar o combate/mapa.

## 5. Ordem, marcos e limites de conclusão

Executar **0→1→2→3→4→5→6→7→8→9→10**. Cada etapa depende dos contratos da anterior; não migrar todos os consumidores ao mesmo tempo.

- **Marco A — etapas 0–2:** defeitos corrigidos e atualização/ciclo de vida seguros.
- **Marco B — etapas 3–6:** combate com UI e cenário substituíveis separadamente, preservando a mão e o playback.
- **Marco C — etapas 7–8:** jornada modular e mapa ramificado legível, mantendo as permissões da engine.
- **Marco D — etapas 9–10:** migração concluída, caminhos antigos removidos e certificação. Só então expandir o Marfim sobre a run.

Não concluir uma etapa apenas por reduzir linhas. Deve haver contrato público, responsabilidade única, testes de invariantes e remoção do comportamento substituído. Cada marco precisa continuar jogável antes do próximo.

## 6. Riscos e mitigação

- **Perder foco/seleção durante reparent:** guardar estado local por IDs e adiar composição durante submit/drag; não reenviar ações.
- **Callbacks obsoletos em node retido:** resolver intenção contra o modelo atual; não capturar dictionaries de uma árvore temporária.
- **Tema provocar HTTP/reprodução:** separar atualização de estilo de workflow e testar contadores/cursor.
- **Neutralidade virar regra de alvo:** relações servem ao desenho; seleção depende de candidatos publicados.
- **Criar um renderer universal prematuro:** oferecer contrato simples para o cenário Control atual; adicionar outras implementações somente quando houver um consumidor.
- **Layout de grafo interferir no determinismo:** geometria é derivada local, sem RNG da run e sem posições em envelopes de comando.
- **Remover caminhos ainda usados por menus/lab/replay:** inventariar consumidores e manter um adaptador transitório explícito, não uma cópia de lógica.

## 7. Referências

- [Contratos visuais atuais](../design/GODOT_VISUAL_PROFILES.md).
- [Menus Marfim e pausa](../design/IVORY_MENUS_AND_PAUSE.md).
- [Configurações e suas regressões](../design/GODOT_SETTINGS_BEHAVIOR.md).
- [Organização de cenas — Godot](https://docs.godotengine.org/en/stable/tutorials/best_practices/scene_organization.html): componentes focados, dependências explícitas e mediação pelo proprietário.
- [CanvasLayer — Godot](https://docs.godotengine.org/en/stable/classes/class_canvaslayer.html): separação das camadas de apresentação.
