# Plano de implementação de runas de sobrescrita, resolução em cadeia e menus de jogo

Data: 22 de novembro de 2026. Status: plano para implementação e aprovação de design. Escopo de produto: Alisyum e Tactical Campaign MVP (`prototype-test`), fluxo normal jogável. Não inclui cenas de laboratório e settings auxiliares como produto, embora sejam úteis para QA.

Referências: [diagnóstico do core](PESQUISA_PLANO_CORE_BUILDS.md) e [estudo de runas e cadeias](ESTUDO_RUNAS_RESOLUCAO_CADEIA.md).

## 1. Objetivo e experiência final

O jogador deve encontrar uma runa, pegá-la fisicamente com o mouse, arrastá-la diretamente sobre uma carta da sua coleção e ver a carta se transformar. No próximo combate, deve perceber a característica substituída e descobrir que ela interage com outras cartas, inscrições, status e recursos. Uma ação pode resolver em uma sequência de efeitos reconhecíveis, inclusive com resultados muito superiores ao valor isolado das peças.

Os menus precisam abandonar o aspecto de ferramenta de administração. A oficina é uma bancada de construção de builds; o inventário é uma coleção de objetos; as recompensas são descobertas; o mapa é a escolha da próxima aventura. A estética permanece abstrata e sem ilustrações obrigatórias: silhuetas, símbolos rúnicos, tipografia, placas, contraste, movimento e áudio são suficientes. Não confundir ausência de arte com ausência de direção visual.

**Resultado mínimo:** em uma sessão curta, experimentar a primeira modificação antes do primeiro combate; usar uma carta com duas runas complementares; ver uma cadeia com pelo menos três peças; entender sua causa e desejar testar uma composição diferente.

## 2. Decisões de design

### 2.1 O que uma runa modifica

Uma runa incide sobre uma característica específica da definição efetiva de uma instância. O nome da carta e seu núcleo permanecem reconhecíveis. A transformação deve deixar explícito o que foi substituído e o que foi preservado.

| Característica | Exemplo original | Transformação |
| --- | --- | --- |
| Afinidade | Consequência física/neutra ou fogo | Residual de fogo ou gelo, substituindo a consequência anterior |
| Forma | Um impacto | Fragmentação em múltiplos impactos de um orçamento compartilhado |
| Consequência | Aplicar status | Consumir status elegível para detonação |
| Continuação | Excedente perdido | Transbordo para outro alvo após abate causal |
| Disposição | Ir ao descarte | Retorno ao topo sob regra explicitamente suportada |
| Vínculo | Não reage a evento | Reage a um fato elegível, com uma janela/escopo definidos |

Reutilizar as categorias existentes: um slot de afinidade e dois espaços de comportamento em Alisyum. Forma, consequência, continuação e vínculo são papéis de design e apresentação dentro de Behavior; não criar novas categorias da engine sem necessidade. MVP deve declarar seus próprios slots, mantendo sua composição de deck e regras de pagamento. Uma runa que substitui uma característica pode aplicar múltiplos patches, mas continua sendo uma única peça para o jogador.

Propriedades independentes podem coexistir. Duas runas que escrevem na mesma propriedade de forma incompatível não devem ser anunciadas como equipáveis. Evitar resolver colisão por “a última vence” sem explicar: excluir por capabilities/requisitos, ou autorizar explicitamente uma ordem e mostrar o resultado. A ordem de aplicação atual é parte da semântica; a UI não a altera.

### 2.2 Regras de equipamento

- A primeira entrega usa o comportamento `Bind` existente: cada unidade tem uma identidade concreta; ao aplicar, fica vinculada à carta e à transformação. A aplicação é fora de combate, em run ativa, para instâncias visíveis e legalmente elegíveis.
- O jogador arrasta sobre o corpo da carta, não precisa acertar um pequeno botão de slot; o slot elegível e a consequência são destacados. Se o destino estiver cheio ou incompatível, a runa retorna à origem e aparece o motivo.
- Para um vínculo irreversível, uma confirmação curta integrada à própria bancada compara antes/depois e informa “Gravar nesta carta · vínculo permanente”. Não abrir uma segunda tela de formulário. Descartar/excluir uma carta não deve gerar devolução implicitamente.
- **Reencaixe reversível é uma hipótese adicional de design:** o contrato de itens atual não permite remover, substituir ou devolver itens vinculados usando os comandos de transformação direta. Se o playtest demonstrar que a irreversibilidade impede experimentar, aprovar uma extensão específica e explícita (ver fase 8). Nunca simular remoção apenas no visual.
- Os ícones na carta identificam o papel e a runa específica; não usar o termo “mod” para confundir inscrição global e runa da carta. Inventário de personagem e bancada de cartas compartilham linguagem visual, não regras de domínio.

### 2.3 Resolução em cadeia

Uma carta continua sendo uma ação individual. A cadeia automática resolve internamente; não introduzir fase de escolher três cartas de uma vez, grid de circuitos ou uma pilha interativa estilo Magic neste ciclo.

Separar três níveis:

1. Composição permanente: preparar a definição efetiva antes do combate.
2. Execução local: sequência, filhos por impacto/proc e continuação.
3. Respostas dinâmicas: eventos reais observados por cartas ativas, inscrições, relíquias, status e containers do setting.

As conexões são abertas por tags/capabilities e fatos, nunca pelo nome de duas cartas que formam uma receita fixa. O sistema deve permitir combinações desproporcionalmente fortes. Distinguir força estratégica legítima de recursão ilimitada, identidade duplicada, cobrança indevida e travamento.

