# Cartas completas, cálculo explicável e modificadores como itens

Data: 2026-10-14. Estado: plano para implementação; nenhuma mudança de gameplay aplicada neste pedido.

## Acompanhamento da implementação

- Base anterior registrada separadamente em `3e18838` (animações e atributos globais).
- Etapa 1: contratos imutáveis de identidade, camadas numéricas, comparações e fixtures mecânicas implementados; não alteram a execução de efeitos.
- Etapa 2: projector puro de base composta implementado, sem ator/combate/candidato legal; fixtures confirmam equivalência entre oferta e instância, custo alternativo, fórmulas não resolvidas e composição própria. Consultas de run/ofertas serão conectadas na etapa 4.
- Etapa 3: captura de prefixo Actor pelo resolver/cálculo comum, atributos estruturados (inclusive zero), fontes pertinentes, circunstâncias e explicação de impactos separada implementados. Testes de strike/multihit com energia zero provam igualdade com orçamento da execução e imutabilidade. A origem é identificada como snapshot anterior à ação, não uma promessa sobre efeitos sequenciais/condicionais futuros.
- Etapa 4: consulta em lote `GET /api/v1/runs/{runId}/cards/presentations` implementada para instâncias visíveis e ofertas atuais; cache derivado limitado, compilação compartilhada por definição no lote, snapshot revisionado e publicação atômica na sessão Godot. A inspeção de combate inclui origem mesmo sem candidato executável e mantém impactos separados. Diagnósticos de fórmulas/traces internos permanecem restritos ao detalhe Full. Testes de equivalência, leitura imutável, cache, revisão e sigilo de pilhas passaram.
- Etapa 5: adapter passivo compartilhado de fatos aplicado às ofertas atuais, mão e coleções de combate, incluindo os modelos finais das animações. Custos de aquisição separados dos custos de uso; números usam a camada explícita e não impactos. Efeitos filhos são apresentados uma vez por path. Fixtures antigas sem o novo contrato mantêm fallback; ofertas reais atuais usam a consulta revisionada.
- Etapa 6: tooltip de cartas com fatos publicados mostra camadas, atributos elegíveis (inclusive zero), fontes pertinentes e condição; não repete os efeitos/custos da face e continua passivo, sem scroll inacessível. Inspector inclui outputs publicados dos buckets, sem calcular na Godot. Impacto continua separado; dados técnicos de fórmulas não são necessários para essa explicação.
- Etapa 7: kind tipado `card-modifier-items`, validação da transformação/política explícita, inventário imutável com identidade e proveniência, roundtrip/hash e consulta `GET /api/v1/runs/{runId}/card-modifier-items` implementados. Compatibilidade usa transição de ledger + compositor existentes, respeita visibilidade, desbloqueios e bloqueio durante combate. Inventário ausente não altera hashes antigos. Aquisição/aplicação ainda aguardam a etapa 8; não há mutação externa por essa consulta.
- Etapa 8: planner único de aplicação/compatibilidade/preview e comando canônico `APPLY_CARD_MODIFIER_ITEM` implementados. Preview GET retorna antes/depois em base composta e versão esperada. Comando atualiza carta e unidade consumida/vinculada no mesmo candidato; descoberta anuncia pares executáveis. Remoção/substituição pelo caminho de upgrades não pode contornar a posse do item. Testes de composição, preview imutável, retry idempotente, dupla utilização, contexto inválido e roundtrip passaram. Engine versionada em `30`; aquisição/conteúdo e UI seguem na etapa 9. Certificação específica de replay semântico/branches e falha de persistência fica no gate final da etapa 10.
- Etapas 9–10: pendentes.

## 1. Objetivo e interpretação

Uma carta oferecida deve ser a mesma carta que será recebida: mesmo componente visual, arte, custo de uso, condições, efeitos e regras. Não deve ser uma descrição de flavor no lugar de sua mecânica. Na escolha, mostrar o valor-base da carta, sem atributos/buffs do herói, defesa de um alvo ou sorteio de crítico. Uma comparação contextual pode ser aberta separadamente.

Se uma oferta já incluir upgrade/modificador próprio, seu valor-base é o da composição ofertada, não o da definição original sem essa alteração. É preciso informar isso e entregar exatamente a composição mostrada.

O tooltip deve complementar a carta com o cálculo e suas causas, não reimprimir suas seções. Modificadores de carta devem existir como itens independentes, adquiridos e depois aplicados a uma instância de carta. A modificação continua usando os componentes, a composição e a bucket pipeline existentes.

## 2. Diagnóstico confirmado no código

