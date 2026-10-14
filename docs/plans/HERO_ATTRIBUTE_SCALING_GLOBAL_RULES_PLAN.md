# Scaling global por atributos: armor e críticos

> Implementação de 2026-10-14: contratos, política revisionada, executor comum, pipelines, projeção REST e painel integrados. Consulte [autoria e contrato](../content/hero-attribute-scaling.md) e [validação](HERO_ATTRIBUTE_SCALING_GLOBAL_RULES_VALIDATION.md). O balanceamento humano do ato permanece alpha.

Data: 2026-10-14. Estado: plano; implementação não iniciada nesta revisão.

## 1. Objetivo e decisões

Aplicar atributos do herói aos efeitos das cartas por uma política global de cálculo, apresentar esses atributos no painel do personagem e permitir exceções locais explícitas. A engine resolve regras, cálculos e sorteios; Godot somente apresenta projeções e envia comandos.

Decisões confirmadas:

- Armor reduz magnitudes elegíveis com diminishing returns.
- Chance crítica usa tiers, inclusive acima de 100%.
- Dano crítico padrão é **100% de bônus por tier**, não chance de crítico nem multiplicador final ×1.
- Acúmulo confirmado pelo autor: **linear**, não exponencial.
- Cartas elegíveis podem critar por padrão, sem repetir configuração de crítico em cada carta.
- Uma carta/componente pode declarar que não pode critar.
- Atributos e seus valores efetivos aparecem no painel de detalhes do personagem.

“Global” significa global à configuração de jogo selecionada, capturada pela run. Não será uma variável estática que mistura settings, usuários ou branches. Um perfil compartilhado pode ser herdado por vários modos/settings; overrides permanecem revisionados e data-driven.

Propostas iniciais, não escolhas adicionais já feitas pelo autor:

- Curva de armor com constante K, inicialmente 100. K continua configurável.
- Armor afeta dano direto antes de consumir Block; o jogo pode configurar outro escopo/ordem.
- Dano direto é criticável por padrão, independentemente de ser ataque, magia ou efeito de outra origem. Cura, pagamento, DoT e procs de condensação não ganham crítico automaticamente nesta primeira configuração. A política pode habilitá-los depois.
- Manter a chance inicial atual do herói em 15%; alterar o dano crítico para 100%. Não interpretar o novo padrão como 100% de chance.
- Ausência de armor/chance: defaults declarados no conteúdo de 0. Ausência de dano crítico: default declarado de 100%. Não espalhar fallbacks em executores ou na UI.

## 2. Diagnóstico do projeto

### Infraestrutura aproveitável

- `CalculationResolver` já é a entrada numérica comum para origens de efeitos, parâmetros e probabilidades.
- `CalculationEngine` possui buckets, scopes Actor/Shared/Target, fórmulas, influências, checkpoints e traces.
- `StatInfluenceProvider` coleta atributos; a composição de providers integra statuses, relíquias e modifiers.
- `RunModeDefinition` já declara pipelines habilitados e defaults por canal. No mundo 1, `effect_amount` aponta para `w1_amount`.
- `EffectTriggerExecutor` executa inputs aleatórios por impacto com identidades/escopos determinísticos.
- `ascendant_critical_probability` já separa tiers garantidos da fração probabilística; o mundo 1 usa `w1_critical_amount` em efeitos explicitamente preparados.
- Inspeção de cartas e `RandomOutcomePreviewProjector` já publicam resultados e alternativas condicionais. Não é necessário criar um calculador de dano na Godot.
- `EffectiveCardResolver` separa base/upgrades permanentes do scaling contextual. Preservar essa fronteira.

### Lacunas

1. Cartas ainda repetem `randomInputs`, captures, IDs de pipeline e stages de crítico. Uma carta nova sem esses blocos não herda o comportamento desejado.
2. Os perfis atuais usam `critical_multiplier` (herói: 1,5), convertido em bônus por captures com base -1. É preciso introduzir a semântica explícita de `critical_damage` em porcentagem.
3. Há fórmulas matemáticas de diminishing returns, mas armor não está integrado à política global do mundo 1. A fórmula existente `ARMOR_REDUCTION` depende do dano recebido: não equivale a A/(A+K).
4. O painel `character_sidebar_view.gd` apresenta recursos, statuses, relíquias e modifiers, mas não apresenta uma projeção de atributos efetivos/derivados.
5. Faltam contratos globais de elegibilidade/exceção de crítico e provas de cobertura para todas as origens de dano.

