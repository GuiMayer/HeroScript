# Camadas do cliente Godot

## Direção das dependências

```text
ui/views → ui/controllers → application/GameSession → engine/EngineGateway → engine/HTTPTransport → REST
	↑          ↑
	└─ presentation/Presenters + ViewModels (cópias defensivas)
	└─ VisualContext + Theme/Layout/Art resources (somente apresentação)
			│
			└─ presentation_ready → envelope → planner → Playback/director → handles → views

bootstrap.gd: instancia transporte/gateway e conecta sessão, playback e preferências
presentation/I18n: traduz mensagens apenas na borda visual
```

`ui/` contém o visual substituível. Nenhuma tela importa o gateway/transporte,
conhece URLs, instancia `HTTPRequest` ou monta envelopes de concorrência.
Não há autoload público de transporte: o nó HTTP é privado da composição.

## Responsabilidades

O estudo aplicado à apresentação está em
[Slay the Spire: gameplay e UX](../../docs/research/SLAY_THE_SPIRE_GAMEPLAY_AND_UX.md).
O novo `ActivityPresenter` relaciona ofertas, preços e navegação anunciados;
`ArtCatalog`/`ArtSlot` resolvem somente visuais pelo
[contrato de slots](assets/ART_SLOTS.md).

| Camada | Cuida de | Não pode fazer |
| --- | --- | --- |
| Transporte HTTP | conexão, JSON, timeout, status, medições limitadas | traduzir, ler preferências, chamar telas |
| Gateway da engine | rotas, queries, schemas, IDs de comando, normalização de payload | animar, calcular regras, salvar preferências |
| Sessão | snapshots, leitura coordenada, comandos, versão esperada e erros de sincronização | construir URLs, tocar áudio, traduzir mensagens, avançar animações |
| Escolhas de progressão | transformar opções anunciadas em escolhas enviáveis | inventar IDs, preços ou regras de upgrade |
| Envelope/planner | validar escopo e organizar fatos em beats causais sem reordenar aplicações | executar efeitos ou revelar zonas ocultas |
| Playback/director/handles | cursor, execução, milestones, pause, skip e conclusão única | enviar comandos, recalcular efeitos, alterar snapshots |
| Presenters | indexar snapshots, descrever aplicações canônicas e organizar histórico/linhagem | consultar HTTP, executar regras ou modificar a sessão |
| Controllers de tela | seleção, intenção publicada, envio, validação local e epochs | construir nodes/estilos, HTTP ou cálculo de regras |
| Contexto visual / perfis | tradução, preferências projetadas, papéis, geometria e artes | conhecer gateway ou alterar envelopes |
| Componentes visuais | apresentar atores/resources/cartas e emitir intenção de input | escolher alvos legais ou decidir o resultado do comando |
| UI | seleção, layout, navegação, tradução, áudio, input e ritmo de apresentação | declarar o resultado de uma regra |
| Bootstrap | injetar gateway e ligar sinais às preferências/playback | implementar regras ou desenhar telas |

As projeções públicas da sessão são cópias profundas. Uma tela pode organizar
seus dados sem modificar os snapshots usados para sincronização e envio.
Em combate, a sessão publica estado, zonas, ações legais e `card_evaluations`
juntos, após verificar run, sequence, combate, step e revisão do conteúdo. A
inspeção da mão não é uma segunda atualização visual: a tela recebe os valores
calculados antes de montar as cartas. Durante a leitura, mantém-se a última
apresentação com input bloqueado; falhas mantêm essa apresentação em estado
dessincronizado até reconectar. Invalidação/troca de setting elimina as inspeções.
O componente `CardView` reconcilia sua face por identidade e ignora alterações
de `inspectionText` para renderização, mantendo esse diagnóstico no modelo.
As projeções continuam tendo o formato dos read models da engine: esta divisão
desacopla transporte e ciclo de vida, não promete compatibilidade com qualquer
mudança futura de contrato de dados.

## Trocar o visual

1. Para uma skin, use `VisualThemeProfile`, `CombatLayoutProfile`, manifesto e
   componentes existentes. `Main.apply_visual_profile` e
   `CombatScreen.replace_composition` são as entradas de substituição. Consulte
   o [guia de perfis e composição](../../docs/design/GODOT_VISUAL_PROFILES.md).
   Uma UI completamente nova também pode reutilizar os serviços do bootstrap.
