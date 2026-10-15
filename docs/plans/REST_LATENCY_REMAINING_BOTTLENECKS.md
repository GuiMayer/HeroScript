# Otimização dos gargalos restantes — 2026-10-15

Continuação da [validação REST](REST_INTERACTIVE_LATENCY_VALIDATION.md).
Preservar regras, resultados, RNG, hashes, imutabilidade e persistência durável.
Cada etapa será validada e commitada separadamente.

## Sequência

1. **Compartilhamento estrutural no encoding canônico.** Novos snapshots não
   devem serializar novamente cada regra, mapa e encounter anterior quando o
   objeto imutável correspondente continua sendo o mesmo. Usar os metadados
   reais do serializer, manter fallback conservador, limite de profundidade,
   tipos declarados, conversores e ordem canônica. Reuso operacional limitado.
2. **Remover trabalho derivado redundante após o novo profiling.** Investigar
   os trechos que continuarem dominantes, sem legalidade aproximada ou cortar
   fatos. Fragmentos gerados internamente podem evitar validações repetidas se
   testes comprovarem a mesma aceitação/rejeição e bytes.
3. **Certificação ampliada.** Repetir o ato completo e comparar o hash de run e
   combate com o serializer de referência após cada comando. Medir por tipo,
   reportar máximos e testar múltiplas seeds. Validar os testes Core, API,
   determinismo entre processos, recuperação e cliente Godot relevantes.

A referência é o conteúdo publicado, nesta máquina local. Não é possível
garantir tempo absoluto para qualquer hardware, carga do SO ou mods sem limite;
outliers não serão ocultados para declarar a meta cumprida.

## Etapa 1 — implementação

Encoding por propriedades aplicado somente a `RunState`, `CombatState` e
`RunEncounterState`, com metadados padrão do System.Text.Json. Folhas usam o
serializer existente, preservando seus conversores/polimorfismo. Formatos não
auditados, extension data, callbacks e conversores por propriedade seguem o
caminho de referência. O array de encounters mantém ordem e compõe seus elementos
imutáveis, incluindo todo o histórico no hash.

Um LRU operacional de até 128 entradas/8 MiB é compartilhado por escopos
aninhados e descartado ao terminar a operação. Identidade + tipo declarado são
as chaves; rótulo de revisão, sequência e igualdade parcial não são suficientes.
O cache não guarda resultados mutáveis nem altera os snapshots.

Fronteiras, fases e fingerprint da descoberta também compõem os mesmos campos
por esse caminho, em vez de embutir novamente o agregado num objeto anônimo.
Fragmentos são criados somente pela fábrica interna que serializa/valida os
valores; a composição não repete parsing de JSON já gerado. O limite original
de profundidade é checado explicitamente antes de cada composição.

Testes adicionados: bytes exatos em 180 revisões com partes compartilhadas,
escopos aninhados e evicção; profundidade inválida e tipo declarado. O teste do
ato compara também SHA-256 do serializer de referência após cada comando, fora
da região cronometrada.

Validação: 1.740 testes Core passaram. O ato adicional com comparação independente
após cada comando também passou. O benchmark sem trabalho de referência
concluiu os 9 encontros/172 comandos, replay e recuperação, com vida final 50.
Sessão p95 238 ms/máximo 537 ms; comandos p95 agregado 936 ms, mas máximo 1.899 ms.
Fim de turno p95 1.306 ms. Portanto esta etapa não fecha a meta de máximo 1 s.

Uma variante intermediária com parsing redundante dos fragmentos foi rejeitada
por regressão de desempenho. Testes de referência e medições de latência foram
separados: o primeiro valida cada hash; o segundo não adiciona alocações de
certificação entre as ações medidas.

## Etapa 2 — definições tipadas imutáveis

