# Autoria de conteúdo na engine e na API HeroScript

Este plano detalha a implementação na engine da primeira entrega de autoria, começando por **Cartas**. O escopo funcional e as decisões de interface estão no [plano do editor de game settings](EDITOR_GAME_SETTINGS_PLANO.md). Os endpoints abaixo são propostas de implementação.

## 1. Escopo da primeira entrega

- Ler cartas e sua proveniência, consultar tags em uso e criar, editar ou excluir cartas pertencentes ao pacote gravável escolhido para o setting. Validar ao menos a raridade, as tags e um componente de custo ou condição com o compilador e os validadores existentes.
- Receber lotes semânticos pela API, gravar JSONs no workspace do Git, gerar revisões publicadas e ativar a revisão de cada setting afetado. O mesmo contrato atende ao modo padrão, com um lote por salvamento, e ao Hot Reload, com um lote por ação confirmada.
- Oferecer reversão e reaplicação de lotes publicados, com registro durável, idempotência e detecção de alterações concorrentes.
- Expor conteúdo herdado com proveniência. Na primeira entrega, autoria sobre pacotes externos, remoção de conteúdo herdado, renomeação de IDs, alterações arbitrárias de pacotes e edição autônoma de tags ficam para incrementos posteriores. Se o pacote editável for consumido por outro setting do workspace, calcular e validar o impacto antes de escrever.

O JSON fonte é a referência para Git e reconstrução após reinício. O bundle publicado é a referência imutável para gameplay. Uma run ativa continua vinculada à sua revisão. A chave de autoria deve identificar a visão dos arquivos, mesmo quando a revisão de gameplay ainda é a mesma.

## 2. Pontos de extensão

| Camada | Implementação |
| --- | --- |
| `src/Mods` | Reutilizar `SettingCompiler`, `IPackageProvider` e proveniência para resolver uma carta e compilar uma visão candidata em memória ou em staging. Adicionar uma sobreposição de leitura para substituir apenas os arquivos do lote, inclusive manifesto se necessário. O provider de disco continua lendo a raiz de pacotes configurada. |
| `src/Core/Content` | Reutilizar `ContentKindRegistry`, validação de grafo, `ContentPublicationService`, o resolver de runtime e o cache revisionado. Permitir persistir e preparar bundles independentemente de ativá-los. Publicar o mapa de revisões afetadas de uma vez sob o mesmo bloqueio, com validação de todos os candidatos antes de modificar o mapa. Ativação deve ser repetível na recuperação. |
| Engine de autoria | Adicionar serviço de aplicação de lotes, gravador de fontes e registro durável de operações. O serviço coordena resolução e precondições, preparação do candidato, validação, escrita, ativação e recuperação. Manter operações semânticas de carta sem aceitar caminhos do cliente. |
| `src/API` | Expor DTOs e estados da operação em controller de autoria. Reusar `AdminEndpoint` e a política de acesso a ferramentas. Registrar dependências em `Program.cs` e documentar as operações no OpenAPI gerado. |

Código de referência: [compilador](../../src/Mods/SettingCompiler.cs) · [providers](../../src/Mods/PackageProviders.cs) · [registro de tipos](../../src/Core/Content/ContentKindRegistry.cs) · [publicação](../../src/Core/Content/ContentPublicationService.cs) · [ativação](../../src/Core/Content/ContentManifestProvider.cs) · [reload](../../src/Core/Content/ContentReloadService.cs).

## 3. Contrato inicial

Prefixo `/api/v1/admin/authoring`. Nas rotas abaixo, `S` significa `/settings/{settingId}`.

