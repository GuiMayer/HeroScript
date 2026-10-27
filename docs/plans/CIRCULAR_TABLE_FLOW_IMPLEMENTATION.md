# Fluxo circular — implementação e certificação

Data: 27/10/2026. [Guia de autoria e migração](../content/circular-table-flow.md).

## Entrega por etapa

| Etapa | Commit | Entrega |
|---|---|---|
| 1 | `210c0d8` | Plano, contrato e reprodução da configuração antes ignorada |
| 2 | `9c8e232` | Política obrigatória, migração explícita e idempotente para PerRound |
| 3 | `3ff3dbd` | Redutor puro de percurso PerRound/Circular e integração ao lifecycle |
| 4 | `60ea724` | Efeitos de sequência e seletores relativos compartilhados |
| 5 | `f32f75d` | Consultas à topologia real e fatos imutáveis por ativação |
| 6 | `fd8f0c8` | Condições canônicas de comandos e guard de saída da ativação |
| 7 | `354bfbf` | Distribuição explícita por participantes e rollback |
| 8 | `dec806c` | Vitória individual configurável e vencedores explícitos |
| 9 | `09071f5` | Setting opcional circular-table com quatro participantes |
| 10 | `3b81216` | Projeções REST, motivos de bloqueio e schemas |
| 11 | `12bf35c` | Certificação integrada, limites, contextos candidatos, migração do duelo e cliente web |
| 12 | Commit que adiciona este relatório | Guia final, changelog e exemplos de autoria |

O pacote `uno-clash` e seu cliente web já existiam localmente, mas não estavam versionados. Seus arquivos necessários foram incluídos na etapa 11 para tornar reproduzível a migração da regra local para JSON/API. As alterações visuais e de cartas não relacionadas que já estavam pendentes na árvore de trabalho não foram incluídas.

## Comportamento e compatibilidade

- Default e Ascendant usam `PerRound` explícito. O duelo continua com dois atores e percurso por rodada. Não existe seleção automática de Circular por quantidade de participantes.
- A mesa nova opta por Circular, sem contadores paralelos de tamanho da mão. Distribuição, penalidades, compra, cor e vitória são definidas no conteúdo.
- Não há segundo loop de jogo, sistema de cartas ou processador de efeitos. Os executores canônicos continuam responsáveis pelas transações e pelo RNG.
- Engine passa de 33 para **34**, pois os contratos de fluxo e os fatos persistidos mudaram. Preservação de gameplay não implica igualdade de hash entre versões. Saves e bundles históricos não são reescritos; sua execução requer runtime compatível.
- Queries observam o snapshot candidato atual, inclusive em fluxos de efeito e de boundary. Fatos são zerados antes do começo da nova ativação e isolados na projeção de intenções de outro ator.
- Todo fluxo do monte compartilhado declara RunOwner. Uma compra causada por efeito não deduz o dono do monte a partir do alvo.
- Descoberta e execução direta usam as mesmas condições, incluindo PASS_PRIORITY. HasBaseLegalCard consulta a legalidade básica, sem recursão das condições de comando.

## Verificação executada

1. Core: **1.935 testes passaram**, excluindo `UnoClashParityTests`, arquivo preexistente e não versionado com pressupostos incompatíveis com o pacote local. Nenhuma falha foi ignorada nas classes versionadas. Depois foi adicionado e executado mais um caso para iteração aninhada sem movimentos: os **quatro testes de ParticipantDealingTests passaram**.
2. API: **12 testes selecionados passaram**, incluindo a abertura da mesa repetida em hosts novos com hashes iguais, contrato OpenAPI, disponibilidade canônica e o duelo PerRound. Uma asserção do teste curto inicialmente comparava o snapshot anterior ao último comando; foi corrigida para reler o estado final.
3. Jornada longa: **1 teste passou em 8min18s**, até um vencedor individual, com retry de todos os comandos, retomada em outra instância, branch e replay semântico do histórico completo. A origem manteve seu hash após a criação da branch.
4. Python: **3 testes passaram**, incluindo idempotência/migração e equivalência da fórmula de compra do duelo com a regra anterior em 192 combinações de inputs.
5. JavaScript: **3 testes passaram**, provando que o botão usa disponibilidade e motivos da API sem decidir a regra de compra localmente.
6. `git diff --cached --check` passou antes do commit de certificação.
7. Certificação adicional em cópia dos arquivos commitados, sem as alterações locais pendentes: **1.934 testes do Core passaram, sem exclusões**, e **2 testes REST passaram** (duelo PerRound e abertura circular repetida com retomada/branch/replay curto). Também passaram os 27 testes direcionados de sequência, consultas, condições, distribuição, pool e resultado. Isso confirma que a compilação e os cenários entregues não dependem dos arquivos locais não commitados.

Comandos reproduzíveis a partir da raiz:

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj
dotnet test tests/API.Tests/API.Tests.csproj --filter '(FullyQualifiedName~CircularTableFlowTests|FullyQualifiedName~UnoDuelCommandPolicyTests|FullyQualifiedName~MultiActorPool|FullyQualifiedName~ContractDocumentationTests)&FullyQualifiedName!~WholeCircularJourney'
dotnet test tests/API.Tests/API.Tests.csproj --filter FullyQualifiedName~WholeCircularJourney -- RunConfiguration.TestSessionTimeout=1800000
python -m unittest discover -s tools/tests -p '*migration*.py'
node --test examples/uno-clash-web/tests/mandatory-draw.test.cjs
python tools/migrate-activation-sequence.py
```

O filtro do Core usado na árvore local foi `FullyQualifiedName!~UnoClashParityTests`. Esse arquivo não integra os commits desta entrega. A jornada longa tem uma etapa própria de CI com limite de 30 minutos. O timeout HTTP de 15 minutos é exclusivo do cliente de certificação; não muda o jogo nem os timeouts do transporte usado pelos jogadores. Não foi estabelecida por esse teste uma garantia de latência de ação.

## Limites explícitos

Não inclui autenticação multiplayer, entrada dinâmica de assentos ou Circular combinado com ATB. O layout do app web continua de duelo; o setting de quatro participantes está certificado pela API. Contestação de +4 não foi configurada. Visibilidade de cartas não substitui autorização de rede. A documentação do plano preserva propostas históricas; os nomes executáveis são os do guia final e dos JSONs publicados.
