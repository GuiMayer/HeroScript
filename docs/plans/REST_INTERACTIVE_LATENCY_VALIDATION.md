# Certificação da latência REST — 2026-10-15

Plano: [correção da latência](REST_INTERACTIVE_LATENCY_CORRECTION_PLAN.md).
Implementação: [registro por etapa](REST_INTERACTIVE_LATENCY_IMPLEMENTATION_PROGRESS.md).

**Atualização posterior:** este arquivo preserva a medição histórica da etapa 10.
As otimizações seguintes e a certificação por socket estão no
[relatório dos gargalos restantes](REST_LATENCY_REMAINING_BOTTLENECKS.md).
A etapa 9 desse relatório passou 526 interações de três runs abaixo de 1 s
(máximo 802 ms), incluindo criação a frio; não é certificação de latência clique-a-tela na Godot
nem garantia para conteúdo e hardware arbitrários.

## Escopo e interpretação

As otimizações mantêm REST, os comandos canônicos, os fatos de apresentação,
o append durável e a versão determinística **31**. Melhorar o transporte não
torna a simulação gratuita. Os números abaixo **não certificam a meta de todas
as ações abaixo de um segundo**.

Ambiente: Windows, Ryzen 5 5600G (6 cores/12 threads), SDK .NET 10.0.301,
runtime .NET 10.0.9, Godot 4.7.2. O jogo do usuário permaneceu aberto; não houve
reinício de sua API ou alteração de seus saves para executar os testes.

O teste do ato usa TestServer em Release, HTTP/JSON, conteúdo publicado e
`FileRunCommitStore` em diretório temporário real, com flush e rename atômico.
Não é store em memória, mas tampouco inclui socket da API, parsing/publicação na
Godot, frame pacing ou duração artística. Portanto não é latência clique-a-tela.
Os percentis desta jornada representam uma seed; não equivalem à matriz completa
de várias seeds, mods, permissões e plataformas prevista no plano.

## Jornada real do mundo 1

Seed 154, `ascendant_world_one`, política determinística preview-greedy já usada
pelo teste de regressão. Ambos os caminhos concluem 9 encounters, 172 comandos
e vida final 50. Sessão compacta: 173 leituras. Verificação semântica e nova
instância de host sobre o mesmo store recompõem o estado final exato.

| Medida | Etapas 1–9 | Medição final da etapa 10 |
| --- | ---: | ---: |
| Comandos, p95 | 1.846,97 ms | 1.051,95 ms |
| Comandos, máximo | 2.854,03 ms | 1.639,48 ms |
| Sessão compacta, p50 | 73,96 ms | 47,98 ms |
| Sessão compacta, p95 | 796,16 ms | 463,78 ms |
| Sessão compacta, máximo | 1.051,98 ms | 669,68 ms |
| Sessão compacta, bytes p95 | 174.855 | 174.855 |

Na primeira medição com reuso de bytes, os p95 foram 1.429,47 ms para comandos
e 675,47 ms para sessões (reduções observadas de 23%/15%). A medição final, sem
a suíte geral concorrendo por CPU, chegou aos números da tabela. A diferença
entre repetições reforça o efeito da carga da máquina: não atribuir toda a
variação ao código. Não é promessa universal nem comparação controlada contra
o projeto anterior à etapa 1. As amostras antigas da auditoria permanecem no
registro de implementação como históricas.

| Ação na medição final | Amostras | p50 | p95 | Máximo |
| --- | ---: | ---: | ---: | ---: |
| Jogar carta | 73 | 298,44 ms | 758,80 ms | 922,45 ms |
| Encerrar turno | 20 | 983,59 ms | 1.550,35 ms | 1.639,48 ms |
| Iniciar encounter | 9 | 355,77 ms | 978,14 ms | 978,14 ms |
| Avançar nó | 23 | 69,87 ms | 99,91 ms | 129,16 ms |
| Resolver combate | 9 | 73,70 ms | 98,31 ms | 98,31 ms |

Essas quantidades não cumprem o mínimo de 100 amostras por tipo/múltiplas seeds
para certificar todos os orçamentos. O p95 usa nearest-rank e inclui startup;
o máximo permanece exposto mesmo em grupos pequenos.

As ações mais lentas ainda são `END_TURN`: no caso final de 1.639 ms, preparação
do commit levou aproximadamente 36 ms e append 6,5 ms; hashing, 1.422 ms, contendo
codificação e trabalho dentro da descoberta. A sessão mais lenta levou 670 ms,
com 655 ms na descoberta canônica. Na medição anterior, um `PLAY_CARD` chegou a
1.412 ms; no ensaio final, seu máximo foi 922 ms.
Não foi removido nenhum efeito, alvo, trace, candidato ou confirmação durável
para melhorar os números.

Os spans são inclusivos: `canonical_encode` ocorre dentro de hashing; descoberta
pode conter hashing; `command_execute` contém o comando inteiro. Não somar os
tempos. A composição de fingerprints também mede a codificação de cada parte.

## Correções finais

- Readiness faz um probe de I/O no diretório do store, sem inventariar partidas
  nem validar todo o histórico. Contagem ausente é `null`, não zero fictício.
- Fingerprints de triggers, lifecycle de resources e inicialização compõem os
  mesmos bytes JSON antes embutidos, **não hashes de hashes**. Reuso somente por
  identidade/tipo de agregados declarados profundamente imutáveis, com serializer
  padrão, no escopo da operação. Cache FIFO limitado a 64 entradas e 8 MiB;
  evicção ou valor maior que o orçamento volta ao cálculo exato.
- Tokens numéricos são escritos sem alocar uma string por número, preservando
  sua representação, inclusive expoentes, negativos e precisão.
