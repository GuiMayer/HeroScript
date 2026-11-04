# Escalonamento temático do MVP

O bucket `AddPercent` soma as contribuições sem agrupamento sobre sua entrada. Para ativar o outro modelo, declare `grouping` no bucket. Em `Add`, o mesmo agrupamento produz uma quantidade absoluta.

- `influenceGroups` mapeia IDs de contribuições a chaves de grupo. Contribuições sem mapeamento mantêm seu valor.
- `groups` define fórmula, parâmetros e peso de cada chave. A fórmula recebe `group.raw` e `group.parameters.<nome>`.
- `subjectKeys` define quais chaves da carta são relevantes. `weightBySubjectCount` define o peso conforme a quantidade de chaves, começando em uma. Sem essas opções, cada grupo usa seu peso fixo.
- `subjectWeightOverrides` permite definir pesos por ID de carta. Com uma tabela por contagem, a soma dos pesos efetivos não pode superar o orçamento daquela contagem.
- `parameterInfluences` permite mapear contribuições a parâmetros de fórmula. A Maestria de Fogo altera `cap` dessa forma.

O MVP usa a curva `I / (1 + I / c)` nos perfis direto, crítico e residual. A curva roda no estágio de origem. A configuração de Maestria atual está nos perfis direto e crítico. Remover `grouping` também requer remover ou redirecionar as contribuições que existem apenas para ajustar parâmetros.

## Matriz analítica inicial

Cada coluna divide o mesmo investimento bruto igualmente entre as tags presentes, usa `c = 1` e pesos por tag de 1,00, 0,80 e 0,65. Os números abaixo são fatores de `increased` antes de arredondamento e mitigação.

| Investimento total | Mono | Duas tags | Três tags |
| ---: | ---: | ---: | ---: |
| 40% | ×1,2857 | ×1,2667 | ×1,2294 |
| 100% | ×1,5000 | ×1,5333 | ×1,4875 |
| 200% | ×1,6667 | ×1,8000 | ×1,7800 |

Para reproduzir uma célula com `n` tags e investimento total `T`, calcule `1 + n × peso(n) × g(T/n)`. Com esses valores, duas tags ultrapassam uma após aproximadamente 66,7% de investimento. Três ultrapassam duas após aproximadamente 257,1%. Multiplique o resultado por 1,25 para Foco de Fogo em cartas elegíveis ou por 1,20 para Fusão de Vazio em cartas elegíveis. Para Maestria, use `c = 1,5` no grupo de Fogo.

Essa matriz isola o cálculo. A disponibilidade de cartas e inscrições, os custos, os efeitos situacionais e os encontros exigem uma simulação de campanha separada.

## Simulação do capítulo

O teste `PublishedChapterCompletesWithBossRetryDurableDialogueAndNoCheats` passou na configuração local do MVP com a seed `8814`. O agente escolheu ações legais pelo resultado previsto de dano, bloqueio, cura e status. A execução completou cinco encontros, incluindo derrota e revanche no chefe, e encerrou na sequência 170 com nove Parts.

A reconstrução depois de reiniciar a API e a segunda execução em um armazenamento independente passaram. A segunda execução conferiu 169 hashes de comandos. O hash final observado foi `0c6e64c64daa30ef8d7a159f3d2a0e003cc2cd122ce1d295c1a7bce3a5ced140`.

Para repetir, execute `dotnet test tests/API.Tests --filter FullyQualifiedName~PublishedChapterCompletesWithBossRetryDurableDialogueAndNoCheats --logger "console;verbosity=detailed"`. A certificação corresponde ao conteúdo local carregado no teste. O cenário valida o fluxo do capítulo e o determinismo. Ele não mede taxa de vitória por build nem substitui a matriz analítica ou um playtest.
