# Gramática de composição de cartas

Contrato executável das etapas 7 e 8 do [plano do core](../../plans/CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md). Engine version **17**.

## Uma autoridade, duas camadas

Uma transformação permanente é uma entrada do ledger existente da carta. Ela pode ter patches, `requirements` e `compositionRules`. Não existe uma store adicional de afinidades, nem um registry de nomes de elementos no executor.

O resolver reconstrói os patches de todas as entradas ativas, na ordem do ledger. Compila essa base estrutural pelo compiler comum; avalia requisitos e regras sobre esse mesmo snapshot; expande bundles em efeitos comuns e compila novamente o container resultante. A composição não executa gameplay nem consome RNG.

Upgrades numéricos posteriores já estão na base quando os bindings são resolvidos. Status, relíquias, buffs e atributos do personagem não participam da seleção permanente: continuam como fontes contextuais da bucket pipeline. Um binding copia a **base**, não um dano já escalado, evitando aplicar scaling duas vezes.

Efeitos gerados não alimentam a seleção de outras regras. Isso evita recursão e dependência circular implícita. Capacidades estruturais vêm de `capabilityIds` declarados nos componentes e de `effect.<TYPE>` para efeitos raiz; por exemplo, `effect.DAMAGE`. Tags, capacidades e identidades de componentes são comparadas com `Ordinal`.

## Requisitos e seleção

`requirements` da transformação e `when` de cada regra usam o mesmo schema:

```json
{
  "requiredTags": ["ember_affinity"],
  "excludedTags": ["sealed"],
  "requiredCapabilities": ["effect.DAMAGE"],
  "excludedCapabilities": ["behavior.exclusive"]
}
```

Todos os requisitos da transformação precisam valer na composição estrutural **final**, incluindo tags/capacidades adicionadas pela própria transformação. Remover uma transformação que sustenta outra é rejeitado. Requisitos não são condições dinâmicas sobre o ator ou o alvo.

`when` escolhe as regras aplicáveis. `selector` filtra efeitos raiz por `effectTypes`, `componentIds` e `requiredEffectTags`, combinados por AND; listas vazias não restringem aquela dimensão. Não seleciona filhos ou gatilhos de lifecycle. Uma transformação com regras, mas sem nenhuma aplicação correspondente, é incompatível.

Slots/categorias/capacidades continuam com o contrato de [transformações permanentes](permanent-transformations.md). Não há restrição implícita por categoria: conteúdo declara `slotId`, limites e exclusões.

## Ordem e escopos executáveis

Entradas seguem a ordem ativa do ledger, incluindo a posição preservada por replacement. Dentro de cada entrada, regras ordenam por `priority` crescente e depois `ruleId` ordinal. Âncoras e membros ordenam por `order` crescente e depois `componentId` ordinal.

| Scope | Expansão no executor existente |
| --- | --- |
| `BeforeSequence` | Efeitos raiz antes de todos os efeitos estruturais. Uma expansão por regra, não por alvo. |
| `AfterImpact` | Filhos anexados à âncora, depois dos filhos que ela já possuía. Executam por impacto bem-sucedido do pai, incluindo repeats/alvos, segundo as políticas comuns. |
| `BeforeImpact` | Filhos com `childTiming: BeforeParentImpact`, antes da resolução numérica do impacto. Leituras posteriores usam o candidato vivo. |
| `OncePerProc` | Filhos com `executionScope: OncePerParentProc`, depois do impacto; a primeira tentativa reserva o escopo, inclusive quando a chance falha. |
| `AfterSequence` | Efeitos raiz depois de todos os efeitos estruturais. Uma expansão por regra. |

Esses scopes são lowering para o executor comum, não um segundo executor de cartas. Compartilhamento de orçamento, chance e inputs aleatórios seguem o contrato de [sequence-budgets.md](../effects/sequence-budgets.md).

Um pai pulado por chance/condição não executa seus filhos. Um impacto de mudança zero não é reinterpretado pela gramática: vale a semântica do executor comum. Um efeito de sequência permanece independente da chance do pai e usa suas próprias condições, alvos, chance e repeat. Os alvos dos filhos também são configuração de conteúdo; `AfterImpact` não força `TARGET`. Qualquer falha continua descartando a transação inteira.

## Bundles parametrizados

Bundles comuns continuam fechados. Um template pode declarar parâmetros externos **somente** para `payloadBindings.cardEffectComponentId`:

```json
{
  "bundleId": "residual.template",
  "effectComponentParameters": ["base"],
  "components": [{
    "type": "effect", "componentId": "residual", "order": 10,
    "effect": {
      "type": "APPLY_STATUS", "target": "TARGET", "statusId": "residual_status",
      "payloadBindings": [{ "parameterId": "potency", "cardEffectComponentId": "$base" }]
    }
  }]
}
```

O status precisa declarar `potency` e seu perfil/unidade/stages de payload. Este exemplo de extensão precisa dessas definições para publicação; não é um status incluído automaticamente no setting.

