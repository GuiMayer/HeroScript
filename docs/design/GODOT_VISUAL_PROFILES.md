# Camada visual substituível — Godot

Implementação: 2026-10-11. Cliente: `examples/godot-engine-showcase`.

Este guia descreve a infraestrutura entregue pelo [plano de arquitetura](../plans/GODOT_UI_ARCHITECTURE_REFACTOR_PLAN.md). Menu, pausa, laboratório e run usam Marfim. A run possui perfis independentes `ivory_run.tres` / `ivory_scenery.tres`; a carta usa a anatomia compartilhada, com arte mais compacta no perfil de gameplay para acomodar fontes ampliadas. A composição lateral permanece uma fixture de teste; não existe seletor público de skins.

## Donos e fronteiras

| Responsabilidade | Dono | Não pertence aqui |
| --- | --- | --- |
| HTTP, envelopes e concorrência | `EngineGateway` / `GameSession` | Nodes, tema, animações. |
| Fatos exibíveis e opções publicadas | Presenters / `view_models.gd` | Cores, fórmulas, envio. |
| Seleção, envio e recuperação | `CombatController`, `ActivityController` | Construção de controles ou alvos inventados. |
| Validação/persistência de preferências | `SettingsController` + `SettingsSchema` | Regras da run. |
| Geometria/gestos da mão | `HandLayout`, `HandInteraction`, `CardHandView` | Segunda seleção autoritativa ou limite de cartas. |
| Cursor dos receipts | `Playback` existente | Tema ou controller visual criando outra fila. |
| Camadas, pausa, foco e toast | `ApplicationShell` | Comandos ou decisões de progressão. |
| Dados visuais locais | `VisualContext` | Referência à sessão ou gateway. |

Os modelos fazem cópias defensivas e preservam extensões desconhecidas. `Card`, `Actor`, `ResourceValue`, `ActionOption` e `Combat` são pequenos records; não existe binding bidirecional nem alteração da sessão por um model. A referência de uma opção é local ao contexto publicado e nunca substitui o ID de comando da engine.

## Alterar a aparência

1. Crie um Resource com `scripts/ui/styles/visual_theme_profile.gd`, preferencialmente copiando `data/themes/preserved.tres`.
2. Sobrescreva papéis em `colors`: por exemplo `text.primary`, `text.secondary`, `surface.canvas`, `surface.panel`, `accent.primary`, `surface.hover` e `surface.modal_scrim`. Papéis ausentes usam a paleta preservada.
3. Configure `body_font`/`title_font` (Resources Font opcionais), `body_font_size`, `font_sizes` por papel (`screen_title`, `section_title`, `body`, `caption`), `screen_margin`, `corner_radius` e `art_manifest`. Use uma cópia do recurso de fonte ao trocar sua configuração; mantenha licenças dos arquivos.
4. Se necessário, use `setting_art_manifests` para selecionar um manifesto visual por setting. Isso não muda a revisão de conteúdo da engine.
5. Aplique com `Main.apply_visual_profile(profile)`. A aplicação atualiza o tema nativo, o contexto e as views ativas, sem criar run ou consultar HTTP.

`AppTheme` é a fachada única do perfil ativo da aplicação. `PrimaryAction`, `SecondaryAction`, `DangerAction`, `ScreenTitle`, `SectionTitle` e `Caption` usam variações nativas de Theme. Estilos locais duplicam StyleBox antes de alterá-lo. Escala é calculada sobre valores canônicos, evitando multiplicação sucessiva e restauração de cores de um perfil anterior.

A fachada da aplicação tem um perfil ativo; componentes injetados e modais podem possuir contextos simultâneos independentes. A mesma factory de estilo constrói seus Themes locais, sem acesso à sessão.

Na migração incremental, o router aplica Marfim nas rotas de menu/configurações/histórico/códice/laboratório e no replay vindo do histórico; restaura o perfil de gameplay nas rotas da run. `Main.apply_visual_profile()` também define o perfil de gameplay usado nas próximas navegações. A pausa usa contexto derivado e Theme nativo próprio, construídos/reaplicados por `AppTheme.scoped_profile()` síncrono, sem mudar o perfil global ou a geração do gameplay. A composição do menu principal está em `MainMenuView`, com geometria em `MenuVisualStyle`, sem callbacks REST ou referências à sessão dentro da view.

### Políticas especializadas