`ContentRuntime.GetDefinition<T>` desserializa a definição em toda consulta.
Isso também impede reaproveitar o hash da mesma definição entre cálculos.
Adicionar cache tipado limitado apenas para classes declaradas profundamente
imutáveis; consumidores de tipos mutáveis continuam recebendo novas instâncias.
Chave deve incluir instância do runtime, tipo, kind e ID; hot reload não pode
reusar definições de outra revisão/configuração. Integrar com o coordenador de
caches e testar concorrência, evicção, invalidação e igualdade de bytes/hashes.

Implementação: LRU com 256 entradas, single-flight por 32 stripes, chave com
identidade do runtime/tipo/kind/ID. Invalidar é atômico em relação à publicação
de valores produzidos em voo. Registrado como `TypedRevisionedDefinitions`,
camada Derived, no coordenador existente. Capacidade zero desativa o reuso.

Tipos auditados passaram a optar pela memoização de hash por identidade:
pipeline, política global de efeitos, política de ordem de turno, carta compilada
e instância de carta. São records com coleções imutáveis/normalizadas; nenhuma
regra ou input de cálculo foi removido da chave. Definições com getters/setters
mutáveis não são compartilhadas.

Testes focados: 21 aprovações, incluindo tipos mutáveis, leitura concorrente,
runtime diferente com o mesmo rótulo, cache desativado, evicção, invalidação e
hash de cópia alterada. Benchmark do ato: comandos p95 698 ms/máximo 1.738 ms;
sessões p95 165 ms/máximo 291 ms. Quatro comandos ainda excederam 1 s; o máximo
não foi ocultado. Próximo foco: alocações/encoding nas folhas e trabalho de efeitos.

Validação adicional: 1.744 testes Core passaram, seguidos dos cinco testes do
cache (incluindo invalidação com leitura em voo). Cinco testes REST passaram,
incluindo o ato com comparação de cada estado com o serializer de referência,
descoberta diferencial em duas seeds e recomputação após invalidar o cache tipado.

## Etapa 3 — reduzir alocações no encoding

Hashing passa a usar um escritor de JSON com buffers de campos reaproveitados,
uma decodificação de cada nome e cópia direta de strings ASCII já canônicas.
Unicode, escapes e caracteres bloqueados pelo encoder mantêm a normalização
original. Nomes duplicados mantêm a ordem relativa original. Buffers são limpos
ao devolver ao pool; o JSON e os snapshots não retêm referências a eles.

`CanonicalJson.Serialize` continua usando o escritor anterior, independente do
caminho otimizado do hash. Testes cobrem todos os caracteres ASCII, Unicode,
escapes, chaves duplicadas e léxico numérico. 1.746 testes Core passaram.
Quatro testes REST passaram, incluindo comparação após cada comando do ato,
replay/recuperação, descoberta diferencial e recomputação fria/quente.

Benchmark: cartas p95 727 ms/máximo 850 ms; fim de turno p95 1.024 ms/máximo
1.142 ms; sessão p95 148 ms/máximo 345 ms. Dois fins de turno ainda excederam
1 s. Não se considera a meta concluída por arredondamento ou só pelo p95.

## Experimentos posteriores rejeitados

Separar atores/components e registros individuais do histórico em fragmentos
não trouxe melhora consistente no ato publicado. Aumentar o número de entradas
e reconhecer a identidade do backing array de `ImmutableArray` também não
confirmou ganho. Esses caminhos foram removidos, mantendo os testes adicionais
de equivalência (atores, polimorfismo e histórico crescente).
Separar o estado de ativação em fragmentos também não confirmou ganho e foi
removido.

Um profiling temporário por propriedade confirmou que histórico e estado de
ativação/intenções são folhas relevantes do encoding. A instrumentação detalhada
foi retirada após a medição; não faz parte dos dados determinísticos nem das
respostas normais. As medições experimentais não substituem a certificação final.

## Etapa 4 — vida útil dos buffers de serialização

O caminho de hashing usa agora `SerializeToDocument` com descarte explícito do
documento temporário após escrever seus bytes canônicos. Não guarda elementos
ou spans do documento em cache. O escritor de referência (`Serialize`) permanece
independente, no caminho anterior. Nenhum buffer alugado escapa dessa vida útil.