## 3. Inventário de capacidades atuais e lacunas

| Necessidade | Autoridade existente | Intervenção proposta |
| --- | --- | --- |
| Substituir componente, número, tags, custo, retorno ou continuação | `CardUpgradePatchDefinition`, `EffectiveCardResolver` | Usar Set/Replace/Remove/Add, patches estreitos e IDs estáveis |
| Cartas modificadas por instância | `CardInstanceState.Upgrades`, `CardTransformationLedger` | Usar o ledger, não uma lista paralela de runas |
| Compatibilidade não manual | `CardCompositionGrammar`, requirements, capabilityIds, seletores | Adotar requisitos e bundles; manter listas específicas só quando necessário |
| Afinidade altera consequência conforme ação | `compositionRules`, bundles parametrizados | Modelar as contribuições originais como âncoras substituíveis |
| Vários impactos de uma magnitude | `EffectSequenceBudget`, Amount com distribution | Capturar origem uma vez e distribuir; verificar stages por carta |
| Time de execução | BeforeSequence, BeforeImpact, AfterImpact, OncePerProc, AfterSequence | Escopos existentes, sem segundo executor |
| Consumo e propagação | Receitas, `effect_continuation`, `EffectContinuationPlanner` | Usar receitas existentes e perfis de overflow pertinentes |
| Eventos entre peças | `GameplayResponseDefinition`, dispatcher, fontes de observadores | Usar componentes response/inscrições e filtros de fatos, bindings, quotas |
| Carta reage enquanto na mão | `ResponseAvailabilityKind.CardZones` | Declarar zona e disponibilidade; cartas ocultas não reagem implicitamente |
| Aquisição/vínculo | `CardModifierItemApplicationPlanner`, inventário, `APPLY_CARD_MODIFIER_ITEM` | Mesma autoridade pra compatibilidade, preview e commit atômico |
| Visualização da carta | `CardBasePresentationProjector`, `CardPresentationFacts`, CardView | Projeção fiel de diferenças e estados contextuais |
| Visualização da causa | IDs de ação/proc/impacto, steps, traces, gameplay events autorizados | Presenter compacto; caso falte proveniência na projeção de gameplay, acrescentar campo mínimo autorizado |
| Desvincular e transferir runa vinculada | Não disponível para itens atualmente | Extensão opcional e testada; não confundir com Remove/Replace de upgrades diretos |

Contratos locais: [transformações permanentes](../../docs/systems/cards/permanent-transformations.md), [gramática de composição](../../docs/systems/cards/composition-grammar.md), [orçamentos](../../docs/systems/effects/sequence-budgets.md), [continuação causal](../../docs/systems/effects/causal-continuation.md), [respostas](../../docs/content/gameplay-responses.md), [itens e apresentação](../../docs/content/card-presentation-and-modifier-items.md).

### Limites a respeitar

- A gramática seleciona componentes estruturais; efeitos gerados não alimentam outra seleção de composição recursivamente. Encadear resultados dinâmicos por efeitos/respostas.
- `component/Replace` preserva endereço, não copia automaticamente magnitude/pipeline/tags anteriores. Não remover um componente referenciado sem testar bindings. Alterar tag da carta não equivale a substituir tags de efeitos internos.
- Os bundles de composição devem conter efeitos compatíveis; componentes response que não atendam ao contrato de bundle de regra devem entrar por patch estrutural autorizado.
- `CardUpgradeDefinition.CardDefinitionIds` vazio não impõe whitelist. Antes de generalizar, declarar requirements e assegurar que todo alvo selecionado aceita os patches.
- Frequências transacionais Occurrence, Impact, Proc e RootAction têm contratos definidos. Activation/Round/Combat/Run duráveis não devem ser presumidos só pela presença de enum. Usar RootAction nos primeiros gatilhos de economia; confirmar janelas duráveis com testes pertinentes.
- As respostas só continuam outra resposta quando a política de propagação autorizar. RecordOnly mantém fatos, mas não dispara descendentes.
- Nenhuma alteração de regra ou cálculo na Godot. Custo/compatibilidade/execução/replay pertencem à engine; apresentação e input ao cliente.

## 4. Fatia de conteúdo a implementar primeiro

Não produzir 40 runas antes de provar seis. Criar uma variante de QA do Piromante, com fixtures de Renegado para comprovar sinergia transversal e MVP para provar ausência de regras específicas na Godot. Escopo inicial: golpe básico, guarda básica, Magma Slash e um aplicador/consumidor de residual existente. Provar duas cartas com o mesmo ID de definição e transformações distintas.

### 4.1 Catálogo inicial experimental

| Item proposto | Slot/papel | Efeito que substitui | Requisito testável |
| --- | --- | --- | --- |
| Brasa | Afinidade | Consequência neutra ou outra afinidade → aplicação de Burn | Efeito raiz compatível; remove contribuição da afinidade antiga, sem duplicar |
| Geada | Afinidade | Consequência neutra ou outra afinidade → aplicação de Geada | Mesmo padrão; usar status/pipeline corretos do setting |
| Fragmentação | Behavior/forma | Impacto único → três parcelas de orçamento compartilhado | Amount com distribution e stages separados; escopo residual explícito |
| Ruptura | Behavior/consequência | Produção residual selecionada → consumo/condensação | Existe status elegível e receita; testar seleção vazia |
| Transbordo | Behavior/continuação | Excedente perdido → transferência causal por abate | Impacto com um alvo, pipeline de continuação compatível; inicialmente 1 salto |
| Ressonância | Behavior/vínculo | Carta sem resposta → carta reage a fato específico em zona ativa | Response component, CardZones=hand, filtro estrito e RootAction=1 |