| Área | Estado atual | Consequência |
| --- | --- | --- |
| Ofertas | `ActivityPresenter.offer()` usa o catálogo de apresentação para texto. `ActivityOptionsView` usa `CardView`, mas define `costsKnown=false` porque não tem inspeção do container ofertado. | O componente é compartilhado, porém os fatos não são: custo e efeitos reais não chegam completos. |
| Combate | `CombatPresenter.card_view_model()` usa avaliação/inspeção quando disponível e texto de apresentação como fallback. | Uma mesma carta pode ter descrição precisa ou genérica dependendo da presença da avaliação. |
| Inspeção | `CardInspectionService` obtém passos/cálculos de `legal.Value.Candidate?.CardPlay`. | Sem preview executável, faltam traces de origem, mesmo quando a carta deveria poder ser lida. Leitura e legalidade estão parcialmente acopladas. |
| Números da face | `CardSectionPresenter` procura prefixos Actor nos traces do preview. | A UI ainda deriva o número de origem de um trace de execução, em vez de consumir um fato de apresentação explícito. |
| Tooltip | `CardDetailsPanel` repete nome, tipo, raridade, custo, requisitos, efeitos, comportamentos e badges. | Há redundância com a face e pouco espaço para explicar o cálculo. |
| Inspector | `CombatPresenter.inspection_text()` concatena resumo, previews, componentes, procs e listas gerais de relíquias/modifiers/statuses. | Uma fonte pode aparecer sem ter contribuído para o valor daquela carta; explicação se mistura com diagnóstico técnico. |
| Transformações | Há `CardUpgradeDefinition`, patches, bundles, predicados, categorias Base/Affinity/Behavior, slots, Apply/Remove/Replace e ledger imutável. | Existe base reutilizável para aplicar modificadores, mas isso não equivale a um item possuído no inventário. |
| Posse de itens | Não há catálogo tipado de itens modificadores de carta no registry nem inventário canônico desses itens em `RunState`. | É necessário implementar aquisição, identidade, disponibilidade e consumo/transferência, não só renomear upgrades. |

Arquivos principais: `scripts/ui/components/activity_options_view.gd`, `scripts/presentation/activity_presenter.gd`, `combat_presenter.gd`, `card_section_presenter.gd`, `scripts/ui/card_details_panel.gd`; na engine, `CardInspectionService`, `CardTransformationPlanner`, `CardTransformationComposition` e `CardInstanceState`.

## 3. Decisões de arquitetura

### 3.1 Quatro camadas numéricas explicitamente separadas

| Camada | Significado | Uso |
| --- | --- | --- |
| Original | Definição autorada e revisionada. | Referência da identidade e das alterações permanentes. |
| Base composta | Original + upgrades/modificadores próprios da instância/oferta. | Face das ofertas, catálogo e comparação de crafting sem buffs do herói. |
| Valor de origem | Base composta passando pelas etapas de origem da pipeline, com atributos e circunstâncias pertinentes. | Face em combate e cálculo de origem. |
| Resultado de impacto | Inclui alvo, crítico, resistências/capacidades, quantização, limites e consequências. | Preview separado, log e animações. |

Uma única projeção descreve essas camadas. A Godot escolhe qual camada mostrar; não calcula números. A definição e os snapshots continuam imutáveis.

As cores compartilhadas continuam branco/sem alteração, vermelho/menor e azul/maior. A projeção deve declarar qual referência está sendo comparada: original versus base composta para alterações permanentes; base composta versus origem para scaling contextual. Não inferir isso por presença de badges ou pelo texto.

Fórmulas que dependem de um contexto ainda inexistente não recebem um número inventado. A engine deve retornar expressão/descritor legível, dependências e motivo da ausência do número. Por exemplo, “reduz uma porcentagem do recurso atual do alvo”, em vez de uma descrição genérica ou zero. Valor condicional/probabilístico nunca é apresentado como garantido.

### 3.2 Uma projeção de carta, vários contextos de leitura

Criar um serviço comum de fatos de apresentação/inspeção, reutilizando o compiler, `EffectiveCardResolver`, resolver de regras e cálculos existentes. Deve aceitar:

- definição revisionada sem run;
- oferta concreta de recompensa/loja, dentro da run;
- instância possuída fora do combate;
- instância e ator no combate;
- comparação antes/depois de aplicação de item.

Sem criar uma instância falsa no deck, iniciar um combate fictício ou usar um candidato legal como requisito de leitura. Legalidade continua sendo um fato separado. Uma carta indisponível por energia insuficiente ainda deve revelar custo, efeito e scaling.