- `CardVisualStyle`: raridade/borda, comparação numérica branco/vermelho/azul, face e limites de tooltip/inspeção. Não migre essa política para o tema global.
- `HandVisualStyle`: largura mínima selecionável, leque, elevação, duração e limiar de drag. Não define capacidade da pilha nem limite de cartas.
- `VisualContext.set_resource_styles(presentation.resources)`: aparência dos custos/recursos, separada de quantidade e efeito. Um recurso desconhecido usa aparência neutra; a engine continua definindo sua semântica.
- `VisualThemeProfile.resource_color_overrides`: paleta visual opcional por resource, aplicada em custos/HUD/sidebar; IDs desconhecidos preservam a apresentação publicada. `actor_art_max_height` limita somente a geometria do placeholder.
- `ResourceView`: só usa barra quando o snapshot publica limites válidos e o valor está dentro deles. Valores sem limites ou fora deles permanecem visíveis como texto; não são truncados nem tratados implicitamente como vida.
- Ofertas de cartas mostram o preço de aquisição fora da face. Sem custo de uso publicado, mostram “custo indisponível”; preço da loja não é interpretado como custo de jogar a carta.

## Alterar a composição

O combate usa `scenes/ui/combat_composition.tscn` e `data/themes/bottom_preview.tres`. A cena fornece `CombatBody/CombatGameplay`; os componentes reutilizáveis ocupam os slots de campo, HUD inferior e camadas flutuantes.

`CombatLayoutProfile` define posição/largura do preview, sidebar, alça e alternativas. Para outra disposição:

1. Crie outra composição mantendo o slot `CombatGameplay` como Control dentro do corpo HBoxContainer.
2. Crie outro perfil de layout, sem regras, capacidade de cartas, limites de jogo ou IDs de comandos.
3. Use `CombatScreen.replace_composition(scene, layout_profile)`. A view move os mesmos componentes vivos; não duplica o controller nem a mão.
4. Envios/gestos críticos adiam a troca até um ponto seguro. Foco válido, scroll, seleção e estado da sidebar são conservados; o cursor de playback não é avançado.

A prova `combat_lateral_test.tscn` + `lateral_preview_test.tres` desloca o preview para a camada flutuante. Uma composição inteiramente diferente pode reorganizar os mesmos componentes e sinais no coordenador; o contrato não é um editor genérico capaz de interpretar qualquer cena arbitrária.

## Componentes e intenções

`ScreenView` oferece `mount`, `apply_model`, `apply_interaction`, `apply_visual_profile`, captura/restauração local e `dispose`. Views simples implementam somente as operações necessárias; combate/jornada aplicam projeções incrementalmente.

O combate é composto por `CombatHeaderView`, `BattlefieldView`, `CharacterSidebarView`, `ActionPreviewView`, `TurnControlsView` e a mão compartilhada. Seus sinais representam intenção (timeline, pausa, alvo, cancelamento, fim do turno), nunca resultados calculados.

`CardView`, `CardHandView`, `ActorPanel`, `ResourceView`, `ArtSlot`, detalhes e inspeções podem ser instanciados com `VisualContext.new(...)`, sem autoloads de gameplay. O laboratório injeta o mesmo contexto e aparência dos recursos. O overview/diálogo também herdam esse contexto, inclusive arte por setting e preferências de acessibilidade.

O controller usa candidatos atuais publicados. Pagamentos e alvos alternativos continuam separados; a skin não escolhe implicitamente o primeiro candidato. Uma aceitação limpa a seleção; rejeição mantém o contexto para recuperação. Os epochs invalidam respostas de views descartadas.

## Atualização sem reconstrução geral

CombatScreen e ActivityScreen adaptam a sessão; CombatView, JourneyView e ActivityOptionsView desenham projeções passivas. Projeções iguais são ignoradas. Atores são reconciliados por instance ID; escolhas por referência contextual/payload; mapa, mão e controles estáveis conservam identidade, foco e scroll.

`ViewReconciler` mantém os sinais no node vivo. Para subtrees alteradas, algumas views montam uma árvore desejada fora da tela e aplicam somente diferenças; isso ainda aloca nodes temporários, mas não faz uma árvore viva vazia nem duplica autoridade. Não é uma promessa de zero alocações para qualquer mudança.