| Método e rota | Responsabilidade |
| --- | --- |
| GET `/capabilities` | Identificar o workspace e as capacidades de autoria ativas, incluindo tipos graváveis e suporte a undo/redo. |
| GET `/settings` | Reusar a descoberta de settings, incluindo settings apenas de autoria e diagnósticos. Informar pacote gravável e estado da publicação ou recuperação. |
| GET `S/kinds` e GET `S/kinds/{kind}/schema` | Listar tipos e descrever o schema de autoria de cartas, campos de referência e operações aceitas. O restante pode ser anunciado como leitura enquanto o backend de escrita evolui. |
| GET `S/definitions/cards` | Listar cartas com busca, cursor e limite. Ordenar por ID, vincular cursor à versão da visão e informar quando a listagem deve ser reiniciada. |
| GET `S/definitions/cards/{cardId}` | Entregar a definição editável, o valor efetivo compilado, a proveniência e permissões de edição/exclusão. Incluir ETag da visão. |
| GET `S/references/tags` | Listar strings de tags em uso na visão compilada, com filtro e paginação. Distinguir tags declaradas em cartas de menções em critérios, onde o Core puder identificar a origem. |
| POST `S/changes/validate` | Planejar e validar um lote ou uma compensação contra a fonte atual, sem alteração durável. Retornar efeitos previstos, settings afetados e diagnósticos. |
| POST `S/changes` | Aplicar e ativar um lote de autoria ou uma compensação. Exigir `If-Match` e `changeId`. Responder com recibo e nova ETag após sucesso. |
| GET `S/changes/{changeId}` | Consultar estado ou recibo de uma operação após resposta perdida ou recuperação. |
| POST `S/changes/{changeId}/undo` e `S/changes/{changeId}/redo` | Aplicar uma compensação com ID próprio. Podem delegar ao mesmo comando de gravação com `undoOf` ou `redoOf`, também disponível para validação prévia e salvamento posterior. |

Um lote normal contém uma lista ordenada de operações `create`, `update` ou `delete`, cada uma com `kind`, `definitionId` e definição em JSON quando aplicável. Para P0, `update` recebe a definição fonte completa com ID estável. O servidor preserva o restante do artefato, exige o mesmo ID no corpo e valida o documento final. Rejeitar combinações ambíguas sobre a mesma carta dentro do lote ou exigir que o cliente envie uma única operação consolidada. As permissões do pacote são resolvidas na engine. Usar DTO flexível para o valor JSON e validação tipada do Core para o conteúdo.

O recibo contém `changeId`, estado, versão de fonte anterior e nova, revisões ativas por setting, arquivos relativos afetados, resumo por definição, alertas e vínculos de compensação. Retornar `If-Match` como precondição sobre a mesma visão identificada pela ETag na leitura. Usar `428` para precondição ausente, `412` para base alterada, `409` para conflito de identidade da operação ou de compensação, `422` para candidato inválido e `503` para workspace em recuperação. Padronizar `ProblemDetails` com código, severidade, tipo, ID, JSON Pointer e parâmetros. A comparação de idempotência deve acontecer antes de rejeitar um `If-Match` agora desatualizado de uma operação já confirmada. Mesmo ID e mesmo envelope devolvem o recibo original. Mesmo ID e envelope diferente retornam conflito.

## 4. Validação e resolução de fontes

1. **Validar acesso e escopo.** Localizar o workspace configurado e o setting. Resolver a origem da carta pelo catálogo e pela proveniência. Para criação, o servidor escolhe um artefato de cartas no pacote local configurado ou declara um novo artefato em seu manifesto. Rejeitar arquivos fora da raiz, links/reparse points, colisões de caixa e targets de providers somente leitura. Uma carta herdada pode ser consultada, com indicação da origem e da falta de permissão de alteração.
2. **Capturar a base.** Calcular ETag forte sobre entradas relevantes da compilação, incluindo conteúdo dos arquivos fonte, manifestos e configuração de pacotes visível ao workspace. Para P0, uma ETag conservadora de workspace e um único gravador simplificam a coordenação entre settings. Se algum arquivo relevante mudar por checkout ou edição externa, a revisão ativa antiga não satisfaz a precondição.
3. **Montar o candidato.** Aplicar o lote a uma visão isolada dos documentos. Atualizar pela chave da carta em catálogos que contêm várias definições. Preservar propriedades não tocadas, codificação e finais de linha. Adotar regra estável para indentação e inserção. Testar que a mesma operação gera o mesmo diff e que a edição de uma carta não reserializa o catálogo inteiro com mudanças de estilo.
4. **Validar estrutura e regra.** Reutilizar a desserialização estrita do registro, verificar ID, variantes polimórficas e referências. Compilar com os mesmos providers, ordem de pacotes e patches do runtime. Fazer a validação de grafo para bloqueios de exclusão e devolver os caminhos dos usos encontrados. Para cartas, cobrir no mínimo catálogos/pools, decks e referências por ID nas estruturas já usadas pelos settings escolhidos. Onde a cobertura atual do grafo for insuficiente, estendê-la antes de liberar exclusão. Um lote pode remover um uso e sua definição juntos quando a engine suportar ambas as operações no escopo daquele setting.
5. **Validar dependentes.** Descobrir todos os settings que consomem o pacote modificado, inclusive por dependência transitiva. Validar os candidatos de todo o conjunto e preparar runtimes dos settings que devam estar ativos de acordo com a política do host. Diagnosticar patches que deixariam de aplicar, incluindo `ExpectedHash` de pacote. Uma falha impede a aplicação do lote inteiro. O `validate` usa este mesmo fluxo de leitura e validação, com a versão de fonte usada no plano.
6. **Tratar tags segundo o modelo atual.** Uma tag nova é gravada como string no campo `tags` da própria carta. Combinar consulta de tags com os campos de referência conhecidos pelo Core, preservando a semântica de identidade atual. Excluir uma carta pode fazer uma tag deixar de aparecer no catálogo derivado. Um registro de tags independente exige contrato e migração próprios.

