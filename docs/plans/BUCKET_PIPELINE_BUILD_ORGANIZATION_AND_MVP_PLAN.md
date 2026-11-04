# Pipeline configurável de builds e alinhamento do MVP

## Objetivo

Adaptar a autoria e a exposição dos cálculos à organização de poder em escalamento do personagem, transformações da carta e condições do combate, preservando o calculador genérico, execução determinística e snapshots imutáveis. Alinhar o setting prototype-test à direção de novembro de 2026: energia universal, escudo passivo, retenção, composição 15+5, Calor/Vazio, companions reativos, inscrições e retry persistente.

Este documento é um plano, não implementação nem certificação. A análise é estática sobre o checkout atual; não foram executados testes nesta preparação. Alterações locais anteriores devem ser inventariadas e preservadas antes da implementação. A direção foi lida em `C:/Users/usuario/.gemini/antigravity/brain/7e671731-6f08-4a09-8880-fb0b3e8f7b66/direcionamento_heroscript.md`.

## Diagnóstico confirmado

| Área | O que existe | Implicação |
| --- | --- | --- |
| Redutor genérico | CalculationEngine recebe request e pipeline; não reconhece nomes de jogos/recursos | Preservar o núcleo e reutilizar operações existentes |
| Operações | Add, AddPercent, Multiply, Set, Minimum, Maximum, ConsumeCapacity e Formula | A organização proposta é majoritariamente autoria, não demanda três novos tipos de cálculo |
| Percentuais | AddPercent usa a entrada do bucket como base fixa para todas as contribuições | Valores do mesmo bucket somam; buckets percentuais consecutivos se multiplicam |
| Multiplicação | Multiply aplica cada fator sequencialmente | Definir explicitamente quais fatores podem multiplicar entre si |
| Contexto | Resolver único, providers de componentes e fontes contextuais, filtros de tags, bindings de stats e recursos | A origem identifica proveniência; não deve escolher operação matemática |
| Inscrições | InscriptionCalculationInfluenceProvider coleta influências dos bônus equipados; há projeção de atributos | Auditar sobreposição entre projeção e influência direta; não presumir que exista duplicação sem teste |
| Estágios | Stages com escopos Shared/Actor/Target, estágios contíguos, checkpoints e receipts em CalculationQuantity | Já há infraestrutura contra reaplicação; grupos de design não podem substituir identidade dos estágios |
| Crítico | Capturas revisionadas e fórmulas genéricas, tiers e scope Impact | Reutilizar; não criar ramo especial para dano ou enum de crítico no redutor |
| Condensação | Recipe, seleção/consumo de stacks, agregação de payload e um proc por ação | Não modelar como simples fator do bucket; continuar na orquestração de efeitos |
| Aplicação | CalculationSettlementPlanner e ResourceMutationReducer separados do cálculo | Consumo de escudo e alteração do recurso continuam transacionais fora do redutor |
| Evidências | Trace, fingerprint, ordenação ordinal, coleções imutáveis e validação de finitos | Preservar contratos e verificar alterações de hashes por revisão |

### Diferença concreta no prototype-test

`Resources/calculation-pipelines.json` separa `increased` (ordem 20) e `heat` (ordem 25), ambos AddPercent. `thermal-potency` lê o recurso heat com scale 0.005 e requiredTags fire. Assim, 50 de Calor fornecem 0,25, ou 25%. Com base 100 e outro aumento de 20%, o resultado antes de outros fatores é 100 × 1,20 × 1,25 = 150. Colocar as duas contribuições no mesmo bucket daria 145. Isso é composição multiplicativa real e deve ser uma decisão publicada do setting.

As pipelines diretas/críticas do setting também declaram armor como Formula e block como ConsumeCapacity. Há arredondamento intermediário em more e arredondamento final; mudar os pontos de arredondamento pode alterar gameplay e replay. Nenhum desses comportamentos deve ser silenciosamente corrigido junto com uma mudança apenas editorial.

## Arquitetura alvo

```text
JSON revisionado do setting
  -> componentes/atributos/regras contextuais autorizadas
  -> plano de execução dos efeitos (hits, alvos, stacks, custos)
  -> CalculationResolver: base, capturas e influências aplicáveis
  -> CalculationRequest imutável + pipeline revisionada
  -> CalculationEngine: operações numéricas e trace
  -> settlement de capacidades + ResourceMutationReducer
  -> novo snapshot, eventos, journal e apresentação
```

### Três dimensões independentes

