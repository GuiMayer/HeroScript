# Distribuição de quantidades

## Estado atual

A etapa 8a do [plano do core](../../plans/CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md) implementa a base numérica para dividir um orçamento. A integração automática com impactos e cartas ainda depende da etapa 8b. Não existe um campo JSON de multi-hit habilitado nos efeitos nem um endpoint de distribuição nesta entrega.

`ICalculationEngine.Distribute` recebe uma quantidade já calculada, uma política e destinatários identificados. Retorna parcelas imutáveis e um trace determinístico. Não conhece dano, cura, stacks, recursos, cartas ou alvos reais. Os destinatários são IDs opacos; o executor decidirá a que impactos eles correspondem.

## Fluxo numérico

1. A pipeline calcula a base e os estágios da origem, com `captureOnly`.
2. A camada numérica divide a quantity capturada. Cada parcela mantém os receipts da origem.
3. Cada aplicação recebe sua própria parcela e executa somente os estágios restantes da pipeline, usando o contexto atual do alvo.
4. Settlements continuam derivados do cálculo daquela aplicação. Distribuição não gasta defesa ou qualquer recurso.

Uma base 10 com bônus flat 2 gera um orçamento 12. Em três parcelas iguais, cada uma recebe 4, e não `10/3 + 2`. Reaplicar o estágio da origem sobre uma parcela falha pelo contrato existente de receipts.

Um teste conecta a distribuição à pipeline e ao processador comum: orçamento 12, três parcelas de 4, defesa inicial 5. A defesa consome 4, depois 1, depois 0; os pedidos finais de redução são 0, 3 e 4. Essa composição manual no teste verifica os contratos. Não significa que o executor já faça esse planejamento automaticamente.

## Políticas

| Campo | Contrato |
| --- | --- |
| `mode` | `Continuous` ou `Quantized`. |
| `quantum` | Obrigatório, positivo e finito em Quantized. Não aceito em Continuous. |
| `remainderAllocation` | `Earliest` ou `Latest`: define quem recebe uma unidade extra. |
| `allowTargetScopedInput` | Opt-in para dividir quantidades que já incluem stages de alvo. Não remove receipts. |

Destinatários declaram `recipientId` e `order`. A ordem é `order` crescente, com desempate por ID ordinal. A posição no array/dicionário recebido não decide o resto.

Exemplo de política para o pedido **interno da camada numérica**, não para colar em uma definição de carta:

```json
{
  "mode": "Quantized",
  "quantum": 1,
  "remainderAllocation": "Earliest",
  "allowTargetScopedInput": false
}
```

### Quantized

Divide uma quantidade em unidades do quantum declarado. 10 com quantum 1, em três destinatários, resulta em 4/3/3 com Earliest ou 3/3/4 com Latest. Dois stacks em três destinatários podem resultar em 1/1/0: a matemática não inventa um stack em cada impacto.

Também aceita unidades fracionárias e valores assinados: 2,5 com quantum 0,25 resulta em 1/0,75/0,75. A unidade continua definida pelo consumidor, sem lista hardcoded de recursos.

O orçamento precisa ser um múltiplo exato do quantum na representação numérica usada. Não há arredondamento implícito do orçamento. Se o design exige converter uma base fracionária para uma contagem inteira, essa conversão deve acontecer antes, na pipeline, com política explícita.

Nem todo decimal é exato em Float32. `quantum: 0.1` não promete que um orçamento decimal 1 possa ser repartido em dez parcelas que somem exatamente 1. Também se rejeita uma parcela que não possa ser representada exatamente em Float32. O resultado é falha, não perda silenciosa de conservação.

### Continuous

Distribui na malha binária exata do Float32 de entrada. Não calcula e arredonda `total / count` separadamente para cada destinatário: divide as unidades representáveis do orçamento e distribui o resto na ordem declarada.

As parcelas diferem no máximo por uma unidade dessa malha; a soma em Double dos valores Float32 é exatamente igual ao orçamento Float32 original. Isso vale para valores normais, negativos, subnormais, zero e os extremos finitos. O trace registra `allocationQuantum` para tornar essa precisão visível. Não é uma promessa de aritmética decimal ou de precisão infinita.

## Proveniência e validação

O pedido identifica distribuição, revisão e unidade. A input quantity precisa ter valor finito, revisão/unidade correspondentes, fingerprint e receipts válidos, únicos e limitados. Receipts Shared não carregam contexto de ator; Actor/Target exigem seu ID de contexto.

Por padrão, um orçamento da origem não pode conter stages de alvo. O opt-in permite outro desenho numérico, mas preserva toda a proveniência. Um consumidor ainda não pode aplicar de novo o mesmo stage para o mesmo contexto; dividir não “limpa” scaling anterior.

Cada parcela recebe um fingerprint derivado do trace de distribuição, da quantidade original e de sua alocação. Mudar política, origem ou identidade da distribuição muda a proveniência, mesmo quando os valores resultantes coincidem. Serialização preserva pedido, parcelas, receipts e hashes. Coleções autorais são copiadas defensivamente.

O trace inclui input, política, quantum efetivo, unidades totais, unidades base por destinatário, resto, destinatários ordenados, parcelas, total alocado e `conservationRemainder`. Uma distribuição aceita tem remainder zero, verificado por igualdade numérica exata, sem tolerância epsilon.

Limites técnicos: 4.096 destinatários e 16.777.216 unidades no modo Quantized. O último é o domínio inteiro exato de Float32, não um limite de dano. Continuous usa a mantissa limitada da entrada. IDs vazios/duplicados, políticas desconhecidas, quantidades incompatíveis e excesso falham sem truncamento ou resultado parcial.

## O que falta para concluir a etapa 8

- Definir e publicar a política de multi-hit nos efeitos/componentes, distinguindo repeat tradicional de orçamento distribuído.
- Planejar slots de impacto, dividir a magnitude após a origem e reavaliar os stages/settlements restantes por alvo.
- Distribuir stacks residuais separadamente e registrar zero stacks como contribuição ausente, sem tentar aplicar um status inválido.
- Configurar e executar escopos de chance, críticos e triggers por ação/impacto, sem reinterpretar boundaries reservados como implementados.
- Aplicar perda de alvo, stop/retarget, ausência de próximo alvo e orçamentos de execução.
- Comprovar a combinação com condensação OncePerAction, rollback, preview, persistência, replay e branches no gateway completo.
- Fornecer conteúdo JSON e oportunidades que usem esses contratos. Repeats atuais permanecem iguais até a política estar ligada ao executor.

Nesta entrega, nenhum setting, efeito, fluxo de combate, save ou UI foi alterado. A engine version permanece 15 porque a operação nova ainda não muda o estado ou os hashes do gameplay existente. A versão deve ser revisada quando a integração de execução for habilitada.
