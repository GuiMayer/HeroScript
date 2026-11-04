# Autoria dos grupos de poder da build

## Contrato

Fonte, operação matemática e grupo de apresentação são dimensões independentes. Os grupos abaixo são convenções de autoria deste jogo; não são enums de execução nem ramos especiais de CalculationEngine. Pipeline, bindings, fórmulas e componentes revisionados determinam o cálculo.

| Origem | Contribuição possível | Operação configurada | Momento/contexto |
| --- | --- | --- | --- |
| Carta e upgrade de base | Base alterada e BaseTrace | Entrada do cálculo | Instância/revisão da carta |
| Atributo/inscrição | Flat ou increased | Add / AddPercent | Ator ou alvo definido no binding |
| Modificador de carta | Base, influência ou execução | Componente/patch correspondente | Instância/ledger; não altera atributo do personagem |
| Estado do ator | Fator condicional ou aumento compartilhado | Multiply ou AddPercent | Snapshot definido para o impacto/captura |
| Estado do alvo | Fator condicional ou mitigação | Operação explícita | Alvo daquele impacto |
| Companion | Atributo, status ou efeito publicado | Mesmos contratos de qualquer origem | Trigger/condição/cooldown da definição |
| Armor | Quantidade restante | Formula | Alvo; política do setting |
| Escudo consumível | Capacidade e settlement | ConsumeCapacity | Alvo; aplicação transacional |
| Crítico | Capturas, tiers e fator | Pipelines/fórmulas existentes | Impact, salvo outra política explícita |
| Condensação | Agregação de stacks e um proc | Recipe + quantidades | Consumo único e receipts preservados |

## Grupos de design

- **Escalamento do personagem:** atributos e inscrições; não reescrevem a carta. Percentuais que devem somar usam o mesmo bucket.
- **Transformações da carta:** mutações, componentes e execução estrutural. Multihit, troca de alvo e consumo de stacks não são automaticamente multiplicadores.
- **Condições do combate:** alterações contextuais que podem contribuir para buckets compartilhados ou independentes. Uma condição não ganha multiplicação apenas por ser um status.
- **Defesa e aplicação:** apresentar separadamente os modificadores do alvo, consumo de capacidade e alteração efetiva do recurso.

## Convenção numérica

`Add` recebe valores na unidade do canal. `AddPercent` recebe frações: 0,20 representa +20%. Contribuições do mesmo bucket incidem sobre a entrada fixa daquele bucket. `Multiply` recebe fatores: 1,20 representa ×1,20. O produto vazio e buckets Add/AddPercent sem contribuições preservam a entrada. Fórmulas podem ter comportamento próprio mesmo sem contribuições; documentar e testar.

Base 10 + flat 5, com increased 0,20 e 0,30 no mesmo bucket, resulta em 22,5 antes da conversão. Dois aumentos de 20% e 25% no mesmo bucket dão ×1,45; em buckets consecutivos dão ×1,50. O `prototype-test` atual usa Calor em bucket separado: manter esse payoff separado é uma escolha explícita de conteúdo.

Não usar 20 onde o contrato requer 0,20, nem 0,20 onde requer fator 1,20. Limites e negativos são políticas do canal: uma redução percentual pode ser legítima, mas efeitos não negativos precisam de conversão/limites explícitos. Não aplicar clamps universais no redutor. Soma de bônus traz retorno relativo decrescente, mas não diminui o ganho absoluto; diminishing returns de armor exige fórmula própria.

## Ordem e seleção

Pipeline explícita do efeito tem precedência. Sem ela, vale o default do modo para o canal. Sem ambos, deve existir exatamente uma pipeline habilitada compatível. O ID explícito deve estar habilitado e usar o canal correto. Não selecionar pipeline por nome de personagem, carta, recurso ou fonte.

Ordem dos buckets, arredondamento, limites e conflito de Set são dados autoritativos. Separar percentuais ou mover arredondamento pode mudar o resultado; publicar uma nova revisão nesses casos. A DSL é sequencial e expressões matemáticas convencionais precisam ser traduzidas e testadas, sem presumir precedência de operadores.

## Transformações, captura e imutabilidade

Upgrades de base entram uma vez. Influências de inscrições não devem ser aplicadas novamente se já estiverem incorporadas no atributo usado pelo binding. Não remover receipts para reaplicar escalamento. Condensação agrega quantidades e emite um proc; os efeitos desse proc podem reduzir ou aumentar qualquer recurso configurado, não somente health.

Regra de split/repeat, alvos, momento de captura e crítico por impacto pertencem à definição/execução. A pipeline recebe números resolvidos, produz quantidade/trace/fingerprint e não consome diretamente recursos. Settlement e mutação são responsabilidade dos serviços transacionais.

Snapshots antigos continuam vinculados ao conteúdo publicado de sua revisão. Previews não alteram RNG, inventário ou estado. Uma nova revisão com regras diferentes pode produzir hashes diferentes; dentro da mesma revisão, os mesmos inputs autoritativos devem reproduzir o mesmo resultado.

## Critérios para publicar conteúdo

Antes de publicar: validar referências/unidades, exemplos numéricos, situações sem influência, limites, crítico, preservação de input e consumo único. Revisar se cada fator independente deve realmente multiplicar. Documentar a proveniência de cada contribuição e sua aplicabilidade por tags/canal. Relíquias podem continuar suportadas na engine, mesmo quando não são oferecidas neste setting.

As definições atuais permanecem válidas sem novos metadados obrigatórios. A exposição visual deve derivar de traces e metadados publicados; a Godot não recalcula os valores de gameplay.

### Exemplo reproduzível

O caso `shared-group` em `tests/Fixtures/calculation-baseline/grouping.json` fornece base 100 e duas contribuições, 0,20 e 0,25, para o mesmo bucket. O caso `separate-groups` envia a segunda contribuição a um bucket posterior. Execute os dois casos com o benchmark do repositório.

```powershell
dotnet run --project tools/CalculationBenchmark -c Release -- tests/Fixtures/calculation-baseline/grouping.json
```