O mesmo `CardView` e um presenter passivo comum recebem esses fatos em recompensa, loja, deck, mão, pilhas e laboratório. O catálogo visual só fornece arte, layout, nomes/flavor e tradução; não duplica custo, números ou regras. Preço de aquisição fica fora da face, como hoje.

### 3.3 Explicação do cálculo na engine

Adicionar projeção explicativa por `effectId`/`componentId` e parâmetro — Amount, stacks, duração, custo etc. Consumir os traces e bindings reais, sem montar um segundo calculador.

Cada explicação precisa distinguir:

1. Valor original e alterações próprias da carta.
2. Atributos com os quais aquele efeito pode escalar no contexto configurado, inclusive atributos elegíveis com contribuição atual zero.
3. Fontes que realmente contribuíram: atributo, item aplicado, upgrade, status, relíquia, modifier contextual, modo ou encontro.
4. Circunstâncias: tags correspondentes, condição avaliada, duração/limite relevante e contexto de origem ou alvo.
5. Operação, unidade, agrupamento do bucket, entrada/saída e arredondamento.
6. Dependências ainda não resolvidas e incerteza, quando houver.

O contrato deve identificar atributos/fontes por campos estruturados. A UI não deve extrair um atributo separando strings como `sourceId` ou adivinhar a semântica de um bucket chamado `increased`.

Contribuições do mesmo bucket aditivo aparecem agrupadas. Exemplo ilustrativo: base composta 10; +50% de atributo e +10% de status no mesmo bucket resultam em 16, não em 16,5. Um multiplicador posterior de ×1,2 gera 19,2 antes do arredondamento declarado. Os outputs vêm da engine; a expressão visual só explica esses outputs.

Condição que não se aplica não entra na soma. Atributos elegíveis e condições inativas podem ser consultados no inspector expandido, com motivo explícito. Não listar todos os statuses/reliquias do personagem como se todos afetassem aquela carta.

### 3.4 Tooltip e inspector têm funções distintas

Tooltip passivo e curto:

- contexto da leitura: base da oferta ou origem do ator;
- atributo(s) elegível(is) e contribuição atual;
- sequência compacta do cálculo, agrupada por efeito;
- apenas circunstâncias relevantes;
- indicação de crítico/alvo pendente, quando pertinente.

Não repetir arte, raridade, custo inalterado, descrição inteira, disposição e todos os badges. Custo aparece na explicação apenas se alterado ou contextual. Uma pequena identificação do efeito é permitida para diferenciar vários efeitos da mesma carta.

Inspector persistente:

- explicação completa e expansível;
- abas/seções “Cálculo”, “Modificadores aplicados” e “Condições”;
- preview de alvo em seção separada;
- diagnóstico técnico somente nas ferramentas dev/modder, conforme capabilities.

Tooltip continua sem scrollbar/foco impossível de alcançar. Conteúdo extenso vai para inspeção persistente, não é comprimido em texto vertical. Ambos consomem a mesma projeção, em níveis de detalhe diferentes.

### 3.5 Modificadores de carta são itens, não buffs do herói

Separar explicitamente:

- upgrade: evolução própria/permanente da carta;
- item modificador: objeto independente, com instância e regras de aplicação, que pode alterar afinidade, comportamento ou propriedades da carta;
- status/modifier contextual: fonte temporária de cálculo, sem posse nem aplicação de item;
- relíquia: sistema já existente, não convertida implicitamente em item de carta.

Adicionar catálogo revisionado proposto `card-modifier-items`. A definição contém ID, nome/arte/raridade, referência à transformação/bundle, compatibilidade, slot/categoria, política de aplicação e de consumo/transferência. Não duplicar a transformação matemática na definição do item. Um item pode autorar “multihit” ou “condensação” usando a composição existente; a execução final permanece indistinguível para o processador de efeitos.

Definir instância possuída com ID determinístico, revisão e proveniência. A base de inventário deve admitir tipos de item extensíveis, mas implementar inicialmente apenas itens modificadores de carta; não construir sistemas de equipamento/crafting fora do escopo.

Regras de aplicação/remover/substituir são JSON/política do modo. Configuração inicial proposta: adquirir e aplicar fora do combate; aplicação usa uma unidade possuída, registrada como consumida ou vinculada conforme a política escolhida. Não habilitar remoção, devolução ou transferência gratuitamente como regra implícita. As opções precisam ser declaradas pelo conteúdo.

Ao aplicar, a engine fecha a transformação revisionada no ledger existente e registra a origem no item. Inventário e carta não podem manter duas autoridades concorrentes sobre o mesmo patch: inventário controla disponibilidade; ledger controla composição aplicada; o recibo relaciona as duas identidades.

