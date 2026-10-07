# Validação do core volátil

Data: 2026-10-06. Plano: [implementação do core](CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md).
Engine version: **20**. Ambiente: Windows, .NET SDK 10.0.301 / runtime 10.0.9,
Godot 4.7.2. Execuções locais; medições não são um SLA para qualquer máquina.

## O que foi entregue

O setting `volatile-core` define a jornada, cards, afinidades, transformações,
status, payloads, receitas, atributos e pipelines em JSON. O modo normal usa
`core_volatile`; `core_volatile_sandbox` habilita branches e ferramentas por
policies, sem outro processador de regras. A Godot usa os contratos REST para
inputs e apresenta snapshots, previews e procs já calculados.

A jornada tem nove stops e permite quatro transformações na mesma instância.
O jogador pode preparar stacks com potência capturada, condensar para dano,
cura ou modifier, dividir uma magnitude em impactos e transportar excedente
por uma derrota causal. Atributos persistentes entram na origem da pipeline;
melhorias de carta, base do personagem e buffs continuam autoridades distintas.

## Correções encontradas no gate final

- Entradas aleatórias de atividades sintéticas preservam draws no contexto da
  run, inclusive quando o reducer altera recursos. Não há um RNG descartável.
- Ativação de conteúdo revalida e rebinds as regras dos atributos, conservando
  os valores e rejeitando novos limites incompatíveis sem mutação parcial.
- Publicação valida referências de patches de continuação mesmo quando ainda
  não estão aplicados a uma carta. O container compilado valida seu perfil final.
- Preparação e boundaries transportam steps, cálculos e aplicações pelo plano
  imutável até os frames/fatos do commit. Persistência e replay usam o mesmo
  journal, sem diagnóstico paralelo ou autoridade na interface.
- A apresentação lê os nomes reais do schema e normaliza Remove sem enviar
  campos exclusivos de Apply/Replace. Slots de arte são independentes por ID.

## Evidências automatizadas

- Core: **1.524 testes aprovados**, nenhuma falha ou skip.
- API: **186 testes aprovados**, nenhuma falha ou skip, executados em dois
  grupos: 185 regressões (1m16s) e uma jornada de dez repetições (4m29s).
- `VolatileCoreJourneyTests`: hosts/stores novos, seed 922708 e identidades de
  comandos fixas; compara recibos integrais e hashes dos commits completos,
  incluindo RNG, cálculos, steps, aplicações, frames e fatos. As dez execuções
  tiveram 47 comandos e hash final
  `229f2c00a8ac6136a8b377492dc9c5c65d0da02b2036c9dd44d00ace225ea476`. Exige quatro
  transformações na mesma instância, condensação e salto executados.
- Reinício após transformação e após salto na jornada; sandbox reinicia em meio
  ao acúmulo e após condensar, verifica parent inalterado e replay de cada branch.
- `PersistentPlayerAttributeTests` verifica traces duráveis da preparação,
  retry, reinício, dez replays semânticos e isolamento de fork.
- `ContentRevisionActivationTests`, `ContentReloadServiceTests` e testes de
  transformação cobrem revisão explícita, compatibilidade, histórico e rejeição
  de candidatos inválidos. Não foi anunciado hot reload irrestrito durante combate.
- `CardProcPreviewTests` verifica agrupamento, consumo agregado, saltos e
  projeções sem ampliar permissões de zonas ocultas. GETs conservam o hash.
- Godot: layers, volatile_core, volatile_core_online, resolutions, smoke,
  UI smoke, gameplay polish e diálogo. Layout automatizado cobre PT/EN, escalas 100/120%,
  mão vazia e resoluções até 3840×2160, incluindo 2560×1080.

Os recibos REST resumidos não duplicam todos os frames do journal. O teste lê o
store autoritativo para comparar commits completos; a consulta pública a detalhes
continua protegida pela policy de ferramentas do modo.

## Desempenho

Executor puro, build Debug, 30 amostras por cenário, sob carga concorrente de
testes. O benchmark exclui compilação do setting, rede, persistência e interface.
Cada release consome todos os stacks em um proc; seleção causal respeita o limite
de três hops mesmo com mais candidatos.

| Cenário | p95 | p99 / máximo |
| --- | ---: | ---: |
| Condensação, 2 stacks | 9,91 ms | 13,78 ms |
| Condensação, 48 stacks | 24,81 ms | 42,06 ms |
| Condensação, 98 stacks | 55,18 ms | 65,05 ms |
| Continuação, 2 alvos | 284,67 ms | 308,54 ms |
| Continuação, 20 alvos | 418,89 ms | 722,17 ms |
| Continuação, 100 alvos | 502,29 ms | 566,47 ms |

Godot → REST por TCP local, API Release, campanha showcase: 34 comandos,
mediana **348 ms**, p95 **972 ms**, máximo **1.418 ms**. Essa execução ocorreu
durante testes concorrentes. O smoke passou seu limite de p95 e registrou o pico;
não permite afirmar que toda resposta fica abaixo de um segundo.

Jornada core, HTTP in-process Debug: 660 comandos/evaluations, p95 **310,77 ms**,
p99 **528,21 ms**, máximo **1.301,98 ms**. A fronteira inclui aplicação,
serialização e store real, mas não TCP nem renderização.

| Fase / consulta | Amostras | p95 | p99 | Máximo |
| --- | ---: | ---: | ---: | ---: |
| Comandos 1–15 | 150 | 113,43 ms | 263,65 ms | 705,11 ms |
| Comandos 16–30 | 150 | 266,76 ms | 344,42 ms | 527,41 ms |
| Comandos 31–47 | 170 | 463,70 ms | 593,82 ms | 1.301,98 ms |
| Evaluations | 190 | 227,16 ms | 442,77 ms | 581,48 ms |

As fases têm ações diferentes; não isolam apenas o custo do tamanho do histórico.
O p95 final é maior que o inicial, mas permaneceu abaixo do orçamento medido.
Crescimento de estado/alvos ainda tem custo;
agregação de stacks evita um proc por stack, não elimina hashing e serialização.

Não houve corte de efeitos, retirada de traces, mudança de balanceamento para
passar no benchmark nem cache autoritativo. Caches derivados existentes continuam
vinculados a revisão/estado e não substituem o cálculo ou o journal.

## Limites da entrega

Implementação e validação técnica não comprovam diversão, balanceamento ou
qualidade visual final. O playtest humano do gate 14 continua necessário:
entendimento de base/afinidade/comportamento, escolhas entre preparar/consumir,
ritmo de combate e equilíbrio de custos/recompensas. Não foi inventada aprovação.

O teste offline `volatile_core.gd` ainda emite um aviso de duas instâncias de
áudio no encerramento headless; não é falha de regra nem de integração, e não
foi tratado como teste visual humano. A validação online do core encerrou sem
esse aviso após desligar o áudio explicitamente.

Saves anteriores foram preservados, não convertidos silenciosamente. Para
reproduzi-los, mantenha engine e conteúdo compatíveis. A referência de conceitos
legados permaneceu intocada. Alterações de multi-setting/ascendant e UI que já
estavam no checkout foram preservadas e excluídas destes commits quando alheias
ao core. Nenhum push faz parte desta solicitação.