Os valores e nomes são hipóteses. Não implementar uma runa que adote o significado completo de outra só para aumentar catálogo. Retorno, custo alternativo, Concentração, Lastro e Guarda Viva são próximas candidatas após o aceite da fatia. Reutilizar Eco Duplo se a semântica por distribuição for confirmada; não alterar silenciosamente seu efeito no histórico.

Exemplo controlado de Fragmentação: base 6, três partes de 2 antes de stages de alvo. Com uma afinidade de payload total 3, dividir 1+1+1 é possível se o contrato de distribuição aplicável for definido. Se a afinidade for uma aplicação por uso/proc, aplicar uma vez com potência 3. Não deixar o Repeat multiplicar automaticamente todo residual. Separar chance por impacto do gatilho “por carta”.

### 4.2 Refatoração mínima do conteúdo

- Auditar `data/configs/alisyum/Resources/cards.json`: componentes cost/main/targeting/disposition, efeito de afinidade, aliases, payloads, repeat, tags nos níveis carta/efeito.
- Introduzir âncoras/namespace de consequência substituível apenas no recorte: ex. `main` e `affinity`. Os IDs existentes são contratos de patches, preservar quando possível. Usar bundles em `card-component-bundles`, registrar o arquivo no package manifest. Precedentes: default e volatile-core.
- Migrar as runas necessárias em `card-upgrades.json` e `card-modifier-items.json` com IDs novos para regras materialmente novas; disponibilizar por versão de conteúdo, não sobrescrever itens possuídos de uma run antiga.
- Referenciar status, pipelines, fórmulas e recipes do setting; publicar uma variante revisionada e validar grafo antes de oferecer no menu normal. Manter pacotes e saves anteriores.
- Não duplicar os 122 modelos para representar suas variantes. Instâncias derivam composição de definição + ledger.

### 4.3 Três builds de prova

| Cenário | Montagem | Prova desejada |
| --- | --- | --- |
| Fornalha | Golpe/Brasa/Fragmentação, identidade e consumidor residual | Mais eventos úteis sem aumentar orçamento de dano; consumo gera payoff perceptível |
| Rompimento | Aplicador, golpe/Ruptura e Ash Phoenix | Consumir stacks uma vez, produzir resultado agregado, realizar compra autorizada |
| Geada reativa | Aplicador/Geada, segunda carta na mão/Ressonância, golpe de Block | A carta reativa não é jogada automaticamente; produz Block com quota; jogador decide converter |

Os detalhes de gatilhos existentes devem ser verificados. O exemplo de fornalha não presume que identidade atual reage a Burn, apenas que isso pode ser uma definição candidata. Manter Calor como contador de estado: se for consumido, descrever como consequência da cadeia, não tornar mais uma moeda no centro da HUD.

## 5. Contrato da resolução em cadeia

Fazer um teste vertical antes de criar toda a economia:

```text
Comando legal (carta e alvo) → pagamento autorizado
    → sequência de impactos (origem comum e alvos efetivos)
        → consequências locais
        → fatos canônicos de mudança/status
            → respostas elegíveis por disponibilidade, filtro e quota
                → novos fatos quando propagação permite
    → disposição e encerramento conforme executor
    → commit/recibo autoritativo → apresentação agrupada → novo input
```

É um diagrama conceitual, não alteração da ordem existente do executor. A ordem precisa ser comprovada para cada família. Antes/depois da sequência e pós-fato podem intercalar-se; documentar o timing exato que a engine realmente executa.

- Respostas reativas filtram tópicos concretos publicados: `status.applied`, `status.reapplied`, `status.stacks.consumed`, `effect.condensation.resolved`, `resource.change.resolved`, `actor.defeat.changed`, `card.moved`. Um fato de dano é mudança de recurso aplicada com papel e contexto apropriados. Não inferir pela HP anterior do cliente nem observar genericamente todo ganho/perda.
- Escolher um único tópico de consumo para um payoff quando existem fatos relacionados (ex. consumo e condensação), evitando dupla ativação.
- Scope por impacto quando o evento tem identidade de impacto; por proc quando pertence ao proc; por RootAction quando é uma recompensa única em cadeia com vários eventos. Não usar “por turno” se não houver suporte durável confirmado.
- Definir bindings e alvo de cada resposta, snapshot e revalidação, ação que derrota origem/alvo, excesso a transportar, ausência de próximo alvo e capacidade de mão. Resposta de custo não deve virar dano recebido. Toda falha do executor mantém atomicidade.
- Evitar ciclo A → B → A sem progresso. A primeira cadeia pode permitir respostas descendentes apenas nas conexões pretendidas, com filtros e quotas. Testar ciclo adversarial e cadeia válida longa.

### Apresentação em combate

O jogador deve reconhecer origem e consequência, sem assistir a uma lista de logs técnicos. Mostrar a carta modificada em destaque, os símbolos de runa acendendo na ordem em que contribuíram, contadores e impactos, depois um resumo compacto com causa.

```text
Fragmentação ×3   →   Brasa +3   →   Ruptura −3 Burn   →   1 carta comprada
```

Em combate, os dados reais podem ter outra ordem e números; o componente visual usa somente facts e traces publicados. Não exibir evento que não ocorreu. Causalidade vem de IDs/provenance, nunca de coincidência de ordem ou nome. O histórico técnico fica atrás de inspeção, com autorização. Quando eventos completos forem restritos no modo normal, projetar apenas contribuição efetiva visível e legalmente publicada; não solicitar modo dev nem vazar cartas ocultas.