## 5. Gravação e Hot Reload

O `ContentReloadService` atual recompila fontes já salvas e pode ativar o manifesto antes de criar o runtime. Reaproveitar suas etapas de compilação, validação e cache no novo fluxo, com preparação de runtime antes da ativação e confirmação explícita da gravação de fontes. Definir estas fases:

| Fase | Operação e garantias |
| --- | --- |
| Planejar | Adquirir o bloqueio de autoria do workspace, conferir idempotência e `If-Match`, montar o candidato e validar todos os settings. Preparar os bundles e verificar que os runtimes podem ser construídos antes de tocar nas fontes. |
| Preparar | Registrar o lote duravelmente com hashes anteriores e novos, caminhos relativos, staging e imagens necessárias para recuperação e compensação. Persistir bundles imutáveis preparados sem mudar as revisões ativas. Manter staging e journal fora dos arquivos de conteúdo versionados. |
| Gravar | Conferir a base novamente e substituir cada arquivo por renomeação a partir de staging no mesmo volume. Usar verificação de hash do destino quando possível e registrar fases no journal. Conferir o conjunto final. Uma renomeação atômica protege um arquivo. O journal e a recuperação protegem a consistência do lote diante de interrupção. |
| Confirmar | Marcar as fontes como confirmadas duravelmente depois de validar os hashes finais. Ativar os manifestos preparados em uma atualização coordenada, invalidar visões mutáveis relacionadas e persistir o recibo aplicado. A API de autoria evita leituras intermediárias por coordenação com o gravador. Runs vinculadas continuam usando caches imutáveis. |
| Recuperar | Antes de habilitar autoria no startup, inspecionar lotes pendentes. Antes da confirmação de fontes, restaurar somente arquivos que ainda correspondam aos hashes escritos pelo lote. Depois da confirmação, completar ativação e recibo de forma idempotente se as fontes ainda corresponderem. Conteúdo desconhecido no disco exige diagnóstico e recuperação explícita, preservando os arquivos externos. |

O gravador deve coordenar processos da engine que compartilhem a raiz, por exemplo com um bloqueio exclusivo de workspace e um único store de autoria. Git e outros editores operam fora desse bloqueio. Detectar checkout/merge quando possível, reverificar hashes no momento da troca e após escrita, e documentar que leitores externos podem observar o intervalo entre renomeações. Para P0, configurar uma única engine gravadora por checkout/worktree, com porta e store próprios. Manter o reload manual para alterações externas, coordenado com o mesmo bloqueio. Impedir que um publish de draft histórico altere a revisão ativa durante a aplicação ou contorne a política do setting sob gestão de autoria.

O **Hot Reload** recebe o mesmo lote de `POST S/changes` e ativa a revisão assim que aquela ação for confirmada. Uma falha de validação mantém fontes e revisão anteriores. Uma falha depois da confirmação das fontes deve aparecer como operação em recuperação até que a ativação e o recibo sejam resolvidos. Após perda de conexão, consultar pelo `changeId` ou repetir o mesmo envelope. O estado do recibo informa se o efeito foi confirmado. O mecanismo atual de troca de revisão de run continua sendo um fluxo de playtest separado.

## 6. Desfazer e refazer confirmados