- A composição mantém o limite original de profundidade JSON: o teste de
  fronteira aceita a profundidade permitida e rejeita a que o serializer anterior
  também rejeitava. O cache armazena profundidade junto dos bytes, sem nova travessia.
- OpenAPI publicado foi atualizado aditivamente, preservando as restrições
  editoriais. O export do host é diagnóstico separado, não substituto cego.
- O gerador de OpenAPI usa Microsoft.OpenApi 2.7.5, compatível com o Swashbuckle
  instalado e com correção de segurança. O mismatch de major anterior causava
  erro ao gerar o documento. [Advisory oficial](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc).
- O teste de determinismo entre processos reconhece o layout de saída isolada,
  evitando selecionar um executável antigo de outra compilação.
- O launcher exige `compact-interaction-sessions-v1`, sem aumentar a versão das
  regras só para negociar o contrato REST. Não reutiliza um host anterior sem
  `/session`. Nenhum processo do jogador foi reiniciado durante esta validação.

## Transporte na Godot

Probe sintético: 100 GETs de 524.302 bytes por FPS, servidor TCP local no próprio
teste, conteúdo JSON conferido integralmente. Usa o transporte de produção, mas
não executa regras nem persistência. Não comparar esse número diretamente com
o benchmark TestServer como se ambos fossem o mesmo trecho do pipeline.

| FPS | Chunk padrão anterior, p50/p95 | Chunk de 1 MiB, p50/p95 |
| --- | ---: | ---: |
| 30 | 229,60 / 265,37 ms | 108,28 / 249,75 ms |
| 60 | 211,47 / 239,50 ms | 105,65 / 201,23 ms |
| 144 | 205,08 / 235,56 ms | 78,06 / 107,75 ms |

Máximos com 1 MiB: 403,16 / 559,15 / 141,33 ms, respectivamente. Há contenção
com a validação geral e compilação paralelas; por isso não usar a diferença
como um ganho isolado garantido. A redução da mediana sustenta usar leituras
maiores no worker nativo. São até quatro workers, sem aumentar esse número ou
cortar respostas. Chunk é buffer de transferência, não limite do conteúdo.
O comportamento foi conferido no [código nativo da Godot](https://github.com/godotengine/godot/blob/4.7/scene/main/http_request.cpp):
leitura do corpo por chunks e controle independente de `body_size_limit`.

Na repetição final da suíte Godot, os p95 foram 130,72 / 110,63 / 108,68 ms.
Máximos: **1.472,55** / 123,59 / 119,54 ms. O outlier não foi excluído: nem mesmo
o probe de transporte certifica máximo universal abaixo de um segundo sob carga.

## Testes e reprodução

Core: **1.738 testes passaram**, incluindo igualdade de bytes/hashes, cache
frio/quente/evicto, profundidade JSON, tokens numéricos, readiness e cancelamento. Descoberta possui
comparação diferencial em duas seeds, com IDs, sucessores e hashes completos.

Godot: a execução final completa passou **45 suites e 1.042 checks**; teste
adicional de progressão por setting passou 30 checks. O probe de transferência
sintética acrescenta 300 leituras de 512 KiB, divididas entre 30/60/144 FPS,
validando o corpo recebido integralmente, sem API externa ou mutações de jogo.
Dois warnings são emitidos pelos fixtures negativos de watchdog e composição
inválida; não houve erro funcional ou falha de script na execução final.

A primeira execução geral da API foi abortada pelo limite histórico de 300 s,
com 215 testes aprovados e sem falha observada até o aborto. Isso **não** foi
tratado como aprovação da suíte completa. A execução ampliada terminou em 13 min
4 s: **221 passaram e 1 falhou** por não localizar o probe entre processos na
saída isolada. Depois da correção e construção da dependência, a repetição focada
passou inicialmente 10 testes; na build final, passou **14 testes**, incluindo
esse probe (3 processos independentes), contratos, sessão, descoberta diferencial,
diagnóstico e cache frio/quente. O resultado geral original não foi reclassificado como verde: é uma
execução com falha de infraestrutura, seguida de repetição corrigida aprovada.

```powershell
dotnet test tests/Core.Tests
dotnet build tests/Determinism.Probe -c Release --artifacts-path .runtime/rest-cross-process-validation
dotnet test tests/API.Tests -c Release --no-build --artifacts-path .runtime/rest-cross-process-validation -- RunConfiguration.TestSessionTimeout=900000
dotnet test tests/API.Tests -c Release --no-build --artifacts-path .runtime/rest-cross-process-validation --filter 'FullyQualifiedName~CompactSession_CanTraverseTheSamePublishedActAndReplay' --logger 'console;verbosity=detailed' -- RunConfiguration.TestSessionTimeout=900000
./tools/test-godot-ui.ps1
```

Não executar o benchmark junto da suíte geral para interpretar seus tempos como
referência isolada. Os testes usam stores e preferências isolados do jogador.

## Metas ainda não certificadas na medição histórica da etapa 10

O p95 de sessão inédita ainda excede 150 ms; os de carta e fim de turno não
estão certificados nos orçamentos de 250/500 ms. O máximo do comando continua
acima de 1 s, logo o objetivo ponta a ponta também permanece aberto.

O próximo foco é reduzir trabalho repetido dentro do hashing de novos snapshots
e da descoberta exata de resoluções distintas. Qualquer ampliação do reuso deve
continuar diferenciando revisão, instância, alvo, custos, permissões e efeitos
possivelmente falíveis. Não usar legalidade aproximada, respostas otimistas,
truncamento de mecânicas ou redução da durabilidade como atalho para cumprir SLO.

Também ficam abertas a medição clique-a-tela com a API atualizada, amostras
adicionais por tipo de comando/seed e o restante da matriz de conteúdo/configurações. A jornada
e o probe sintético não substituem essas medições.