Reutilizar `presentation_planner.gd`, `semantic_cues.gd`, `gameplay_event_presenter.gd`, `combat_screen.gd`, Playback, perfis de motion e áudio. Não animar todos os eventos como bloqueios separados: agrupar beats cosméticos sem reordenar fatos, usar acentos rápidos e clímax para a cadeia longa. O layout da HUD e as dimensões dos atores e cartas permanecem fixos.

## 6. Redesign de UI: oficina e menus

### 6.1 Direção visual comum

Consolidar um tema de jogo, reaproveitando `data/themes`, recursos abstratos e CardView. Escolher uma única direção “bancada rúnica”: superfícies de marfim/pedra e carvão, um acento quente por ação, acentos elementais restritos a significados, diagonais e linhas estruturais coerentes. Testar contraste antes de fixar paleta. Não encher o espaço de contornos e painéis; reduzir a moldura quando o próprio objeto já tem identidade.

- Hierarquia de quatro níveis: título da tela, objeto primário (carta/peça), ação principal, detalhes secundários. O objeto da decisão domina a área central, não rótulos de sistema.
- Botões com estados normal, hover, pressionado, foco, gasto e indisponível claros; ícones de família com silhueta distinta, nome curto e descrição de efeito. Cor nunca substitui texto/símbolo.
- Hierarquia tipográfica por papéis definidos em Theme; preservar acessibilidade e localização pt-BR/en, números e diacríticos. Usar labels secundários compactos, detalhes expansíveis.
- Em vez de instruções como “selecione um item e um alvo”, mostrar peças manipuláveis e dicas contextuais. Remover JSON, versão, status da API e nomes técnicos da superfície normal; estados de erro/conexão permanecem acessíveis e explicam recuperação.
- Animações discretas de entrada, botão, destaque e equipar. Preservar o hover das cartas e efeitos sonoros novos. Animações não alteram minimum size de containers, nem fazem input esperar sem motivo. Preferir offset do filho visual sobre o tamanho do layout.
- Construir cenas .tscn editáveis, reutilizar StyleBox/Theme e componentes compartilhados. Não montar UI inteira com scripts que criam nós dinamicamente quando editor pode defini-los.

### 6.2 Bancada de runas — tela principal de construção

Layout de referência em 1440×900, adaptável:

```text
┌─────────────────────────────────────────────────────────────────────────┐
│ ← Jornada / Baralho     FORJA DE OBOR        Ouro 53  FR 3    Continuar  │
├─────────────────────────────────────────────────────────────────────────┤
│ RUNAS DISPONÍVEIS          COLEÇÃO / BARALHO        CARTA EM FOCO         │
│ ┌─────┬─────┬─────┐       ┌─────┐┌─────┐┌─────┐    ┌───────────────┐   │
│ │  ◇  │  ⋔  │  ✶  │       │carta││carta││carta│    │     GOLPE     │   │
│ │Brasa│Frag.│Rupt.│ ─drag→│ ◇ ○ ││ ○ ○ ││ ○ ○ │    │    6 dano     │   │
│ └─────┴─────┴─────┘       └─────┘└─────┘└─────┘    │ [◇] [⋔] [○]  │   │
│ símbolos e quantidades    ┌─────┐┌─────┐┌─────┐    └───────────────┘   │
│                            │carta││carta││carta│    Mudança: 1×6→3×2   │
│ detalhe do item em foco    └─────┘└─────┘└─────┘    Interage com: Burn  │
│                                                      [Gravar na carta]  │
└─────────────────────────────────────────────────────────────────────────┘
```

Essa é uma composição de interface, não mockup final. Propor área esquerda ~20–25%, coleção ~45–50%, detalhe/prévia ~25–30%; usar área central para ampliar a quantidade de cartas. Em 1280×720 e escalas ampliadas, transformar detalhe em gaveta/sobreposição acessível, sem reduzir texto a ponto ilegível; preservar alvos de drag e área principal.

- Runa é uma ficha com símbolo geométrico, nome curto, estado Available/Bound/Consumed conforme o modelo. Unidades iguais podem agrupar visualmente por quantidade, mas o arraste sempre carrega um itemInstanceId elegível. Item agrupado não permite aplicação múltipla implícita.
- Carta é o CardView já utilizado no combate. Mostrar até três encaixes no rodapé da instância, com tamanhos fixos; identificar o tipo de slot e a transformação real. Encaixes ocupados não prometem interação de remoção quando indisponível.
- Ao começar arraste, as cartas permanecem no lugar. Elegíveis recebem halo e símbolo de encaixe; incompatíveis ficam levemente atenuadas (não invisíveis) e podem mostrar o motivo. Ao pairar sobre alvo, exibir diferença autorizada no painel direito. Não criar nova grade filtrada que desloca alvos durante o drag.
- Ao soltar sobre a carta, focar o destino e preparar confirmação inline, exclusivamente se existe preview vigente compatível. A própria carta mostra acoplamento em estado pendente, distinto de aplicação concluída.
- Ao confirmar e receber commit, símbolo encaixa, pequeno pulso, som apropriado e texto de confirmação. O inventário e carta se atualizam pela mesma revisão. Se o servidor rejeita, não persistir aparência fictícia; animação de retorno e motivo.
- Mesma bancada acessível em forja, inventário de modificadores e Baralho. Comprar/adquirir continua separado de aplicar: quando aquisição terminar, a runa aparece no inventário e pode ser arrastada imediatamente no mesmo fluxo.
- Teclado/controle: selecionar runa, navegar por cartas válidas, confirmar no mesmo estado de preview, Esc/B cancela, foco retorna à origem. Isso é caminho equivalente ao arraste, não um segundo sistema.