O encoding final já era um buffer privado nunca reutilizado nem alterado após
publicação. O LRU passa a guardar esse mesmo buffer, evitando uma segunda cópia
de cada fragmento/snapshot. Mantidos 128 entradas/8 MiB de bytes úteis por
operação, sem ampliar o cache para esconder a regressão. O backing array pode
ter capacidade ociosa do `ArrayBufferWriter` (o limite não representa memória
total do processo). Testes cobrem retenção dos bytes após descarte,
reuso de buffers temporários, evicção, Unicode, polimorfismo, histórico e 600
revisões compartilhadas. 1.750 testes Core passaram.

Benchmark sem certificação entre comandos: 172 comandos/9 encontros, mesmo
resultado final (vida 50), replay e recuperação válidos. Cartas: p95 682 ms,
máximo 768 ms; fim de turno: p95 818 ms, máximo 862 ms; sessão: p95 158 ms,
máximo 509 ms. A primeira entrada em combate levou 1.179 ms: a meta geral ainda
não está concluída. Valores são medições TestServer nesta máquina, não latência
de ponta a ponta do Godot, nem garantia para conteúdo/hardware arbitrários.

Certificação adicional: cinco testes REST passaram, incluindo o ato inteiro
comparando cada hash de run/combate com o serializer anterior, replay/recuperação,
descoberta diferencial, sessão versionada e cache frio/quente/invalidação.
O teste entre três processos independentes também passou, comparando intents,
críticos, stacks, receipts, commits e replay.

## Etapa 5 — medir a interação completa por HTTP real

O teste do ato agora também tem variantes Kestrel em portas loopback dinâmicas,
com stores independentes e seeds 154/250/251. Além do POST e da sessão isolados,
reporta a soma de POST consumido + próxima sessão atômica recebida/parseada,
por tipo de comando e com contagem de interações acima de 1 s. Exclui a política
de escolha do teste, a certificação independente e as animações artísticas.
Não é uma medição de renderização/input do cliente Godot.

A seed 154 continua sendo o golden de 9 encontros e vida final 50. As outras
seeds podem terminar em vitória ou derrota legítima: o teste não altera regras,
RNG, dificuldade ou conteúdo para alcançar uma medição melhor. Toda variante
verifica replay semântico e recuperação num novo host.

As três variantes passaram (523 comandos, 526 sessões, 27 encontros). Vida final
154/250/251: 50/52/50. A medição mais abrangente ainda expõe outliers:

| Seed | Máximo POST | Máximo POST + sessão (fim de turno) | Interações ≥ 1 s |
| --- | ---: | ---: | ---: |
| 154 | 1.593 ms | 1.729 ms | 10 |
| 250 | 1.885 ms | 2.078 ms | 9 |
| 251 | 1.361 ms | 1.393 ms | 6 |

Nenhuma outra categoria além de carta/fim de turno excedeu 1 s nessas três
execuções. Isto não certifica o SLO; a conclusão favorável do benchmark isolado
da etapa anterior não pode ser generalizada. Próximo foco é retirar serialização
redundante dos encontros históricos imutáveis entre comandos.

## Etapa 6 — escrita direta dos grafos imutáveis

O cache entre comandos para encontros encerrados foi experimentado e rejeitado:
não houve ganho consistente nas três seeds. Não foi mantido nem introduziu
retenção global adicional de snapshots.

Folhas abaixo dos agregados profundamente imutáveis passam a ser escritas com
metadados de propriedades ordenados uma vez, sem construir um DOM do grafo
inteiro. Dicionários com chave string são ordenados ordinalmente; arrays mantêm
sua ordem. Conversores atribuídos, polimorfismo, callbacks, extension data,
number handling e chaves de outros tipos usam o serializer original, seguido de
normalização. O escritor de referência e inputs públicos mutáveis/desconhecidos
não usam esse caminho. Um scanner sem DOM mantém o limite de profundidade.