### 3.6 Aplicação atômica e determinística

Comando recebe `itemInstanceId`, `cardInstanceId` e versão observada. A engine valida posse, setting/revisão, atividade/modo, compatibilidade, slots, conflitos, custos e disponibilidade da unidade.

Uma única transação:

1. Fecha e valida a composição resultante.
2. Consome/vincula a unidade e paga custos, quando declarados.
3. Atualiza a carta e o ledger.
4. Emite recibo/commit e atualiza projeções.

Falha não consome item, moeda, slot nem ID. Retry não duplica aplicação. Remoção/substituição, quando habilitadas, também são transações completas. Inventário, origens e alterações entram em checkpoint, journal, replay e branches; branches não compartilham estado mutável.

## 4. Ordem de implementação

### Etapa 1 — Contratos e testes de identidade/apresentação

- Definir fatos de face e as quatro camadas numéricas, sem alterar resolução de gameplay.
- Definir identidade de oferta versus definição versus instância possuída.
- Criar fixtures: dano, cura, Block, stacks, condicionais, custos alternativos, multihit, condensação, carta sem crítico e fórmula sem contexto.
- Formalizar comparações de cores e o que significa “valor-base” em oferta modificada.

Gate: expectativas precisas de custo/efeito para uma mesma carta em oferta e mão; nenhum valor depende de texto autorado no catálogo visual.

### Etapa 2 — Projeção canônica de base, independente da legalidade

- Extrair do serviço atual a leitura de container/composição que não depende de um combate ou candidato executável.
- Projetar custo de uso, alvo genérico, parâmetros, condições e comportamento a partir do conteúdo compilado.
- Resolver ofertas concretas e composição pré-aplicada sem materializar cartas falsas.
- Manter fatos incompletos explícitos em fórmulas contextuais, sem assumir zero.

Gate: oferta mostra números-base e custos reais; carta sem energia ou fora da mão continua legível.

### Etapa 3 — Explicação estruturada de origem e impacto

- Criar projector de cálculo sobre os mesmos traces/providers/bindings.
- Publicar atributos elegíveis, contribuições ativas, condições, unidades, operações, prefixo de origem e etapas de impacto.
- Projetar o prefixo de origem em leitura pura mesmo quando não existe ação legal completa. Não executar buckets de alvo para produzir a face.
- Preservar capture de origem, orçamento distribuído, resultados por impacto e arredondamento; não escalar ou critar duas vezes.
- Limitar quantidade/profundidade de linhas; preparar resumo e detalhe sem payload de histórico inteiro.

Gate: explicação concorda numericamente com execução; ler não modifica RNG/sequence/estado. Condensação sem stacks explica dependências sem fabricar um proc.

### Etapa 4 — REST, batching e sincronização

- Expor projeções de ofertas e de instâncias por consultas read-only versionadas. Proposta: `/runs/{runId}/card-offers/presentation`, `/runs/{runId}/cards/presentation`; ampliar avaliação de combate existente para fatos comuns e breakdown.
- Fixar shapes finais no OpenAPI antes de implementar clientes; não criar rota de mutação que escape do gateway.
- Batch limitado, validação de acesso a oferta/owner/zonas ocultas e detail por capabilities.
- Cache de definição por revisão/fingerprint; cache contextual inclui run/branch/sequence, ator, composição, contexto e alvo quando necessário.
- Incorporar fatos ao snapshot atômico do cliente. Não renderizar flavor provisório e trocar texto depois.

Gate: nomes e ordem oculta de pilhas não vazam; oferta/instância de outra run não é aceita; consulta aquecida não exige simular a run.

### Etapa 5 — Um presenter e uma face em todas as telas

- Extrair a formatação de carta de `CombatPresenter` para presenter passivo compartilhado.
- Migrar recompensas, loja, deck, pilhas, mão, laboratório e antes/depois de transformação.
- Ofertas usam base composta por padrão; combate usa origem contextual, sempre mantendo impacto no preview separado.
- Remover descrições mecânicas fallback do catálogo visual e o `costsKnown=false` forçado para ofertas com projeção válida.
- Manter ação de aquisição/aplicação fora da face e preço de loja separado do custo de uso.

Gate: mesma definição/composição e mesmo modo de leitura produzem a mesma carta; sem nome de alvo ou resultado aleatório contaminando a descrição.

### Etapa 6 — Tooltip sem redundância e inspector útil