### 6.3 Contrato Godot do gesto

Reaproveitar o precedente `scripts/ui/loadout/inscription_tile.gd` (payload versionado e drag preview nativo) e `deck_module_view.gd`. A inscrição e a runa não compartilham cálculo de legalidade.

Referências oficiais: [Control e drag/drop](https://docs.godotengine.org/en/stable/classes/class_control.html), [navegação de GUI](https://docs.godotengine.org/en/stable/tutorials/ui/gui_navigation.html), [containers](https://docs.godotengine.org/en/stable/tutorials/ui/gui_containers.html), [temas](https://docs.godotengine.org/en/stable/tutorials/ui/gui_skinning.html).

Propor componentes novos/ajustados:

| Candidato | Responsabilidade |
| --- | --- |
| `rune_tile.tscn/.gd` | Ficha, seleção, início de drag, preview sob cursor |
| `rune_card_target.tscn/.gd` | Drop target sobre toda a carta; slots e feedback, sem calcular elegibilidade |
| `rune_workbench.tscn/.gd` | Composição comum da bancada; grid, detalhe e navegação |
| `rune_workbench_controller.gd` ou extensão do controlador existente | Orquestra versão, preview, intenção, confirmação, submissão, reconciliação |
| `rune_presentation.gd` | View models de inventário, estado, nomes, deltas e encaixes derivados da projeção |

Antes de criar scripts, verificar se a responsabilidade cabe em `card_modifier_inventory_view.gd`, `pattern_workshop_view.gd`, `unified_loadout_controller.gd` ou seus presenters; não duplicar caminhos. Os nomes são candidatos de arquitetura, não arquivos já implementados.

Payload local do drag contém apenas kind, escopo da view, runId, revisão, sequence/step e itemInstanceId observado. O alvo fornece cardInstanceId. Não carregar preço, efeito computado ou patch arbitrário no payload enviado.

```text
Idle → selecionada/dragging → target-hover (compatibilidade da snapshot)
  → preview-pending → preview-ready → drop-staged → confirming
  → submit-pending → confirmed/reconciled
             └→ erro/cancelamento/contexto obsoleto → snapshot atual
```

- `_get_drag_data` e `set_drag_preview`: somente forma visual. `_can_drop_data` é pura e rápida, consulta mapa de legalidade publicado; não dispara requisição por frame, não interpreta regras. `_drop_data` cria intenção de preview/confirmar, não modifica carta diretamente.
- Coletar a opção exata `APPLY_CARD_MODIFIER_ITEM` disponível pelo par item+instância. Uma opção ausente ou duplicada bloqueia. Consultar `GET /api/v1/runs/{runId}/card-modifier-items/{itemInstanceId}/preview/{cardInstanceId}` via serviço existente. Aproveitar cache derivado por contexto, debounce em hover de ~80–120 ms, descartar respostas tardias de outro alvo. Se drop ocorrer antes da resposta, manter seleção e só habilitar confirmação quando a resposta chegar.
- Validar contexto completo: run, revisão, sequence, step, identidade da view e par alvo/item. `SessionInspectionService.preview_transformation` hoje verifica run + sequence; revisar metadados efetivamente retornados e guardas locais restantes antes de aceitar. Não inventar campos de API.
- Revalidar legalidade e opção ao confirmar. Usar submissão canônica com commandId/expectedSequence/expectedStep por `GameSession`; a recuperação de comando pendente reutiliza a identidade do comando, não reenvia como novo.
- Se mudança de rota/versão, desconexão, fechamento, Alt-Tab/cancelamento, revogação de item, overflow de grade ou operação incerta, limpar preview/arraste e conservar snapshot consistente. Bloquear operações concorrentes e permitir recuperação. Cancelar sem API de mutação, sem gasto e sem alterações no RNG.
- Não reutilizar drag de jogar carta durante combate. O inventário fica sem drop quando leitura/edição não está autorizada. Configurar mouse_filter dos filhos decorativos para não interceptar cards; scroll/foco não quebra drop; testar render em resoluções e DPI diferentes.

### 6.4 Outros menus do fluxo jogável

A bancada é a prioridade, mas uma boa tela isolada não resolve menus que parecem ferramentas. Acertar a linguagem comum antes de migrar as demais.

| Tela | Decisão de design | Evidência de aceite |
| --- | --- | --- |
| Menu principal / seleção de personagem | Jornada como ação dominante, selecionar herói por identidade/fantasia e mods, Continue em destaque quando disponível; laboratório/configuração técnica fora da superfície principal | Novo jogo/Continue claros em poucos cliques; sem status técnico permanente |
| Mapa | Rota/risco/recompensa visíveis; substituir listas de ações por nós e detalhes contextuais; acesso discreto à build | Entender próxima escolha e voltar sem perder contexto |
| Recompensas | 3 cartas/runa oferecidas em objetos grandes; relevância para a build como segunda camada; escolher/decompor/ignorar só se legal | Comparar opções sem abrir vários diálogos; feedback de aquisição |
| Bazar/forja | Bancada e ofertas visuais, carta/runas compráveis, custo junto ao objeto, inventário disponível na mesma tela | Comprar runa e aplicá-la sem sair para outro menu |
| Preparação / loadout | Hero sheet abstrata, identidade, slots visuais, capacidades; Baralho integrado à bancada; abas claras, atributos avançados em detalhe | Inscrições e runas com semânticas separadas, drag equivalente |
| Evento / diálogo | Foco em situação e alternativas com consequência conhecida; retirar layout genérico de opções administrativas | Decidir sem precisar navegar por formulário |
| Pausa / configurações / resultado | Ação principal de retomar/continuar; configurações por categorias, voltar previsível; resumo de build e causado/recebido/destaques quando publicados | Navegação sem perder operação pendente ou resetar partida |

Arquivos de partida: `scenes/ui/screens/main_menu.tscn`, `map_screen.tscn`, `activity_screen.tscn`, `unified_loadout.tscn`, `scenes/ui/components/activity_*`, `pattern_workshop*`, `card_modifier_*`, `loadout/*`, `scenes/ui/overlays/pause_menu.tscn`. Revisar presenters e controladores vinculados antes de editar. Não renomear nós consumidos por scripts sem alterar os contratos e testes. Não inventar UI para funcionalidades não implementadas.

Capturar baseline de cada tela nos dois settings com Godot MCP/execução real, depois screenshots equivalentes das novas. Avaliação visual humana obrigatória do estilo; verificar objetivamente contraste, clipping, foco, hierarquia, integridade e igualdade do estado. Não generalizar para cenas auxiliares. Reutilizar variantes de tema sem refazer HUD de combate já corrigida.

## 7. Jornada, recompensas e economia

No Alisyum atual a primeira forja/loja explícita de runa fica após dois combates. Mover o acesso ao diferencial para antes do primeiro combate. O engine suporta `CardUpgrade` com modifierItemIds; a aquisição consome sua escolha/custo e não aplica item automaticamente. Usar atividade inicial dedicada, sem criar um command novo de recompensa mista por conveniência.

Prova de campanha curta, isolada:

1. Escolher identidade, escolher uma de três runas iniciais através de oferta autorizada e arrastá-la sobre carta básica.
2. Combate curto que evidencia a mudança; desbloquear uma nova peça após a vitória.
3. Combate de dois alvos em que o jogador precisa escolher entre payoff imediato e preparar cadeia.
4. Loja/forja ou escolha de inscrição; adquirir runa complementar; permitir guardar unidades.
5. Chefe com oportunidade de executar uma cadeia poderosa e uma escolha final de construção.

Usar nós reais de aquisição (CardUpgrade ou Shop) e pools revisionados. Testar ofertas úteis com o inventário inicial, sem depender de sorte perfeita ou forçar uma receita única. Se necessário oferecer aquisição de runa e escolha de carta em nós adjacentes visualmente coesos, sem apresentar dois comandos como uma operação atômica que não existe. A aquisição separada da aplicação deve ser recuperável e não duplicar custos na navegação/reconnect.

Medir tempo até primeira transformação (meta experimental até 2 min), disponibilidade legal de pelo menos três momentos de construir na fatia e pelo menos duas rotas plausíveis de sinergia. Progressão não é um tutorial obrigatório longo: sensação de descoberta e decisão sobre qual carta mudar vem do próprio gesto.

Para MVP: não copiar recursos, slots de identidade, hand lifecycle, módulos de custo ou regras de Alisyum. Mapear seus status/pipelines e disponibilizar ao menos uma variante de afinidade/forma/vínculo autoritativamente compatível. A bancada comum só enxerga as opções anunciadas.

## 8. Etapas de implementação e commits

Cada etapa produz um commit específico após gates técnicos. Os commits são recomendação para execução futura; este plano não os cria. Isolar alterações anteriores do checkout (gambits, run, HUD e arquivo temporário da planilha). Se houver sobreposição, revisar antes de editar; não misturar linhas não relacionadas.

| Etapa | Entregável | Gate / sugestão de commit |
| --- | --- | --- |
| 0. Baseline | Capturas, runs QA, matriz de conteúdo, contrato de item, eventos disponíveis, baseline de latência | Relatório reproduzível; somente diagnóstico. `docs(design): baseline rune composition and menu flows` |
| 1. Fatia estrutural | Variante Alisyum, standard de âncoras, 2 afinidades e Fragmentação; legalidade e antes/depois | Transformação real por instância, orçamento preservado, base não duplicada, save antigo intocado. `feat(content): add scoped overwrite runes` |
| 2. Cadeia vertical | Ruptura, Transbordo, Ressonância e duas peças reativas; 3 builds fixtures | Resolução e replay canônicos, quota/propagação testadas, matriz de compatibilidade. `feat(content): compose causal rune chains` |
| 3. Contrato de projeção | Expor estado de encaixe e diferenças reais; causa visível no recibo/preview se faltar | Não ampliar informação oculta; API atual funciona onde suficiente; testes de contrato. `feat(presentation): expose rune composition facts` |
| 4. Sistema visual | Theme, componentes rúnicos, cards de oferta, foco, transições e padrões de navegação | Comparação visual em resoluções e idiomas; uma identidade comum. `feat(ui): introduce rune workbench visual language` |
| 5. Bancada drag/drop | Fichas, CardView targets, seleção/preview/confirmar, estados async, navegação teclado | Drag real até corpo da carta, drop válido e inválido, uma aplicação, zero gasto cancelando. `feat(ui): drag rune items directly onto cards` |
| 6. Integração no ciclo | Forja, Loadout, prêmio inicial, loja e fluxo curto; ofertas de build | Jogar fatia inteira e modificar cedo; compra/uso/reconnect consistentes. `feat(gameplay): integrate early rune discovery` |
| 7. Resolução expressiva | Ícones, timeline causal compacta, feedback de encaixe/procs, agrupamento e áudio | Cadeia legível, mãos/inimigos estáveis, controles liberados. `feat(ui): present causal rune chains` |
| 8. Expansão de usabilidade | Redesign dos demais menus normais, ajustar economia/flow, MVP e demais heróis após gate de Piromante | Matriz visual e smoke completo; feedback humano. Separar commits por grupo de telas e conteúdo |
| 9. Certificação | Matriz completa, performance, simulações, playtests, documentação | Todos os gates técnicos, decisão humana registrada. `test(gameplay): certify rune chains and menus` |

Se um contrato genérico realmente faltar, fazer uma etapa técnica separada com testes antes do commit de conteúdo que o utiliza. Não adicionar código engine apenas porque JSONs atuais não usam recursos disponíveis.

Extensão opcional de reencaixe: depois da fase 5 e do primeiro playtest, o autor decide se runas são gravuras consumíveis/vínculos permanentes ou componentes móveis. Se aprovar componentes móveis, acrescentar capacidade/política explícita de unequip/swap restrita a contextos de preparo/forja, reaproveitando ledger e planner de item. Uma operação atômica deve retirar a transformação ativa, validar a composição restante, devolver a MESMA unidade à disponibilidade (limpar vínculo), registrar proveniência, custo se configurado e validar os dois lados de transferência. Swap direto precisa validar ambos os destinos e nunca destruir item em falha; preview e legalidade pelo mesmo planejador, sem reaproveitar REMOVE_CARD_TRANSFORMATION para burlar as invariantes. Testar durabilidade, revisão de aquisição, dependências de mods, versões antigas, retry e replay. Só então habilitar arrastar rune equipada para inventário/outra carta. Não entregar botão falso.

## 9. Plano detalhado de testes

### 9.1 Engine, conteúdo e API

Reutilizar e ampliar `CardTransformationTests`, `CardTransformationCompositionTests`, `CardCompositionGrammarTests`, `CardCompositionPublicationTests`, `CardModifierItemInventoryTests`, `CardModifierItemDurabilityTests`, `CondensationTests`, `EffectContinuationTests`, `GameplayResponseDispatchTests`, `GameplayResponseContinuationTests`, `GameplayResponseMatcherTests`, `PriorityResponseLedgerTests` e integrações REST relevantes.

| Caso | Expectativa verificável |
| --- | --- |
| Duas instâncias iguais, uma modificada | A outra mantém composição/hash efetivo anterior |
| Afinidade troca fogo→gelo | Sem Burn antigo e Geada nova simultânea por acidente; tags/pipeline coerentes |
| Forma 1×6→3×2 | Origem capturada e repartida; arredondamento, defesa e críticos independentes testados |
| Ordem A→B e B→A | Mesma composição se independentes; diferença documentada ou incompatibilidade se conflitantes |
| Afinidade + Fragmentação + Ruptura | Stacks/potência e timing coerentes; condensação consome uma vez |
| Transbordo | Somente abate causal; carry, alvo vivo, sem scaling duplicado; sem alvo termina |
| Resposta na mão | Visível/ativa apenas na zona declarada; mão cheia, saída da zona, dono derrotado |
| Cadeia com 3–5 peças | IDs e facts causais, quotas e contribuição exata; mesma seed/revisão preserva replay |
| Ciclo adversarial e exceção a meio | Quotas/orçamento seguros, sem escrita parcial; erro estruturado |
| Inventário | Preview não altera run/RNG; aplicar item uma vez, repetir idempotente, concorrência rejeitada sem perder unidade |
| Revisões | Bundle fechado, parâmetros e regra preservados; nenhum hot reload muda run existente |
| Recompensa e navegação | Adquirir e aplicar separadamente, custos exatos, sem duplicação ao retomar |

Testar composição declarada versus aplicação efetiva através do planner, não apenas contar listas de IDs. Matriz mínima: cards × runas, pares legíveis, substituições em ordem inversa e instâncias duplicadas. Salvar CSV/JSON de evidências apenas como artefato de QA sem dados de partidas privadas.

### 9.2 Godot e contrato de interação

Base: `card_modifier_items.gd`, `pattern_workshop_screen.gd`, `pattern_workshop_inventory.gd`, `map_pattern_workshop.gd`, `unified_loadout_autosave.gd`, `unified_loadout_layout.gd`, `loadout_input.gd`, `reward_choice.gd`, `activity_reconciliation.gd`, `presentation_planner.gd`, `semantic_cues.gd`, testes de motion e composição/HUD.

Adicionar testes específicos do fluxo de runas, por exemplo `rune_workbench_drag.gd`, `rune_workbench_preview_context.gd`, `rune_workbench_online.gd` e `rune_chain_presentation.gd` (nomes propostos).

- Drag inicia em runa de inventário, preview sob cursor, compatible target real. A posição do grid não muda. Drop no corpo da carta seleciona identidade exata mesmo com cópias.
- Cancelar por Esc/drop fora, trocar aba, mudar resolução, soltar em alvo incompatível ou não anunciado: zero comandos. Mapa/forja mantém retorno coerente.
- Duas prévias assíncronas A e B; resposta de A depois de B jamais muda B. Resposta com versão antiga, erro, timeout, mudança de run não permite confirmar.
- Drop seguido de confirmação válida envia exatamente um comando; double-click não reaplica. Rejeição não mostra equip concluído. Reconexão após resposta incerta reconcilia item/carta pela snapshot. Input de teclado produz o mesmo handler.
- Slots, nome da runa e números realmente exibidos vêm da composição da engine. A comparação before/after não calcula valores de combate futuro; usa Base/ComposedBase/Source/Impact conforme contexto.
- Cadeia curta/longa, vários status, cartas modificadas, resize e janela incorporada: dimensões fixas das cartas e unidades; sem perder clique, foco, hotkeys, retomar ou skip. Áudio novo toca sem sobreposição descontrolada; hover preservado.

### 9.3 Evidência visual e usabilidade

Executar 1280×720, 1440×900, 1920×1080, 2560×1080 e escala de texto ampliada; localização PT-BR e EN; ponteiro e teclado/controle. Capturas na primeira abertura, menu de herói, rota, recompensas/loja, bancada idle/drag/preview/incompatível/sucesso, combate com cadeia, pausa e Continue. Validar screenshot em renderer real com Godot MCP ou ferramenta de captura de desktop se necessário, não somente teste headless. Preservar saves e usar sandbox.

Regras objetivas: sem texto truncado essencial; contraste adequado a textos e sinais; mínimo de cliques (adquirir runa → arrastar carta → confirmar, sem abrir telas extras); elementos inferiores não cortados; nenhum frame que mude o tamanho de unidade por status; todas as opções legais acessíveis com teclado e mouse. Capturar antes/depois com mesmo contexto e resolução.

## 10. Validação com MCPs e IA

Utilizar infraestrutura existente em `tools/HeroScript.Mcp/README.md`, API sandbox 5280 via `tools/Start-HeroScriptMcpSandbox.ps1`, e Godot MCP isolado conforme `GODOT_MCP_SETUP.md`; não reutilizar partida do autor para testes que escrevem.

1. Validar versão e revisão candidata; `validate_content`, `validate_gambits`, consultas de inspeção. Verificar que os novos JSONs são realmente carregados pelo package.
2. `simulate_encounter`, `analyze_cards` e `simulate_branch` para base, A, B, A+B; usar seeds comparáveis e comandos legais das fixtures. MCP atual usa heurística, não estratégia ótima. Quando não representar composição completa de run, usar teste do planner/API isolado, não inventar ferramenta ou elevar permissões.
3. Capturar resultado e traces: dano líquido/bruto quando publicado, Block, stacks, compra, energia, escopo, ação/proc/impact, origem e ordem. Verificar `verify_replay` e hash de fonte inalterado.
4. Conectar Godot MCP a clone/cópia QA. Conferir estado de apresentação com API, screenshot e diagnóstico; fazer ao menos um gesto real de arraste e confirmar a escrita na run de QA. Headless para regressões, renderer real para UI.
5. Rodar testes `dotnet test tests/Core.Tests` com filtros relevantes, `dotnet test tests/API.Tests` e `tools/test-godot-ui.ps1 -Tests ...`. Ler instruções atuais do runner/fixtures e APIs isoladas antes de executar. Separar resultados de regressões anteriores do checkout.

IA prepara hipóteses, matriz, testes, comparações, validação de entradas e screenshots. A diversão e o feeling precisam de avaliação humana: perguntar qual carta parecia interessante de modificar, o que foi perdido, por que a cadeia disparou e qual build quer tentar em seguida. Evitar aumentar catálogo para mascarar feedback negativo.

## 11. Critérios de aceite e próximos limites

### Gate de produto obrigatório

- Escolher a primeira runa na abertura, arrastar fisicamente sobre uma carta e reconhecer a mudança sem abrir menu técnico.
- Pelo menos duas runas úteis combináveis na mesma instância; dois exemplares da mesma carta podem assumir funções diferentes.
- Executar cadeia real com três peças diferentes e payoff visível. O jogador consegue explicar o porquê.
- Uma nova peça cria mais de uma opção de build. Pelo menos duas estratégias plausíveis no recorte; nenhuma exige pegar uma sequência de recompensas específica sem alternativas.
- Menu principal, inventário, preparação, mapa, recompensa, loja, forja, pausa e resultado têm identidade visual consistente; não se comportam como formulários administrativos.
- Pipeline de ação, apresentação, save/Continue e replay continuam confiáveis; sem regressão de tamanho dos inimigos, seleção e movimento de cartas, hover ou pagamento.

### Métricas iniciais (não são provas de diversão)

Tempo até primeira runa aplicada ≤ 2 min; no mínimo três oportunidades de construir na fatia; registrar tempo de drag/preview/commit, feedback de input p95 < 100 ms no hardware de referência, número de telas para adquirir/aplicar, tempo bloqueado por cadeia, quantidade de procs distintos, diferença de base vs combinações, taxa de pick/uso e falhas. Comparar com baseline e verificar resultados reais; não declarar vitória de design somente por teste automático.

### Fora do primeiro ciclo

Editor de circuitos, resolução de múltiplas cartas enfileiradas manualmente, novas classes, arte de personagens, outra engine de cálculo, reescrita de saves, menus de settings auxiliares, multiplayer e publicação de grandes catálogos de runas antes de validar o protótipo.

**Ordem recomendada:** provar o core no conteúdo, fazer uma bancada de jogo funcional e agradável desde a primeira runa, integrar a descoberta à jornada, mostrar a cadeia de forma expressiva e só ampliar após feedback humano. A meta é um sistema que produza builds surpreendentes, não mais uma coleção de botões de melhoria.