```json
{
  "upgradeId": "residual_affinity", "category": "Affinity",
  "requirements": { "requiredCapabilities": ["effect.DAMAGE"] },
  "compositionRules": [{
    "ruleId": "residual", "priority": 10, "namespace": "residual",
    "bundleId": "residual.template", "scope": "AfterImpact",
    "selector": { "effectTypes": ["DAMAGE"] },
    "effectBindings": { "base": "$anchor" }
  }]
}
```

`$anchor` resolve para o ID do efeito estrutural selecionado; também se aceita um ID estrutural explícito. Os parâmetros fornecidos precisam corresponder exatamente aos declarados. Bindings finais precisam apontar para um componente numérico da carta efetiva. Sequence scopes com `$anchor` e múltiplos matches falham por ambiguidade, sem escolher uma base arbitrária. Templates parametrizados não podem ser usados como bundles comuns sem bindings.

A validação de template usa placeholders numéricos transitórios apenas no compiler puro. Eles nunca entram na carta persistida nem na execução. IDs de membros/aliases locais são renomeados como nos bundles comuns; IDs externos vinculados não recebem esse prefixo.

## Revisão, identidades e limites

`CardBundleCompiler.Seal` resolve **todos** os templates no runtime fixado e captura regra + bundle imutáveis no ledger. Uma definição autoral não fechada não pode entrar no ledger. `closedCompositionRules` é um campo interno ignorado no JSON autoral. Reconstituição, reload do processo e replay não consultam novamente o catálogo atual para essas regras. Hot reload de uma base ainda precisa produzir uma composição válida.

Cada expansão usa `namespace_<16 primeiros caracteres do hash canônico do ID da âncora>`. Namespace autoral ASCII de até 47 caracteres deixa espaço para o sufixo e respeita o limite comum de 64. Não depende de hora, ordem de dicionário ou RNG. Colisões são rejeitadas mesmo quando os membros viram filhos e deixam de ser componentes raiz.

Limites: 32 regras por transformação, 64 membros por bundle de regra, 256 expansões por carta, 64 símbolos por lista, 128 caracteres por símbolo. Duplicatas, contradições, escopos sem execução e overflow de ordem falham. Nós gerados, inclusive filhos, respeitam o orçamento comum de 4.096; profundidade e repeats continuam limitados pelo validator de efeitos. Não há truncamento silencioso.

Publicação verifica templates e referências mesmo de regras condicionais inativas. Uma base sem pré-requisitos pode ser inelegível sem invalidar o catálogo inteiro, pois outra transformação pode habilitá-la. A execução e o discovery sempre validam o candidato completo.

## Inspeção e incompatibilidades

`EffectiveCardDefinition` expõe `capabilities` e `compositionTrace`, com transformation ID, rule ID, bundle, scope, âncora e IDs dos membros. O trace participa do fingerprint, junto com o ledger/snapshots e os componentes finais. A inspeção de carta existente expõe isso em `effectiveBase`.

`CardTransformationPlanner.Assess` é uma consulta pura que usa o mesmo planejamento de comandos/discovery. Retorna `isCompatible`, revisão/sequence/step capturados, `changedComponentIds`, trace final e diagnostics com `code`, `message`, `transformationId`, `ruleId` e `subject`. O conjunto de IDs inclui âncoras cuja origem mudou e membros removidos/novos, mesmo quando o payload resultante é igual. `transformation_invalid` representa falhas de política/estrutura ainda sem código especializado; erros não são classificados por parsing de mensagens.

Códigos específicos incluem `missing_tag`, `excluded_tag`, `missing_capability`, `excluded_capability`, `no_matching_rule`, `ambiguous_anchor`, `component_collision`, `invalid_composition`, `match_limit` e `expansion_limit`. Esta consulta de domínio não cria uma rota de mutação paralela. O endpoint especializado de assessment/preview e a apresentação Godot permanecem nas etapas 12/13.

## Conteúdo inicial disponível

O pacote `heroscript.base` publica `core.ember.affinity` para `basic_attack` e `heal`, com slot `affinity` de capacidade 1:

- Dano gera `burning` no alvo, com Skip se ele já tiver sido derrotado pelo impacto.
- Cura gera `regeneration` no próprio personagem.
- Stacks iniciais: 1; duração: 3. A intensidade inicial usa as fórmulas dos status existentes; não promete intensidade proporcional à base da carta. Templates parametrizados acima permitem esse desenho quando o conteúdo declarar o payload.
- Custos não mudam ao receber a tag. Os bundles guardam chaves de nome/arte em metadata; não exigem artes ou traduções novas no executor.

As oportunidades da jornada não foram alteradas para oferecer essa afinidade automaticamente. O catálogo já pode ser usado por cenários e pelos comandos canônicos nos modos/atividades que a autorizarem. Oportunidades e conteúdo completos pertencem à etapa 11.

Regressões cobrem seleção/compatibilidade, limites, bindings, scopes no executor comum, bucket pipeline real para o residual, dez execuções equivalentes, snapshots serializados e store real com reinício/retry/replay/branch isolada. Nenhuma alteração visual Godot integra esta entrega.