- Registrar as mudanças semânticas diretas e inversas junto com o resultado confirmado, com identidade de recurso e hashes pré/pós-alteração. Guardar evidência por artefato suficiente para restaurar uma criação ou exclusão, inclusive mudanças de manifesto geradas pelo lote. A retenção define por quanto tempo uma ação pode ser compensada. Tornar essa disponibilidade observável pela API.
- **Undo** referencia um lote aplicado. Verificar `If-Match` e que as definições e entradas de manifesto afetadas ainda correspondam ao estado posterior esperado. Aplicar a operação inversa sobre o estado corrente, preservando mudanças externas que forem independentes. Para a primeira entrega, se não for possível provar independência de uma edição no mesmo artefato, retornar conflito com diagnóstico. Validar novamente o grafo e os settings afetados. A compensação grava fontes e ativa a revisão produzida pelo compilador.
- **Redo** referencia a reversão confirmada, usa seu estado resultante como precondição e reaplica a intenção original pelo mesmo pipeline. Registrar os vínculos para impedir compensações duplicadas e distinguir um novo comando de um retry idempotente. Se uma nova dependência tornar a operação inválida, retornar diagnóstico mesmo com hashes válidos.
- Permitir a prévia de undo/redo em `changes/validate`. O modo padrão pode confirmar essa intenção depois no mesmo `changes`. Hot Reload usa a execução imediata. Em ambos, o servidor verifica a base no momento da gravação.
- Uma revisão antiga pode reaparecer quando as fontes voltam ao conteúdo anterior. O registro de autoria ainda contém um novo comando confirmado. A operação preserva a distinção entre versão das fontes, identidade do comando e hash do gameplay.

## 7. Ordem de implementação

1. **Fixar o contrato e as fixtures.** Usar dois settings reais em cópias temporárias, como `uno-clash` e `circular-table`, e uma fixture pequena com pacote compartilhado, carta herdada, arquivo com múltiplas cartas e referências de exclusão. Definir DTOs, códigos, ETag e escopo de escrita.
2. **Entregar leitura e validação.** Expor descoberta, lista/detalhe, schema e referências de tags. Criar visão candidata para alterações de cartas e diagnósticos estruturados. Demonstrar em teste que validar deixa fontes, store e revisões ativas intactos.
3. **Entregar gravação e ativação.** Implementar gravador, preparação de runtime, bloqueio, journal e inicialização com recuperação. Reusar o pipeline para salvar e aplicar imediatamente. Testar primeira aplicação por API em setting real.
4. **Entregar compensação.** Persistir operações inversas, expor prévia e execução de undo/redo. Simular concorrência, falhas e recuperação em host isolado.
5. **Fechar a fatia.** Exportar contrato gerado, documentar habilitação da autoria e fluxo de Git. Validar reconstrução das revisões após reinício e isolamento de runs. Expandir a outros tipos pelo registro apenas depois de satisfazer os critérios abaixo.

## 8. Testes e critérios de aceite

| Nível | Casos mínimos |
| --- | --- |
| Core e Mods | Resolver fonte própria e herdada; rejeitar caminho fora da raiz e link; preservar outras cartas e campos do arquivo; manter diff estável; aceitar tag existente e nova; rejeitar ID duplicado ou trocado e componente inválido; detectar carta referenciada e patch dependente incompatível. Compilar o candidato e o conteúdo relido do disco com a mesma revisão após gravação. |
| API e contrato | Exercitar métodos, respostas e schemas no OpenAPI. Autoria desabilitada, credencial ausente e política negada. Lista/detalhe e tags isolados por setting. Criação, edição, exclusão, validação sem efeito durável, estados `428`/`412`/`409`/`422` e recibo consultável. Verificar a ETag em duas sessões de teste e após edição externa. |
| Concorrência e recuperação | Dois clientes partem da mesma base, apenas um grava. Retry com o mesmo ID recebe o mesmo recibo; payload diferente conflita. Injetar falhas após staging, após primeira substituição, após confirmação de fontes e antes do recibo final. Reiniciar e conferir arquivos, revisão ativa e status. Alteração externa durante recuperação preserva o arquivo divergente e bloqueia novas gravações até resolução. |
| Modo padrão e Hot Reload | Validar o candidato sem alterar fonte ou revisão, depois aplicar lote e observar nova revisão por leitura. Aplicar ações consecutivas, verificar recibo e rejeição inválida. Testar setting dependente e demonstrar que falha em qualquer candidato impede ativação parcial. Verificar run antiga na revisão original e nova run com a revisão publicada. |
| Undo/redo e Git | Criar, editar e excluir, desfazer/refazer pelos endpoints e reler a fonte e a revisão em cada etapa. Simular conflito em estado alterado, tentativa duplicada, dependência nova e restart entre ação e undo. Comparar o diff no checkout temporário e validar recompilação a partir somente dos arquivos versionados. |

A fatia fica pronta quando um cliente que conhece apenas a API consegue listar e editar uma carta, criar uma tag no mesmo lote, validar um componente, confirmar a gravação no Git, observar a revisão ativa e desfazer/refazer após reinício. O caso de interrupção precisa demonstrar recuperação verificável e preservação de dados externos. Registre tempos de validação, preparação e aplicação em settings reais para orientar o desempenho do Hot Reload.