- Substituir o resumo repetido pelo breakdown compacto.
- Organizar múltiplos efeitos/parâmetros sem repetir entradas/condições comuns.
- Trocar `inspection_text()` genérico por seções de fatos relevantes; retirar listas globais de fontes sem relação com a carta.
- Adicionar detalhes expansíveis, motivos de condições inativas e seção de modificadores próprios.
- Preservar acessibilidade, hover estável, navegação teclado/controle, paleta configurável e EN/PT com chaves em inglês.

Gate: tooltip explica “por quê”, inspector explica “como”; nenhuma scrollbar inacessível, truncamento enganoso ou oscilação ao selecionar.

### Etapa 7 — Definições e posse dos itens modificadores

- Implementar catálogo tipado, validação de referência à transformação, política e metadados de apresentação.
- Implementar instâncias/inventário imutáveis e sua serialização, hashes e consultas read-only.
- Preservar distinção de upgrade, item modificador, status e relíquia; reutilizar composer/ledger existente.
- Incluir capacidade/compatibilidade em read models sem repetir validators na Godot.

Gate: item pode existir sem carta escolhida, aparece com arte/nome próprios e não muda nenhuma carta apenas por ser adquirido.

### Etapa 8 — Gateway de aplicação e preview antes/depois

- Implementar planejamento puro de aplicação usando `CardTransformationPlanner`/composer ou serviço extraído comum, sem segundo caminho autoritário.
- Expor preview versionado com antes/depois, deltas, custos, compatibilidade e motivos de rejeição.
- Implementar comando canônico proposto `APPLY_CARD_MODIFIER_ITEM`; criar opções de remoção/substituição somente quando a política as habilitar.
- Registrar unidade utilizada, transformação fechada e origens no mesmo commit; garantir idempotência e rollback.
- Atualizar persistência, replay semântico e branches; versionar o executável/schema quando o contrato persistido mudar.

Gate: duas tentativas não usam o mesmo item duas vezes; falha não perde item; após restart/replay a carta mantém a composição demonstrada no preview.

### Etapa 9 — Recompensas, loja e aplicação na Godot

- Ofertas de item entregam posse, não transformação automática de uma carta.
- Criar coleção de itens e fluxo: selecionar item → ver cartas elegíveis → comparar antes/depois → confirmar.
- Mostrar indisponibilidade e motivos dados pela engine; slots e custo não são recalculados na UI.
- Migrar modificadores de afinidade/comportamento apropriados do conteúdo atual para aquisição como itens. Upgrades numéricos continuam upgrades; não converter tudo indistintamente.
- Adicionar exemplos: afinidade elemental, multihit, condensação genérica de stacks, continuação de excesso e proibição de crítico. Cada exemplo referencia os componentes existentes, não cria efeitos especiais por item.

Gate: jogador entende o que está adquirindo, o que está aplicando e o que mudou; comprar item não altera deck implicitamente.

### Etapa 10 — Certificação e documentação

- Comparar custos/efeitos de oferta, deck, mão e carta recebida; cobrir composição pré-aplicada.
- Testar cartas ilegais, zero de energia, efeitos sem alvo, atributos zero/elegíveis, condições falsas, buffs, relíquias, modifiers e custo contextual.
- Conferir separação upgrade/base versus scaling, somas do mesmo bucket, multiplicadores, críticos, Armor/Block e quantização de multihit.
- Certificar posse, consumo/vínculo, remoção/substituição configuradas, falta de slot, conflito, double-click, retry, rollback, restart, hotreload, revision isolation e branches.
- Repetir seed/comandos e comparar receipts/hashes; leituras e previews não consomem RNG.
- Godot headless e renderização em 1280×720 e 2560×1080, EN/PT, mouse/teclado/controle e tooltip de cartas com vários efeitos.
- Medir latência/payload/cache e garantir ausência de flash de texto ou de rerender por cada frame de hover.
- Atualizar OpenAPI, guia de conteúdo, diretrizes de cartas e relatório de validação.

Gate final: a carta recebida corresponde à oferta; o tooltip explica somente o cálculo relevante; cada item modificador tem posse/proveniente e aplicação atômica; nenhuma regra/cálculo se move para a Godot.

## 5. Primeiro marco e fora de escopo

Primeiro marco: **etapas 1–6**, resolvendo a carta oferecida e o tooltip antes de adicionar inventário novo. Segundo marco: **etapas 7–10**, entrega de itens e aplicação ao deck.

Não incluir agora equipamentos genéricos, árvore de crafting, migração silenciosa de saves, novos calculadores, animações exclusivas por item ou rebalanceamento amplo do ato. Frequência/preços dos itens e políticas de consumo/devolução devem estar em JSON e ser afinados no conteúdo; não são defaults universais da engine.