Datas e enums numéricos/string padrão usam tokens do serializer original, com
até 128 escalares por comando (escopos aninhados compartilham o limite). As
chaves distinguem tipo, Kind e offset, não só igualdade de instantes. Conversores
arbitrários não entram nesse reuso. Nada disso faz parte de estados ou fatos.

A primeira variante correta sem reuso dos escalares teve custo excessivo e foi
ajustada antes da aceitação. O ato com comparação independente após cada comando
passou; testes adicionais incluem Unicode ordinal versus UTF-8, datas, conversores
que emitem chaves duplicadas, léxico numérico e políticas de número/ignore.

Novo benchmark Kestrel, mesmas três seeds e 523 comandos/526 sessões/27 encontros,
sem mudança de resultado/replay/recuperação:

| Seed | Máximo POST | Máximo POST + sessão | Interações ≥ 1 s |
| --- | ---: | ---: | ---: |
| 154 | 1.014 ms | 1.081 ms | 1 |
| 250 | 754 ms | 931 ms | 0 |
| 251 | 632 ms | 759 ms | 0 |

O tempo total do conjunto caiu de 4 min 32 s para 2 min 46 s. A meta ainda não
está fechada: não se arredonda o único excesso para declará-la cumprida.

Certificação da variante com escalares: 1.752 testes Core e 64 checks Godot
passaram (sessão compacta, pool HTTP, envelope de apresentação, troca de conexão
e estabilidade do texto das cartas). Os testes Godot são contratos/fixtures,
não medição de gameplay renderizado contra a API real.
Seis testes REST adicionais passaram, incluindo a comparação independente após
cada comando, cache frio/quente, descoberta diferencial, sessão versionada e
três processos independentes com os mesmos receipts/commits/replay.

## Etapa 7 — composição do fingerprint sem buffer duplicado

`ComputeObjectHash` alimenta SHA-256 incrementalmente com os mesmos delimitadores,
nomes escapados e bytes canônicos completos das propriedades, na mesma ordem.
Não é hash de hashes. Evita copiar snapshots grandes para um segundo buffer
crescente só para hashá-lo. Mantém rejeição de nomes duplicados e profundidade.

O nome da propriedade ainda usa o próprio `Utf8JsonWriter`: o experimento com
`JsonEncodedText` rejeitava UTF-16 malformado que o escritor anterior aceitava
com substituição. Testes independentes cobrem cada caractere ASCII, Unicode,
nomes vazios, caixa, duplicatas, UTF-16 malformado e objeto vazio.

Primeira bateria Kestrel com streaming, 523 interações de comando + sessão:
nenhuma atingiu 1 s. Máximos por seed 154/250/251: 840/881/707 ms. Replays,
recuperação e resultados finais permaneceram iguais. A criação da run não
estava nessa contagem; foi incluída na bateria final ampliada.

O teste tem um gate opcional `HEROSCRIPT_INTERACTION_BUDGET_MS=1000` para a
execução isolada de `SocketSession_MeasuresCommandsAndRefreshAcrossPublishedSeeds`.
Ele falha se qualquer interação medida atingir o limite, sem excluir outliers.
Suites comuns de correção continuam sem limite de tempo absoluto, pois podem
rodar concorrentes/em hardware de CI diferente. O relatório sempre mostra os
excessos, independentemente de o gate estar habilitado.

Na bateria ampliada com criação, o gate reprovou a seed executada a frio:
`START_RUN` levou 1.977 ms no POST/2.279 ms com a primeira sessão. As duas
criações seguintes levaram 696/690 ms com sessão. Os outros 523 comandos
continuaram abaixo de 1 s. Logo a meta global continua aberta: a inicialização
de nova run a frio precisa de uma etapa própria, não de exclusão do relatório.

Certificação do streaming: 1.753 testes Core e seis testes REST passaram,
incluindo o oráculo de cada comando e os três processos independentes.