**Observação de matemática:** expressões inline de `RuntimeFormulaEvaluator` são sequenciais, da esquerda para a direita; não assumem precedência matemática convencional. Especificar as fórmulas novas com operações explícitas e testar seus resultados. Não “corrigir” a fórmula crítica atual apenas pela aparência textual.

## 3. Regras matemáticas

### 3.1 Armor

Para armor efetivo A não negativo e K positivo:

```text
reduction = A / (A + K)
remaining = K / (A + K)
amount_after_armor = amount_before_armor × remaining
```

Exemplos de referência com K=100:

| Armor | Redução | Parcela restante |
| ---: | ---: | ---: |
| 0 | 0% | 100% |
| 25 | 20% | 80% |
| 100 | 50% | 50% |
| 300 | 75% | 25% |
| 900 | 90% | 10% |

Armor não é recurso consumível. Não será reduzido ao absorver dano nem tratado como Block. A curva limita a redução assintoticamente; armor finito não concede imunidade. Não acrescentar cap de redução silencioso: se um setting desejar cap, deve declará-lo.

Definir no perfil o tratamento de valores negativos, limites de magnitude e K inválido. Configuração inicial: rating não negativo, K>0, valores finitos; entrada inválida falha na validação. Penetração/ignorância de armor é uma exceção específica, separada da exceção de Block. A tag existente `piercing` não passará a ignorar armor por coincidência.

### 3.2 Chance crítica por tiers

```text
C = chance efetiva em pontos percentuais, sem cap de 100%
guaranteed_tiers = floor(C / 100)
extra_probability = (C mod 100) / 100
tier = guaranteed_tiers + extra_success
```

| Chance | Resultado |
| ---: | --- |
| 0% | Tier 0 |
| 15% | 15% de tier 1; caso contrário tier 0 |
| 100% | Tier 1 garantido |
| 150% | Tier 1 garantido + 50% de tier 2 |
| 250% | Tier 2 garantido + 50% de tier 3 |

Cada impacto tem seu próprio sorteio, inclusive em multi-hit e múltiplos alvos, conforme o scope Impact existente. Registrar a política de consumo de RNG nos casos 0%/100%/tiers garantidos; reutilizar ou modificar conscientemente o contrato existente, nunca introduzir draws ocultos.

### 3.3 Dano crítico linear

```text
B = critical_damage efetivo / 100
critical_factor = 1 + tier × B
amount_after_critical = amount_before_critical × critical_factor
```

Com dano crítico 100%: tier 0 ×1, tier 1 ×2, tier 2 ×3, tier 3 ×4.
Com dano crítico 150%: tier 1 ×2,5; tier 2 ×4; tier 3 ×5,5.

O bônus cresce linearmente com o tier; não usar `(1+B)^tier`. Dano crítico e chance crítica são atributos independentes. Valores negativos e não finitos são rejeitados/normalizados somente pela política declarada. Limites técnicos para quantidade de tiers e magnitude devem ser explícitos e validados, não um cap de game design escondido em 100%.

## 4. Arquitetura proposta

### 4.1 Política global de resolução de efeitos

Criar catálogo revisionado `effect-resolution-policies`, referenciado pelas regras de combate/modo, contendo:

- Seletores por semântica, tags, canal, operação e campo numérico, sem depender de ID/nome de carta.
- Elegibilidade de crítico e de armor.
- Pipeline numérico e inputs aleatórios padrão.
- IDs de atributos, unidades e defaults declarados.
- Política de overrides, prioridade e tratamento de conflitos.

Formato conceitual, a consolidar na etapa de contratos:

```json
{
  "policyId": "hero_attribute_scaling",
  "attributeProfileId": "hero_combat_attributes",
  "rules": [{
    "ruleId": "direct_damage",
    "selector": {
      "requiredTags": ["effect.damage"],
      "excludedTags": ["damage.residual"],
      "resourceOperation": "SUBTRACT",
      "resourceField": "Current"
    },
    "amountPipelineId": "hero_direct_damage",
    "critical": {"enabledByDefault": true, "scope": "Impact", "inputTemplateId": "tiered_critical"},
    "armor": {"enabledByDefault": true, "ratingAttributeId": "armor", "curveParameter": 100}
  }]
}
```

Reutilizar a semântica canônica de dano da engine. Uma subtração de pagamento/custo não deve virar dano por ser SUBTRACT. Da mesma forma, o efeito pode diminuir mana ou outro resource: não usar o nome `health` como condição. Se outra alias representar dano, deve publicar/receber a mesma semântica de cálculo.

