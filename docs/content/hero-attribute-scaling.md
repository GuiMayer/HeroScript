# Atributos e resolução global de efeitos

## Autoridade

O modo aponta para `effectResolutionPolicyId`. A política está no catálogo revisionado `effect-resolution-policies`, participa da publicação do setting e só afeta modos que a selecionam. O executor comum adapta uma cópia imutável do efeito antes de distribuir orçamento, sortear inputs e calcular impactos. Cartas, habilidades, relíquias e outros produtores não recebem calculadores particulares.

O Mundo 1 usa `w1_global_effect_rules`; o showcase anterior e outros settings conservam suas próprias configurações. Uma run já criada mantém suas referências e revisão. Não se alteram arquivos de revisões publicadas nem se migram valores de histórico silenciosamente. A versão executável atual é `32`; runtimes históricos incompatíveis precisam de sua versão correspondente para execução/replay semântico. A versão 32 acrescenta contribuições de inscrições e corrige a resolução de owner de relíquias/modificadores fora de combate; não reinterpreta runs antigas sob essa correção.

## Matemática do Mundo 1

- Chance crítica é percentual: `floor(chance / 100)` tiers garantidos e uma probabilidade `(chance % 100) / 100` de mais um tier.
- Cada impacto sorteia separadamente. Probabilidade zero não avança RNG.
- `critical_damage` é **bônus percentual por tier**, não multiplicador final. O fator é `1 + tier × critical_damage / 100`. Com bônus 100%, tiers 1/2/3 produzem ×2/×3/×4.
- O default de bônus 100 é declarado no binding de `w1_critical_bonus`, com escala 0,01. Chance e Armor ausentes usam zero declarado nos respectivos bindings. Valores reais do ator prevalecem sobre defaults.
- Armor rating A usa `max(A, 0)` e fração restante `K / (A + K)`. K=100 é parâmetro da fórmula `W1_ARMOR_REMAINING_AMOUNT`, com constraint `minimumExclusive: 0`. A projeção de redução reutiliza a mesma fórmula; não mantém outra constante K.
- A ordem inicial é origem → crítico por impacto → vulnerabilidade → Armor → Block → arredondamento final. A ordem e os buckets são conteúdo.
- Armor não é recurso e não é consumido. Block é capacidade com settlement configurado. `piercing` ignora Block; `ignore_armor` é o bypass de Armor configurado na política e nos bindings.

Exemplo: base 10, chance 250%, bônus 100%, Armor 100/K100 resulta em 15 ou 20 antes de Block; Block 5 deixa 10 ou 15. Os números não dependem do nome do recurso alvo.

## Seletores e exceções

`requiredTags`, `excludedTags`, `channels` e `excludedChannels` selecionam regras. Ambiguidade entre regras falha, não escolhe uma ordem arbitrária. Dano direto do Mundo 1 usa canal `effect_amount`; dano residual usa `w1_dot`, sem crítico automático. Custo, cura e condensação não herdam crítico por coincidência. Uma subtração genérica só é dano para esta política quando declara explicitamente `effect.damage`; subtrair mana pode ser dano, pagar um custo não é.

As regras escolhem `amountPipelineId` e, quando possuem crítico, `nonCriticalPipelineId`, `criticalStageId` e `criticalInput`. O template usa captures e perfis existentes de cálculo/probabilidade. Não há fórmula de crítico ou Armor no reducer ou na Godot.

Uma carta pode incluir:

```json
{
  "type": "effect_rule_override",
  "componentId": "rule.no-critical",
  "rule": { "requiredTags": ["effect.damage"], "critical": false }
}
```

`effectId` restringe a um efeito. `armor: false` usa `armorBypassTag` declarado pela regra; sem esse mapping a exceção falha. Overrides também podem ser declarados no próprio efeito em `ruleOverrides`. Proibições prevalecem sobre permissões. Upgrades e transformações podem compor o componente pelo caminho normal de containers. Não usar um pipeline especial em cada carta para simular a exceção.

Os perfis de dano direto usam pontos e scopes Actor/Shared/Target. Parâmetros distribuídos autoram um prefixo de origem e sufixo de alvo; a política acrescenta a etapa crítica ao sufixo quando habilitada. Quantidades transportadas e etapas target-only não recebem outro sorteio/reescalonamento de origem. O diagnóstico `resolutionPolicy` registra ID da política, regra, fingerprint e exceções aplicadas.

## Leitura REST

| GET | Autoridade |
| --- | --- |
| `/api/v1/runs/{runId}/character-attributes` | Ator do encontro ativo quando existente; personagem persistente fora dele. |
| `/api/v1/combats/{combatId}/actors/{actorId}/attributes` | Ator da versão atual do combate associado à run. |

Resposta: `runId`, `combatId`, `runSequence`, `combatStep`, `contentRevision`, `actorId`, `authority`, `attributes` e `criticalRules`.

Cada atributo inclui `attributeId`, `displayName`, `unit`, `baseValue`, `effectiveValue`, `pipelineId`, `pipelineFingerprint`, `contextTags` e até 64 contribuições. `contributionsTruncated` informa truncamento. Não é serializado o trace completo com todas as variáveis/fórmulas ou histórico. `criticalRules` publica tiers garantidos, chance percentual do próximo tier e bônus por tier, calculados sem sorteio.

`attributes` é declarado na política com perfis de projeção, tags de contexto e transformações de unidade (`flatValue`, `displayScale`, `displayOffset`). Base significa somente os bindings de atributos do ator; efetivo acrescenta os providers normais de status, relíquias, modifiers, modo e encontro. Não inclui uma carta específica: bônus condicionais por carta/tag devem continuar sendo conferidos na inspeção daquela carta. As tags publicadas tornam esse contexto explícito.

Todas as leituras são `CaptureOnly`, sem settlements, alterações de recurso, sorteios ou commits. Modos sem política retornam uma coleção vazia, não inventam defaults de outro jogo. A projeção reutiliza os mesmos providers, fórmulas e engine da execução, sem simular uma jornada para abrir o painel.

## Cliente Godot

O gateway lê atributos em paralelo com comandos/capabilities/zonas. `GameSession` valida run, revisão, sequência e passo e publica uma única versão visual. O componente do personagem é passivo e só formata valores; mostra unidades, resumo dos tiers e fontes/contexto no tooltip.

Valores seguem as cores configuráveis do `CardVisualStyle`: branco/base, vermelho/reduzido, azul/aumentado. Chips escuros preservam contraste no tema marfim. EN/PT usam chaves em inglês. A face da carta mostra scaling de origem e a exceção “Cannot crit”; Armor do alvo e resultados críticos permanecem no preview separado/log.

## Verificação e limites

Há testes para tiers/chances/bônus, K configurável e inválido, Armor em outro recurso, ordem com Block, overrides, matriz de cartas/habilidades, leituras sem mutação e jornada de nove encontros com replay. Testes Godot cobrem reconciliação, isolamento de setting, cores, atualização e layout em 1280×720 a 2560×1080.

O balanceamento humano de 25–40 minutos continua alpha: a jornada automática prova executabilidade/replay, não a dificuldade ou diversão. O bônus inicial passou de 50% para 100%; isso exige playtest. Após atualizar o executável, reinicie a API e crie uma nova run para testar a revisão nova.