1. **Fonte:** atributo, inscrição, carta, mutação, status, companion etc. Define proveniência e ciclo de vida, não operação.
2. **Regra matemática:** bônus fixo, aumento percentual compartilhado, fator independente, fórmula ou capacidade consumível. Define agregação e ordem.
3. **Grupo de apresentação/design:** escalamento do personagem, transformação da carta, condição de combate e defesa/aplicação. Serve para autoria e leitura da build.

Uma inscrição pode acrescentar um valor fixo ou um percentual; uma mutação pode alterar base ou estrutura; um status pode gerar soma, multiplicação ou novos efeitos. Não criar regras como `SourceKind.Status => Multiply`. Manter inscrições, itens modificadores de carta, recursos de pagamento e transformações como sistemas separados. Capacidade de inscrições não vira um orçamento implícito de mutações.

### Contrato numérico inicial do setting

```text
base da carta após modificações de base
+ bônus fixos aplicáveis
× (1 + soma dos aumentos percentuais do grupo compartilhado)
× produto dos fatores independentes autorizados da carta
× fator(es) situacionais definidos explicitamente pelo conteúdo
× fator crítico, se aplicável
-> condições/modificadores do alvo
-> mitigação percentual e capacidades, na ordem declarada
-> conversão/arredondamento final
-> aplicação transacional ao recurso alvo
```

A expressão acima é uma política inicial do MVP, não uma ordem obrigatória para toda engine. Estados do ator e do alvo podem estar em estágios diferentes. Respeitar contiguidade e os escopos existentes; um grupo visual de condições pode reunir várias etapas de execução. AddPercent usa frações (0.20 = 20%); Multiply usa fatores (1.20 = ×1,20). Identidades: Add/percentual sem contribuições não alteram entrada; produto vazio é 1. Fórmulas podem ter semântica distinta e precisam de testes próprios.

Exemplo mínimo com campos já existentes, apenas para demonstrar separação matemática, não um bundle completo do MVP:

```json
{
  "pipelineId": "mvp_amount_example",
  "channel": "effect_amount",
  "unitId": "points",
  "stages": [{"stageId":"origin","scope":"Actor"},{"stageId":"destination","scope":"Target"}],
  "buckets": [
    {"bucketId":"flat","stageId":"origin","order":10,"operation":"Add"},
    {"bucketId":"increased","stageId":"origin","order":20,"operation":"AddPercent"},
    {"bucketId":"card_more","stageId":"origin","order":30,"operation":"Multiply"},
    {"bucketId":"origin_state_more","stageId":"origin","order":40,"operation":"Multiply"},
    {"bucketId":"target_state_more","stageId":"destination","order":50,"operation":"Multiply"}
  ]
}
```

IDs são livres. Critério de tags, contribuição de Calor e defesas exigem bindings/regras no bundle real. Não adicionar campos de schema fictícios a exemplos executáveis. A DSL atual avalia operações sequencialmente; expressões convencionais com parênteses não devem ser copiadas sem tradução para a DSL e teste do resultado.

### Crítico, armor e condensação

- Crítico linear: `1 + tier × bonus`; bônus 100% corresponde a 1,0. Chance 150% garante tier 1 e sorteia mais um com 50%; cada impacto sorteia independentemente na política do MVP. Capturas, tiers, RNG, resultado e contexto devem continuar no trace. A política global do setting define aplicabilidade por canal/tags e permite opt-out explícito. Nenhuma lista de nomes de cartas ou health no código.
- Armor: fator de quantidade restante por diminishing returns configurável. Uma configuração inicial possível, a calibrar, é K/(K+A) para A não negativo e K positivo; definir política para armor negativo e limites antes de publicar. Não confundir com escudo consumível nem alterar a fórmula existente sem revisão de gameplay.
- Condensação: selecionar e consumir os stacks previstos e emitir um proc, que pode conter efeitos genéricos. Agregar payloads com unidade, revisão e receipts; a pipeline calcula os números desse proc. Não somar novamente duração/stacks já incorporados nem reaplicar escalamento do ator, crítico ou fatores capturados. Cura, geração de recurso e escudo acumulados também devem ser testados.
- Multihit e encadeamento: orquestração define número/ordem de impactos, alvos e distribuição do orçamento. Cada quantidade segue as políticas existentes; não inferir automaticamente que vários hits preservam dano total. A definição decide Split vs Repeat e quando recapturar contexto.

## Decisões que a implementação deve tornar explícitas