2. Leia `GameSession.run`, `combat`, `legal_actions` e `activity_choices()`.
   Faça a seleção e renderização com os novos componentes visuais.
3. Nas telas existentes, delegue aos controllers de combate/atividade;
   eles usam `submit_candidate(candidate)` e `submit_activity(choice)`.
   A sessão/gateway cuidam do
   payload, versão observada e identidade do comando.
4. Use `start_campaign`, `start_sandbox`, `continue_run`, `timeline`,
   `branch_tree`, `create_branch`, `verify` e `simulate_end_turn` como casos de
   uso. O editor de sandbox pode enviar JSON de cenário; não executa suas regras.
5. Observe `changed`, `failed`, `command_started`, `command_finished` e
   `availability_changed`. Erros estruturados são formatados por `I18n.error`
   somente quando apresentados. Diagnósticos crus do servidor são preservados.
6. Consuma `presentation_ready`, emitido somente após sincronização completa.
   O recibo recebido antes disso é confirmação factual, não autorização para
   começar impactos. O adaptador ativa o envelope e registra o executor de beats;
   cada executor conclui seu handle. O director coordena avanço, pause e skip.
   Input e navegação aguardam `has_blocking_work()`, não apenas frames restantes.

   O director drena operações instantâneas iterativamente, com geração e orçamento
   de CPU. A confirmação manual depende da capacidade visual resolvida. Impactos
   vivos/históricos compartilham `EffectMotion`, que aguarda apenas tweens iniciados.
   `CardUseMotion`, `CardZoneMotion` e `CardTransitRegistry` são compartilhados com
   o laboratório: a mão reserva chegadas e empresta a instância durante o voo.
   As curvas, tempos e limites ficam em `data/motion_manifest.json`.

Nenhuma preferência gráfica deve entrar em um envelope de jogo. No fluxo
atual, a sessão conserva o snapshot final autoritativo. As views podem consumir
`StateAfter` publicado por frame e interpolar `previousValue/currentValue`.
Na ausência desses estados, mostram fatos e reconciliam no fim; não simulam
regras para reconstruir estados intermediários.

O replay visual e a timeline possuem Playback/director descartáveis. Carregam
um commit autorizado por vez, inclusive de combates encerrados, sem consultar o
combate atual ou enviar comandos. Não reconstroem mãos ocultas. Consulte o
[guia de autoria da apresentação](../../docs/design/COMBAT_PRESENTATION_AUTHORING.md)
e o [relatório de validação](../../docs/plans/GODOT_COMBAT_ANIMATION_VALIDATION_REPORT.md).

## Ciclo de vida das telas e snapshots

Atualizar o snapshot não significa reconstruir a navegação. Enquanto o contexto
permanece o mesmo, `ui/main.gd` conserva a instância de `CombatScreen` ou
`ActivityScreen`, `MapScreen` ou `DialogueScreen` e chama `refresh_state(presentation)`. A tela cria um presenter
novo sobre cópias defensivas do snapshot atual e reconcilia a composição por
identidade: atores, ofertas, mapa e mão conservam os nodes válidos. Algumas
subtrees alteradas usam uma árvore desejada temporária, nunca uma segunda view
ativa. Uma projeção igual não recompõe a árvore. A troca de instância fica
reservada para uma transição real de contexto.

Regras de implementação:

- mapa, ofertas e diálogo possuem cenas/coordenadores separados; não há um painel narrativo operacional embutido nas ofertas;
- `DialogueReading` e `MapCamera` guardam leitura/gesto local, sem acesso à engine; histórico não dá rollback e páginas não são nós de gameplay;
- uma opção de diálogo é contextual à run/branch/sequence/instância/nó/revisão; opções antigas ou de outra conversa não são enviadas;

- identidade de tela, foco semântico e posições de scroll pertencem à UI, não à
  run;
- animação de entrada é executada somente quando o componente entra no contexto,
  não em todo refresh;
- áreas contextuais reservam geometria para que visibilidade não desloque
  controles estáveis;
- preferências visuais são aplicadas ao subtree novo antes de ele ser desenhado;
- leituras assíncronas carregam um `render_epoch`; respostas de uma geração
  anterior são descartadas;
- `CardView.configure()` termina com o subtree já escalado, inclusive quando a
  inspeção REST acrescenta raridade, buffs, upgrades ou fontes contextuais;