### 4.2 Resolver único, puro e anterior aos cálculos

Introduzir um `EffectResolutionPolicyResolver` comum no caminho do `EffectTriggerExecutor`, não no controller da carta. Ele recebe efeito, contexto, política revisionada e overrides efetivos; retorna um plano imutável contendo regras aplicadas, parâmetros, inputs aleatórios e pipeline.

O resolvedor não calcula dano, não consome recursos e não sorteia. A execução continua em CalculationResolver/CalculationEngine e no RNG determinístico existente. Não usar um pipeline com crítico apenas no `PLAY_CARD` e outro para ações/inimigos/statuses.

Resolver uma única vez os defaults do efeito; usar snapshots do contexto em cada impacto para as influências dinâmicas. Reusar exatamente o mesmo contrato em execução, inspeção, ações legais, intenções, continuação e condensação. Não manter um caminho especial de crítico da UI ou da carta.

### 4.3 Exceções locais como componentes

Adicionar declaração componente de override de regra, não bloco de cálculo duplicado. Exemplo conceitual: `effect_rule_override` com selector de efeitos e `critical: Disabled`.

- Ausência = herda a política global; não significa “crítico desligado”.
- Aplicação pode atingir todos os efeitos da carta ou um componente específico.
- Upgrade permanente pode adicionar/remover o componente; status/modifier temporário declara override contextual próprio.
- Normalizar essas declarações para um contrato genérico de resolução de efeitos. O processador não deve precisar saber se vieram de carta, habilidade ou relíquia.
- Bônus locais de chance/dano crítico continuam como contribuições numéricas nos canais, não como outro algoritmo de crítico.
- Proposta inicial de conflitos: negação vence no mesmo nível de prioridade; prioridades e precedência declaradas no perfil. Validar conflitos impossíveis antes da publicação.
- Não permitir que um pipeline explícito contorne a regra “não pode critar” e ainda consuma um sorteio crítico.

### 4.4 Ordem inicial da bucket pipeline

```text
base da definição + upgrade permanente
→ scaling do ator: flat / increased / more
→ distribuição por sequência, quando configurada
→ crítico do impacto, com chance/dano crítico capturados
→ modifiers do alvo / vulnerabilidade
→ armor do alvo
→ consumo da capacidade de Block
→ limites e arredondamento final
→ reducer do resource alvo
```

Scopes: origem Actor; crítico por impacto Shared; armor/defesas Target. No multi-hit, não aplicar scaling de origem novamente depois da distribuição. Não arredondar a cada novo bucket se o perfil pede arredondamento final. Armor e Block são independentes; inserir armor antes de Block é uma decisão inicial configurável do setting.

O pipeline somente recebe pedido de cálculo/influências e devolve resultado/traces/settlements declarados. Não consulta mão, deck, câmera, botão, animação ou nome da carta.

### 4.5 Projeção de atributos e REST

Adicionar serviço read-only de atributos efetivos, usando os mesmos bindings/providers/pipelines. Contratos incluem:

- IDs, nomes/chaves, unidades; valor-base, valor efetivo e contribuições por origem.
- Armor rating e redução derivada, identificando curva, K e contexto.
- Chance crítica efetiva, tiers garantidos e probabilidade adicional.
- Dano crítico efetivo e fatores exemplificativos por tier.
- Contribuições condicionais separadas. Não fingir que existe um único valor efetivo universal se uma relíquia só vale para ataques específicos.
- Run/actor/combat IDs, setting, branch, sequência, revisão e fingerprint de contexto.

Rotas propostas: `GET /api/v1/runs/{runId}/actors/{actorId}/attributes` e `GET /api/v1/combats/{combatId}/actors/{actorId}/attributes`. Projeção de mapa usa o ator persistente; combate usa o ator do encontro ativo. Não ler o personagem persistente desatualizado durante o combate.

Integrar a projeção ao bundle de sincronização existente da Godot, sem chamadas por carta/por frame. Controller apenas valida entrada, chama serviço e devolve DTO. Edições/cheats continuam nos comandos canônicos com capabilities; esses GETs não fazem mutação.

A projeção geral do herói exclui bônus intrínsecos de uma carta não selecionada. A inspeção da carta acrescenta o contexto local, regras globais aplicadas e suas exceções. Prévia aleatória apresenta alternativas condicionais, não promessa de resultado nem range global fictício. Não consumir o RNG autoritativo numa consulta.