Calor separado é uma janela multiplicativa intencional ou aumento no mesmo grupo? Recomendação inicial: fator situacional separado, limitado e legível, com faixas/autoria que permitam voltar ao grupo aditivo por JSON. Mudar o atual ganho contínuo para limiares é alteração de conteúdo que exige valores e playtest, não comportamento embutido no coletor.

Escudo passivo: configurar reset/concessão na fronteira de ativação do proprietário, preservando proteção durante as ativações adversárias; confirmar o significado de rodada no conteúdo. Energia 3 é orçamento por participante, não um pool implicitamente comum. Fadiga ocorre por compra que continua impossível após o fluxo de reciclagem, conforme configuração explícita. Nenhuma dessas decisões pode surgir como fallback do cálculo.

## Etapas de implementação

### 0 — Baseline e inventário de autoridades

Inventariar pipelines, fórmulas, bindings e política global de cálculo em todos os settings, incluindo prototype-test e ascendant. Registrar a precedência de pipeline explícita vs default do modo. Levantar alterações locais sem commitar material alheio. Congelar casos representativos com revisão/seed/identidade e resultados: normal, crítico por tier, multihit, status, companion, condensação, shield, armor, outro recurso alvo, cura e signed delta. Medir custo de resolver/preview e comando completo.

Aceite: corpus reproduzível, resultados/hashes antigos e regras efetivamente executadas documentados. Não assumir que a documentação histórica representa o runtime atual.

### 1 — Contrato de autoria e taxonomia

Publicar uma matriz fonte → contribuição → bucket → momento de captura → unidade → aplicabilidade → apresentação. Identificar cada fator independente e justificar por que multiplica. Definir nomes/IDs das pipelines do MVP, arredondamentos e defaults explícitos no setting. Preservar distinção base/flat/increased/more. Nenhum comportamento numérico depende do rótulo de grupo visual.

Aceite: exemplos calculados à mão, incluindo 150 vs 145, produto vazio, aumentos negativos permitidos, bounds e políticas de ausência; decisões de game design marcadas separadamente de garantias do motor.

### 2 — Schemas, validação e política de publicação

Reutilizar CalculationPipelineDefinition e validação existentes. Completar a verificação de referências entre efeito/canal/pipeline/bucket/formula, compatibilidade de unidades e estágios, valores finitos e referências à revisão. Garantir erro visível para influência destinada a bucket inexistente quando ela deveria participar, sem obrigar contribuição de outro canal a se aplicar. Adicionar diagnóstico de autoria para ambiguidades (fator 20 vs 1.20, percentuais em buckets consecutivos), sem rejeitar configurações legítimas por heurística.

Metadados de agrupamento e descrição só devem ser acrescentados se os read models existentes não bastarem. Se necessários, devem ser opcionais, versionados e declarativos, com fallback por IDs para conteúdo antigo. Definir explicitamente se cada metadado pertence ao conteúdo autoritativo ou apenas à apresentação antes de calcular fingerprints; não retirar dados de hashes por conveniência.

Aceite: testes de bundles válidos/inválidos, conflitos, referências ausentes e settings antigos sem novos campos. Não introduzir novas operações apenas para renomear conceitos já suportados.

### 3 — Coleta e prevenção de contagem dupla

Auditar CardComponentInfluenceProvider, providers contextuais, bindings de stats/recursos e InscriptionCalculationInfluenceProvider. Rastrear cada bônus equipado desde a projeção de atributo até a contribuição efetiva. Definir uma única rota para cada benefício; influência numérica direta e projeção podem coexistir apenas quando representam efeitos distintos. Garantir identidades estáveis por instância/owner e ordenação ordinal. Companion deve contribuir por componentes/eventos/status genéricos ou pelo atributo correspondente, sem provider especial do Elemental da Água.

Aceite: uma mesma contribuição não aparece duas vezes; dois itens legítimos somam/multiplicam conforme JSON; isolamento por personagem, target, setting e revisão; preview não modifica loadout ou carta.

### 4 — Políticas JSON de escalamento e crítico

Configurar pipelines direct/critical e bindings de forma coerente no prototype-test. Preservar a política global de seleção existente e as exceções autoradas da carta; substituir repetições desnecessárias por defaults do modo somente quando equivalentes. Manter chance, bônus e captura por impacto no mecanismo atual, sem sorteio no redutor. Auditar canais não críticos, efeitos contínuos e recursos não-health. O setting decide onde crítico é permitido; o motor não presume que todo efeito ou toda cura critica.