Views emitem referências de intenção, resolvidas contra o controller atual. ActivityScreen não é mais clonada para montar uma árvore desejada: cabeçalho, resumo e opções possuem regiões independentes. Diálogo lê o comando atual do botão retido e recompõe suas opções por ID. Remover uma opção realmente ausente é uma atualização legítima, não uma regressão visual.

## Artes e ciclo de vida

RunSceneryHost pertence ao shell e possui ambiente e CombatSceneryView. BattlefieldView possui HUD/superfícies de alvo, não as artes. Equipes e relações não são reduzidas a hostile. O renderer Control usa anchors públicos no espaço da viewport para alinhar arte e alvo, sem dar input à arte. SceneryVisualProfile/mappings JSON e perfil da UI são independentes.

VisualContext possui factory de estilo/Theme locais, usando a mesma implementação da fachada AppTheme. MapLayoutProfile controla o grafo; MapLayout calcula posições e traçados sem sessão ou permissões, agrupando ciclos por SCC. RouteView permite inspecionar qualquer nó publicado; somente a ação separada do inspector pode viajar por uma escolha atual. Pan/overview/filtro não executam comandos.

CombatInspectionCoordinator limita/coalesce leituras por contexto, revisão e alvos. CombatPresentationDriver controla o intervalo sobre o Playback existente; render/tema não reiniciam o ritmo nem criam outra fila. FloatingHUD possui slots explícitos para preview, sidebar, alternativas e reconexão.

O manifesto associa IDs a slots estáveis; ilustração e hitbox continuam independentes. `ArtCatalog` mantém manifests por caminho e usa o cache nativo de ResourceLoader para texturas vivas. `invalidate(path)` invalida um manifesto, `clear()` invalida todos. Para substituir um arquivo na mesma rota, reimporte/recarregue a textura ou reinicie o cliente; invalidar só o JSON não força reimportação de arte.

`ApplicationShell` possui as camadas gameplay/floating/modal/notification. `CombatFeedback` consome o cursor existente e possui os efeitos transitórios. `dispose`/saída desconecta sinais e cancela gestos/tweens; fechar feedback durante um voo libera a espera. Modais são filhos do contexto que os abriu e restauram foco somente em nodes ainda válidos.

### Auditoria dos overrides

| Exceção | Motivo/dono |
| --- | --- |
| Raridade e branco/vermelho/azul dos números | Contrato explícito de `CardVisualStyle`. |
| Cores de recursos/custos | JSON visual injetado, não cor em `CardViewModel`. |
| Fontes compactas da face, ferramenta e indicador | Papel local de legibilidade; tamanho-base escalado uma vez. |
| Margens de arte, alça, toolbar e área clicável | Anatomia/layout local; não regra de jogo. |
| Transparente/branco de modulação e sombra | Composição/feedback, não paleta duplicada. |
| Cores de desenho em `ActorPortrait` e partículas de `EmberBackground` | Arte placeholder procedural; trocar/substituir como arte, não regra. |
| Highlight de alvo, modal, BBCode diagnóstico | Papel semântico do perfil. |

Novas cores arbitrárias repetidas fora desses donos devem virar papéis; não transforme toda dimensão local em uma configuração global sem consumidor real.

## Verificação

`tools/test-godot-ui.ps1` importa/testa uma cópia com user directory exclusivo. `-ApiUrl` aponta testes online para um servidor de QA; não use o servidor das partidas pessoais para testes que criam runs. Erros de script, asserts e RID interrompem o runner. Diagnósticos de recursos residentes ao encerrar continuam no relatório e exigem revisão independente do código de saída.

A certificação e as novas capturas estão em [interface-refactor/2026-10-11](interface-refactor/2026-10-11/README.md). A [baseline original](interface-baseline/2026-10-11/README.md) permanece intocada. Medidas de refresh síncrono não medem rede/GPU nem tempo clique→resultado.

A continuação específica de combate/jornada está nos [contratos da run](GODOT_RUN_GRAPHICS_CONTRACTS.md) e na [comparação de 2026-10-12](run-graphics-after/2026-10-12/README.md). A [certificação Marfim](ivory-run/2026-10-12/README.md) registra a ativação, novas capturas e testes REST em QA isolado normal, com seus limites explícitos.

Referências nativas: [Theme e variantes](https://docs.godotengine.org/en/stable/tutorials/ui/gui_skinning.html), [organização de cenas](https://docs.godotengine.org/en/stable/tutorials/best_practices/scene_organization.html) e [retenção de static variables](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/gdscript_basics.html#static-variables).