## 5. Implementação sequencial

### Etapa 1 — Contratos, unidades e casos de referência

1. Formalizar percent versus factor, armor rating versus resource, tiers e limites técnicos.
2. Criar tabelas/testes das fórmulas antes de alterar conteúdo.
3. Definir seletores de dano direto, escopos iniciais de DoT/condensação, order armor/Block e conflitos de overrides.
4. Reutilizar operações explícitas da MathEngine; K é constante do perfil, não hardcoded no reducer.

Gate: fórmulas independentes demonstram os números das tabelas, sem Godot e sem depender de uma carta específica.

### Etapa 2 — Catálogos globais e compilação revisionada

1. Implementar definições imutáveis da política e do perfil de atributos/defaults; referenciar pelo modo/regras de combate.
2. Incluir recursos na compilação de settings, validação do grafo e fingerprint da revisão.
3. Validar referências, unidades, bounds, scopes, prioridade, templates e overrides.
4. Definir como StatInfluenceProvider resolve o default declarado para atributos ausentes. Não copiar default 100 em dezenas de entities.

Gate: dois settings com curvas/defaults diferentes coexistem sem vazamento; conteúdo inválido não publica.

### Etapa 3 — Aplicação global e overrides de efeitos

1. Implementar o resolver puro de políticas e integrá-lo ao caminho comum de efeitos.
2. Conectar templates de probabilidade e pipelines aos efeitos elegíveis que não possuem configuração local.
3. Implementar o componente de override e sua composição com upgrades/modifiers.
4. Aplicar a mesma resolução em habilidades, relíquias, inimigos, continuação, condensação e inspeção.
5. Incluir políticas aplicadas no orçamento de trabalho, cache/fingerprint e diagnóstico.

Gate: uma carta mínima com custo + dano, sem bloco de crítico, recebe crítico global; a mesma definição com override Disabled não sorteia nem aplica fator crítico. Definições de entrada permanecem imutáveis.

### Etapa 4 — Pipelines de crítico, armor e settlements

1. Implementar captures efetivos de `critical_chance`, `critical_damage` e armor.
2. Reutilizar tiers garantidos + fração; converter `critical_damage` percent em bônus e aplicar fórmula linear.
3. Integrar armor no scope Target, sem consumo e sem resource implícito.
4. Separar bypass de armor de bypass de Block.
5. Preservar distribuição de orçamento por impacto, quantização, arredondamento final e limites do reducer.

Gate: teste de base 10, chance 250%, bônus crítico 100%, armor 100/K100 produz 15 no tier 2 e 20 no tier 3 antes do Block. Com Block 5, saúde/mana alvo diminui 10 ou 15 e Block é consumido uma única vez.

### Etapa 5 — Prévia, legalidade, intents, logs e replay

1. Tornar inputs críticos herdados visíveis em `CardInspectionService` e `RandomOutcomePreviewProjector`.
2. Publicar elegibilidade, chance, tiers, bônus, alternativas e contribuição de armor na avaliação existente.
3. Manter as autoridades únicas de execução/inspeção/intents; remover duplicação local de resolução.
4. Registrar policy IDs/fingerprints, tier, roll, curva, before/after de buckets e settlements nos traces existentes.
5. Atualizar caches para considerar política, atributos efetivos, alvo, overrides e revisão; nada de cache apenas por definição de carta.

Gate: consulta não muda RNG/sequence/resource; preview e execução sobre o mesmo contexto concordam nas alternativas; replay/branch reproduzem os fatos sem reroll ou cálculo na UI.

### Etapa 6 — Serviço de atributos e sincronização REST

1. Criar projeção do personagem persistente e do ator ativo com serviço comum.
2. Implementar rotas e DTOs versionados, com autorização/capabilities para detalhe sensível.
3. Incorporar a leitura ao bundle do gateway e ao snapshot atômico de GameSession.
4. Limitar detalhes/contribuições e evitar serializar histórico completo.

Gate: mapa/combate mostram a autoridade correta; trocar setting/branch/status não reutiliza atributos de outro contexto; GET não cria eventos ou commits de jogo.

### Etapa 7 — Migrar conteúdo Ascendant sem duplicação