Aceite: 0/50/100/150/200/250%, linear ×2/×3/×4 com bônus 100%, opt-out, fontes diferentes, multihit e RNG ordenado. Preview não deve consumir o RNG autoritativo; exibir resultados condicionais/distribuições quando o resultado real depende de sorteio.

### 5 — Transformações e execução estrutural

Reutilizar componentes de carta, ledger de mutações, execução genérica e recipes. Upgrade de base entra na BaseTrace uma vez. Multihit, chains e condensação continuam preparando o plano, não se tornam propriedades especiais de CalculationEngine. Validar custo e condições com a transação canônica; pagamentos de energia, banimento ou descarte são separados do valor calculado. Definir revalidação de alvo e comportamento de efeitos que geram outros efeitos, com limites de trabalho existentes.

Aceite: repetir plano não duplica aplicação; patches/componentes não mutam definição original; split/repeat e transferências respeitam JSON; um proc de condensação consome all-selected uma vez e falha sem estado parcial.

### 6 — Quantidades, estágios e momento de captura

Auditar CalculationQuantity, IncorporatedStages, ContextId, CalculationQuantityAggregation e distribuição. Documentar para cada transição se o valor é base cru, escalado no ator, capturado por stack ou final após defesa. Preservar receipts compatíveis ao somar/dividir e rejeitar unidades/revisões incompatíveis. Testar mudanças do alvo e estado entre impactos: o JSON deve dizer o que é capturado e o que reavalia. Se faltar capacidade concreta, estender esse contrato genérico; não limpar receipts nem reutilizar estágios artificialmente para forçar recálculo.

Aceite: crítico, Calor e escalamento não são reaplicados em payload já escalado; defesas do novo alvo são aplicadas onde devido; capturas estáveis não mudam por hotreload ou alteração posterior do ator.

### 7 — Condições e defesa do MVP

Configurar Calor, Molhado e efeitos do companion com tags e fórmulas contextuais; documentar se somam em grupo compartilhado ou são fatores independentes. Configurar armor e escudo passivo com os serviços existentes. O reducer não descobre evento, recurso ou ator; receberá só números resolvidos. Eventos de reação usam o barramento genérico, com prevenção de recursão e ordem estável.

Aceite: testes de limiares, igualdade na borda, expiração, Sleep e cooldown; armor zero/alto/negativo conforme política; consumo de shield menor/igual/maior que impacto; settlements efetivos sem quantidade negativa ou uso duplo da capacidade. Fadiga e custos de Vazio não recebem defesa/crítico por acidente: suas políticas/tags devem ser explícitas.

### 8 — Trace, API e interface Godot

Expor pelos read models existentes base alterada, aumentos agregados, fatores independentes, condição aplicável, crítico e mitigação, com proveniência e revisão. Reutilizar apresentação/evaluation/atributos e resolução histórica; criar endpoint novo somente se houver necessidade comprovada sem suporte atual. Não fazer nova leitura por contribuição no caminho de uso da carta.

Godot mostra efeitos base na carta, valor contextual no preview e cálculo curto no tooltip: “Base 10 + 5; aumentos +40%; Calor ×1,25; crítico variável; defesa do alvo …”. Separar grupos editoriais das fases matemáticas; só mostrar fatores pertinentes ao efeito/target. Declarar quando não há alvo ou o resultado depende de sorteio. Reusar os componentes existentes no laboratório, combate e seleção/recompensas.

Aceite: API e UI concordam com trace; EN/PT, estados sem alvo, múltiplos impactos e replay; nenhuma aritmética de gameplay na Godot, nem valores transitórios de outro contexto.

### 9 — Conteúdo alinhado à direção

Migrar o prototype-test em nova revisão: energia padrão 3 por participante, retenção/cadência/limite conforme decisões, composição 15+5 e defesa passiva declarada. Revisar cartas de Fogo/Vazio e companion para representar o papel de cada contribuição sem fator redundante. Inscrições alteram atributos do personagem; modificadores alteram o container da carta. Relíquias não são oferecidas neste setting, mantendo suporte genérico para outros jogos. Rank/polaridades/capacidade/economia/bestiário são auditados como dependências, não declarados concluídos só por esta migração numérica.

Aceite: capítulo jogável, recompensa Decompilar/Mochila, ajuste e retry sem repetição indevida de diálogo. Registrar mudanças de balanceamento e exemplos antes/depois; playtest é necessário para aprovar diversão e equilíbrio.