- uma carta removida, um recurso alterado ou a mudança de atividade podem mover
  conteúdo por consequência do novo snapshot; elementos não afetados não devem
  piscar ou repetir animações.

Esse ciclo continua passivo: persistir uma tela não transforma nodes em estado
autoritativo. A cada refresh, os valores renderizados vêm novamente de
`GameSession`/presenters.

`CombatPresenter` recebe snapshot e tradutor por injeção. `ActorPanel` e
`ResourceView` servem tanto ao combate atual quanto à inspeção histórica.
`TimelinePresenter` mantém paginação e metadados; o gateway normaliza a forma
dos agregados históricos. A tela histórica possui seu próprio `Playback`.
O snapshot mostrado é pós-comando, não um estado reconstruído de cada frame.

A sessão serializa operações e rejeita leituras de gerações descartadas.
Respostas incertas preservam o envelope em memória para recuperação idempotente;
não há promessa de reenvio automático após fechar o processo. A UI separa
estados de seleção, envio, reprodução, recuperação e pause das regras de turno.

`ApplicationShell` possui host, pausa, notificações e restauração de foco.
`CombatFeedback` possui somente os efeitos transitórios dos frames do playback.
Overview, inspeção e diálogo recebem o mesmo `VisualContext` que seus pais.
`dispose()` invalida coordenação/epochs e encerra os recursos locais da view.
Fechar o feedback durante uma animação de carta também libera sua espera.

O schema de configurações está separado da construção de seus controles;
setters/persistência pertencem ao `SettingsController`. Histórico/replay/timeline,
sandbox e códice compartilham o contrato `ScreenView`, sem inventar um controller
para páginas que só precisam de consulta e navegação.

## Injeção e testes

`GameSession.configure(gateway)` recebe a dependência; `EngineGateway.new(transport)`
recebe o transporte. A sessão não depende de autoloads de preferência, áudio,
tradução ou visual. O teste `tests/layers.gd` instancia a sessão com um transporte
em memória e sem cena principal. Uma futura implementação alternativa de gateway
deve fornecer os métodos atualmente usados por `game_session.gd`.

O bootstrap é o único lugar que liga `Preferences.api_url` ao transporte e
persiste o ID da run aberta. Retomar uma branch também atualiza esse ID. Abrir
outra run limpa o cursor visual; isso não altera os commits da run anterior.

Uma falha no refresh após POST bem-sucedido não transforma o comando em
fracasso: ele já foi persistido. A sessão bloqueia input desatualizado até
`refresh()` e não reenvia automaticamente o comando com outro ID.

## Localização e fallback

Inglês é fonte e fallback explícito, inclusive na configuração nativa da Godot:

```jsonc
// en.json
{"NEW JOURNEY": "NEW JOURNEY"}
// pt_BR.json
{"NEW JOURNEY": "NOVA JORNADA"}
```

As chaves são as próprias mensagens em inglês; placeholders são mantidos.
`I18n.text("NEW JOURNEY")` resolve a tradução escolhida, depois inglês e, por
último, a própria chave. Catálogos ausentes não impedem carregar a interface.
`content.json` usa IDs de conteúdo estáveis e nomes por idioma; falta de nome
localizado prioriza o inglês antes do nome fornecido ou ID formatado.

Preferência válida previamente salva é respeitada. Locale não suportado volta
para `en`. Trocar idioma não modifica conteúdo publicado, snapshots ou receipts.

## Validação desta separação

- Testes offline de fallback, transporte injetado e dependências proibidas.
- Cópias defensivas, normalização de payload e versões esperadas.
- Commit bem-sucedido seguido de falha na leitura, bloqueio e recuperação.
- Playback sem chamadas à engine e sem mutação de run.
- Teste de UI com REST real: carta/alvo, idiomas, pause, frames e replay.
- Campanha completa, sandboxes, timeline, branches e simulações pelo mesmo
  serviço de aplicação usado nas telas.
- Regressões de ciclo visual: identidade persistente de Combat/Journey,
  geometria contextual, scroll do mapa, fonte após inspeção e animação de entrada
  somente no primeiro render.

O teste estático de fronteiras impede dependências de HTTP na UI e dependências
visuais na sessão/gateway. Ele complementa os testes funcionais; não substitui
uma revisão arquitetural ao introduzir novos subsistemas.