1. Configurar o perfil global para o mundo 1; outros settings usam suas próprias referências.
2. Adotar `critical_damage: 100` e manter chance inicial 15%; adicionar armor conforme balanceamento, com default 0 declarado.
3. Retirar das cartas os inputs/captures/pipelines de crítico redundantes, preservando valores-base, upgrades, repeat/distribution, tags e condições locais.
4. Converter bônus específicos de cartas para contribuições/overrides, sem perder sua identidade mecânica.
5. Migrar statuses, relíquias e inimigos que influenciam crítico para as unidades novas; não interpretar 1,5 como 1,5%.
6. Adicionar uma carta não criticável e casos de teste com dano em outro resource.
7. Publicar revisão nova, sem reescrever definições/snapshots já publicados. Runs antigas não recebem a regra nova retroativamente; incompatibilidade de versão executável deve ser explícita, não recalcular silenciosamente o passado.

Gate: busca no catálogo não encontra blocos críticos genéricos repetidos nas cartas; todos os efeitos elegíveis recebem a mesma política. Executar a jornada dos nove encontros e revisar o balanceamento, pois trocar bônus antigo de 50% para 100% muda o dano esperado.

### Etapa 8 — Painel do personagem e apresentação de cartas

1. Adicionar seção **Atributos** ao sidebar: Armor, redução, chance crítica e dano crítico.
2. Mostrar units/percentagens, resumo de tiers, contribuição de buffs e detalhes condicionais em inspeção persistente.
3. A Godot recebe valores derivados prontos; não implementar fórmula de armor/tier em GDScript.
4. Cartas exibem números de scaling de origem e regra “não pode critar”, mantendo branco/base, vermelho/reduzido e azul/aumentado.
5. Armor do alvo e crítico sorteado ficam no preview separado/logs, não um dano determinístico prometido na face da carta sem alvo.
6. Preservar estrutura flutuante/colapsável, reconciliação sem piscar, acessibilidade e EN/PT. Incluir atributos no projection key do sidebar.

Gate: atributos atualizam após buff/debuff, nova run e troca de branch; preview não contamina a carta com nome do alvo; sidebar fechado continua só com a flecha. Verificar 1280×720 e 2560×1080.

### Etapa 9 — Certificação transversal e documentação

1. Tabela crítica: chances 0/15/99/100/150/200/250/300; bônus 0/50/100/150/200.
2. Tabela de armor: rating 0/25/100/300/900; vários K; limites/erros.
3. Matriz: dano direto, alias SUBTRACT com semântica de dano, custo, cura, DoT, condensação, inimigo, relíquia, continuação, multi-hit, múltiplos alvos e carta sem crítico.
4. Provar upgrades alterando base versus stats alterando buckets; provar modificadores aplicados uma única vez.
5. Repetir seed/comandos e comparar RNG/receipts/checkpoints/hashes; cobrir target loss, mortes e continuação sem re-scaling de quantidades transportadas.
6. Repetir leituras de atributos/preview e confirmar estado/hash imutável, sem crescimento de passos RNG.
7. Medir latência e tamanho da projeção; não executar uma simulação completa para abrir o sidebar.
8. Documentar perfis, exemplos JSON, endpoints e limites da prévia aleatória.

Gate final: contratos unitários, integrações REST e Godot headless/renderizadas aprovados; sem segundo calculador, sorteio duplicado, regra por carta ou autorrecalcular replay com conteúdo atual.

## 6. Critérios finais de aceite

- Nova carta de dano direto pode ser autorada sem configurar crítico.
- A mesma política cobre todos os produtores de efeitos habilitados no setting.
- Exceção Disabled impede cálculo/sorteio crítico sem impedir scaling/armor.
- Armor diminui valores elegíveis em qualquer resource configurado, sem consumir armor.
- 150%/250% de chance e bônus crítico 100% obedecem às tabelas confirmadas.
- Os números publicados no painel e na avaliação vêm dos mesmos cálculos usados pela execução.
- Upgrades permanentes, atributos, statuses e modificadores permanecem camadas distinguíveis nos traces.
- Setting/revisão/branch/contexto fazem parte da autoridade e do cache; dados-base e histórico continuam imutáveis.
- Tuning de K, atributos iniciais, filtros e ordem de buckets não exige mudança na Godot.

## 7. Fora desta primeira entrega

Não adicionar resistências elementais, evasão, acerto, crítico de cura/DoT por padrão, árvore de atributos, progressão permanente de poder ou outro sistema de defesa. A estrutura permite políticas adicionais sem duplicar processamento; estas mecânicas precisam de decisões próprias depois.