### 10 — Migração, compatibilidade e hotreload

Não sobrescrever revisões já usadas por runs existentes. Mudanças apenas organizacionais devem provar equivalência; mudanças intencionais de cálculo exigem nova revisão e novos resultados esperados. Sem defaults globais que alterem ascendant/default/outros settings. Conteúdo antigo continua com sua pipeline e ordem originais. Hotreload publica nova revisão; ativação em run existente usa preview/validação/transação atuais, com recusa explícita quando não for compatível.

Aceite: histórico antigo continua reexecutável com seu conteúdo; restart/replay e branches preservam hashes daquela revisão. Nova revisão tem seus próprios hashes esperados; não exigir que duas regras diferentes produzam o mesmo hash. Migração repetida é idempotente, sem duplicar bônus, mutações ou RNG.

### 11 — Certificação e desempenho

Executar suítes de cálculo, perfis, políticas, settlement, distribuição, crítico, condensação, recursos e inscrições. Adicionar jornadas API com seed/comandos fixos em stores independentes: comandos, falha transacional, restart, replay e branches. Comparar hashes sucessores dentro da mesma revisão e provar inputs/snapshots intactos. Embaralhar ordem de entrada dos providers para verificar normalização estável onde a semântica é de conjunto; conservar a ordem explicitamente significativa de efeitos/hits/RNG.

Medir resolver, previews, POST PLAY_CARD e END_TURN em Release com persistência e tamanhos crescentes. Reportar p50/p95/p99, aquecimento, amostras e ambiente. Não reduzir traces, retirar validação ou mudar hashes silenciosamente para atingir uma meta; considerar caches por revisão e estrutura compilada somente se a medição justificar. O gate prévio de 100 ms do turno continua aberto até nova certificação, independentemente deste trabalho.

Aceite: zero falhas nas suítes afetadas, exemplos numéricos explicáveis pelo trace, paridade preview/aplicação quando as condições são iguais e nenhum consumo autoritativo no preview. Não prometer determinismo binário entre plataformas diferentes sem teste específico da representação float atual.

### 12 — Documentação e fechamento

Atualizar docs/systems/damage/damage-pipeline.md, guia de críticos, autoria de influência e contratos de apresentação com exemplos JSON executáveis e unidades explícitas. Documentar convenções do MVP separadas das capacidades da engine e da terminologia de design. Acrescentar explicação de retorno relativo decrescente vs redução efetiva por diminishing returns.

Manter Markdown canônicos na main e, quando houver novos guias públicos, incluí-los no manifesto da branch de documentação para publicação automática. Planos internos permanecem fora da navegação pública. Registrar testes, revisões, hashes, desempenho, decisões e pendências reais; não marcar playtest, API real ou distribuição standalone como realizados sem evidência.

## Sequenciamento e commits

Etapas em ordem 0–12. Commits por etapa ou por lote pequeno quando a etapa exigir conteúdo e motor separados. Antes de cada commit, revisar o índice para preservar o trabalho preexistente; não usar add global. As etapas 2–6 só alteram o motor quando houver lacuna demonstrada por teste. Mudanças apenas de autoria devem usar a infraestrutura atual. Cada etapa tem aceite verificável; não avançar sobre falha conhecida sem registrar/corrigir sua causa.

## Matriz mínima de exemplos verificáveis

- Base 10 + flat 5, increased 0,20 e 0,30 no mesmo bucket: 22,5 antes da conversão.
- Dois fatores 1,20 e 1,25 em Multiply: ×1,50; os mesmos aumentos num AddPercent: ×1,45.
- Nenhuma contribuição: identidade preservada; rejeitar NaN/infinito e bounds inválidos.
- Crítico linear com tier 2 e bônus 1,0: ×3; não aplicar novamente ao condensar quantidade já marcada.
- Armor K=100 e A=100, se adotada essa política: metade do valor anterior; shield consome só o restante aplicável, na ordem configurada.
- Mesmo efeito vindo de carta, status, companion ou evento: mesmo cálculo com o mesmo request resolvido, independente da proveniência editorial.
- Redução de mana e cura de outro recurso seguem os mesmos contratos de cálculo/aplicação, sem decisão especial por nome do recurso.
- Condensação de stacks de cura/escudo/recurso e de dano: um proc, consumo único e composição numérica sem dupla contagem.
- Prévia, recusa e falha de pagamento: nenhuma mutação de estado, inventário, pilha, conteúdo ou RNG autoritativo.
