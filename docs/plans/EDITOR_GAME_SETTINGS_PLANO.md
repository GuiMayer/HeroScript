# Editor de conteúdo e regras da HeroScript

Análise de 15 de novembro de 2026, baseada no checkout `96e51f9f`. O trabalho desta etapa é o relatório e o plano. Os contratos propostos abaixo são escopo de implementação.

O [plano de autoria na engine e na API](AUTORIA_ENGINE_API_PLANO.md) detalha a implementação de Cartas, a gravação dos JSONs, o Hot Reload, o desfazer/refazer e os testes.

## 1. Decisão e principal dependência

Construir uma aplicação web local, servida sob `/editor/`, com a engine como autoridade sobre o conteúdo. Começar por **Cartas**, incluindo criação, edição, exclusão, tags e a validação de um campo de regra da própria carta. A navegação já deve representar as demais áreas e mostrar **Ainda não implementado** nas páginas sem editor. O modo padrão grava com **Salvar**. No **Hot Reload**, cada ação concluída é aplicada pela API imediatamente.

Hoje a API consegue consultar conteúdo publicado e compilar, validar e publicar JSONs que já estão no disco. Falta a operação que recebe as edições da ferramenta, grava as fontes versionadas e ativa o resultado com controle de concorrência. Essa operação é a dependência central da primeira fatia funcional. O browser mantém rascunhos, preferências e estado de interação. Compilação, regras do jogo, referências, escrita e reversão confirmada pertencem à engine.

## 2. Descoberta e inventário de endpoints

Verificação executada no checkout: exportação em host de teste isolado, quatro testes de API (OpenAPI, settings jogáveis, publicação de draft e manutenção da revisão da run) e dois testes do Core (publicação via reload e rejeição de candidato inválido). Os seis passaram. Os endpoints sugeridos e a interface ainda precisam ser implementados e testados.

**Existe `GET /openapi/v1.json`.** O servidor usa Swagger/Swashbuckle com API Explorer e publica o JSON em todos os ambientes. A interface Swagger em `/docs/api` é habilitada em Development. Rodei o exportador do repositório em um host de teste isolado. O OpenAPI gerado listou **114 caminhos e 117 operações**. O levantamento do código encontrou **117 atributos HTTP em 41 controllers com ações** (89 GET, 27 POST, 1 DELETE). O anexo lista método e caminho do contrato gerado. A contagem de operações cobre os controllers nesse checkout. A própria URL do Swagger, sua interface e outros recursos de middleware ficam fora de `paths`.

O arquivo versionado `openapi/heroscript-v1.json` tem **68 caminhos e 70 operações**. Na comparação por método e caminho, há **48 operações só no contrato gerado e uma só no arquivo estático**. Em particular, o `POST /api/v1/admin/content/drafts` consta na cópia estática e não existe no controller atual. A criação vigente fica em `POST /api/v1/admin/settings/{settingId}/drafts`. O `GET /api/v1/capabilities` anuncia capacidades da engine e o perfil de ferramentas. A disponibilidade de uma operação administrativa depende adicionalmente da configuração e das credenciais.

**Controle de mudanças recomendado.** Reutilizar `tools/ExportOpenApi` na CI, ordenar e comparar `método + caminho`, parâmetros, schemas, mídia e respostas. Registrar adições no PR e exigir revisão em remoção ou mudança incompatível do contrato de autoria. Preservar o contrato público atual como contrato deliberado, com um snapshot gerado próprio para autoria ou uma classificação explícita de escopo. Comparar o API Explorer com a tabela de rotas registradas em teste caso surjam rotas ocultas ou endpoints fora dos controllers. Uma rota nova entra automaticamente no inventário documentado se o API Explorer a publicar. Ela só ganha formulário após ser ligada a metadados de autoria e a uma capacidade negociada. Para o editor, consultar um endpoint de capacidades de autoria autenticado e usar a descrição da API para diagnóstico e geração de tipos. Um catálogo HTTP adicional de toda a aplicação é dispensável para a superfície observada.

Fontes locais: [Program.cs](../../src/API/Program.cs), [exportador OpenAPI](../../tools/ExportOpenApi/Program.cs), [política do contrato versionado](../../openapi/README.md), [filtro de schemas de comandos](../../src/API/Contracts/CanonicalCommandDocumentFilter.cs).

## 3. Rotas relevantes e comportamento

Nas tabelas, os caminhos são relativos a `/api/v1`. **Autoria** significa que a rota tem `AdminEndpoint`.

| Método e caminho | Acesso e comportamento atual | Implicação para o editor |
| --- | --- | --- |
| GET `/admin/settings` | Autoria. Catálogo de descoberta dos providers, incluindo definições sem lançamento e diagnósticos. | Melhor base atual para o seletor. Permitir ver settings com erros e informar quando o carregamento da edição está indisponível. |
| GET `/content/settings` | Público. Filtra settings sem `launch` ou sem revisão ativa. Diagnósticos de erro no catálogo podem produzir 503. | Adequado ao launcher. Seria uma lista incompleta para autoria. |
| GET `/admin/settings/{settingId}` | Autoria. Compila a composição descoberta e entrega definição, bundle, pacotes e proveniência. Falha com 422. | Bom diagnóstico e fonte inicial de proveniência. O snapshot completo é pesado para o carregamento de toda página e a compilação pode falhar antes do editor abrir. |
| POST `/admin/settings/{settingId}/validate` | Autoria. Compila arquivos existentes e valida o bundle. | Útil para validação de branch/disco. O contrato não recebe alterações JSON do formulário. |
| POST `/admin/settings/{settingId}/drafts` | Autoria. Cria um draft do bundle compilado em disco e responde 201 com localização. | O draft atual é um snapshot de publicação. A rota não aceita edições da UI. |
| GET `/admin/content/drafts/{draftId}` | Autoria. Lê draft por ID. | Permite inspeção. Faltam edição, listagem por setting e descarte via API se houver um futuro fluxo de drafts compartilhados. |
| POST `/admin/content/drafts/{draftId}/validate` | Autoria. Responde 200 com o resultado do serviço, inclusive erros de validação. | Interpretar `isValid` e as mensagens, mesmo em resposta HTTP bem-sucedida. |
| POST `/admin/content/drafts/{draftId}/publish` | Autoria. Recebe `expectedVersion` do draft, grava bundle e ativa seu manifesto. Divergência da versão retorna 409. | A versão do draft não protege a revisão ativa ou a versão das fontes. Publicação deixa os JSONs de autoria intactos. |
| POST `/admin/content/reload` | Autoria. Recebe `settingId`. Exige `AllowConfigReload`. Invalida caches mutáveis, compila, cria draft, valida, publica e aquece o runtime. | Útil após Git ou edição externa. Por si só não grava edições e uma falha no pré-aquecimento pode ocorrer depois da ativação. |
| GET `/content/revisions` | Público. Recebe `configName`, devolve `currentRevision` para ele e agrega manifests conhecidos e publicados. | Filtrar por setting na UI quando necessário. O servidor não restringe o conjunto de revisões a esse setting. |
| GET `/content/revisions/{revision}` | Público. Resolve manifesto por revisão, com consulta ao manifesto atual e ao store publicado. | O ID da revisão permite inspeção. Tratar `configName` nessa rota como contexto de fallback, pois a resolução global ocorre primeiro. |
| GET `/content/{kind}` | Público. Lista definições publicadas, exige `revision` no serviço e combina com `configName`, que por padrão vale `default`. Pagina por `after`, limite padrão 100 e máximo 500. `tag` só filtra a lista por tags presentes no JSON. | Enviar setting **e** revisão em todas as consultas. Há paginação, mas busca textual e um índice de referências exigem rotas próprias. |
| GET `/content/{kind}/{definitionId}` | Público. Resolve uma definição na revisão publicada explicitamente selecionada. | Reutilizável para visualização e comparação. A resposta é a definição efetiva, possivelmente herdada ou modificada por patch. |
| POST `/content/validate` | Público. Recebe `bundle` completo ou `revision` publicada. A validação devolve erros e alertas, com 422 para candidato inválido. | Reutilizar o Core para validar alterações montadas no servidor. A UI não deve recompor pacotes ou hashes. |
| GET `/content/{settingId}/revisions/{revision}/gameplay-events/catalog` e GET `/content/{settingId}/revisions/{revision}/gameplay-responses/schema` | Catálogo de eventos e metadados de respostas de gameplay. | Reusar nas páginas de regras relevantes. O schema cobre respostas de gameplay, além das quais existem outros tipos de regra. |
| GET `/runs/{runId}/content/activation-preview` e POST `/runs/{runId}/commands` | Prévia e comando `ACTIVATE_CONTENT_REVISION` para uma run, sujeitos às capacidades e às verificações de compatibilidade. | Fluxo opcional de playtest. Desfazer uma edição exige um contrato próprio de autoria. |
| GET `/operations`, `/operations/{name}`, `/operations/categories`, GET `/simulations/formulas` e `/simulations/formulas/{name}`, POST `/simulations/formulas/evaluate` e `/simulations/math/expression/evaluate` | Descoberta de operações/fórmulas e avaliação conforme os parâmetros de cada serviço. | Apoiam exploração e prévia de regras. Qualquer avaliação usada no editor precisa corresponder ao setting/revisão e ao contexto selecionado. |
| GET `/version`, `/capabilities` e `/health/ready` | Versão, capacidades e estado da instância. | Diagnóstico de conexão e compatibilidade do editor. |
| POST `/sandbox/scenarios/validate`, POST `/sandbox/runs` e consultas da sandbox | Validação de cenários e execução de playtest isolado. | Integrar depois que o editor apresentar claramente qual revisão será testada. |

Fonte local: [controllers](../../src/API/Controllers), [serviço de publicação e resolução](../../src/Core/Content/ContentPublicationService.cs), [registro de tipos](../../src/Core/Content/ContentKindRegistry.cs).

### Fontes, revisões e reversão

- **Fontes para Git.** O setting declara ID, nome, descrição, lançamento e pacotes com versões. Os `package.json` apontam para fontes e dependências. Um arquivo pode conter várias cartas (por exemplo, `uno-clash/Resources/cards/uno.json`). O compilador combina definições e patches `Merge`/`Replace` e registra proveniência por caminho de artefato, definição e JSON Pointer. O provider de pacotes oferece descoberta e leitura. Para remover uma definição própria, o gravador precisa alterar o documento fonte e, quando aplicável, o manifesto. Para uma definição herdada, falta uma operação explícita de ocultação/exclusão no pacote dependente. Remover um override, excluir uma definição própria e ocultar uma definição herdada têm efeitos distintos.
- **Revisões para runtime.** Cada bundle tem revisão por hash, é publicado em um store separado (padrão `data/content`), e a revisão ativa de cada setting fica no provider em memória. A inicialização compila/publica as fontes configuradas, com cache de startup quando aplicável. Runs existentes continuam com suas revisões; mudar o seletor do editor apenas muda o contexto de navegação. O reload atual serializa suas chamadas dentro de um processo. Sua sequência valida antes de publicar, mas aquece o runtime depois de ativar; escritas dos arquivos de publicação/draft e ativação são etapas distintas.
- **Reversão hoje.** Existe mecanismo interno para ativar um manifesto e a publicação de um draft compatível pode reativar o bundle que ele carrega. Não há um endpoint explícito que reverta uma edição de autoria e restaure as fontes versionadas. O caminho operacional atual para restaurar as fontes é recuperar a alteração desejada no Git e recarregar o setting. Trocar a revisão de uma run e republicar um snapshot antigo deixam as fontes como estavam. Um rollback administrativo de revisão pode ser útil depois para operação, com semântica independente do desfazer da edição.
- **Tags hoje.** `CardContentDefinition.Tags` é uma lista de strings. O registro da engine não contém o tipo `tags`. `keywords` existe, mas a engine não o declara como registro autoritativo das tags da carta. Tags também aparecem em critérios como `requiredTags`, `excludedTags` e nos pools. A primeira fatia deve oferecer **Criar tag e adicionar** inserindo a string na carta, em uma única operação. O servidor deve retornar sugestões e usos de tags extraídos dos campos que ele conhece, combinados com alterações ainda no rascunho quando aplicável. Para tags autônomas, documentação, nomes traduzidos, renomeação e remoção global, decidir se será criado um registro explícito no Core com migração de strings legadas. Preservar a semântica de maiúsculas/minúsculas da engine. O filtro REST por tag é insensível a caixa e sozinho não define a identidade das tags.

Fontes locais: [modelos de pacotes](../../src/Mods/PackageModels.cs), [compilador](../../src/Mods/SettingCompiler.cs), [provider de fontes](../../src/Mods/PackageProviders.cs), [modelo de carta](../../src/Core/Run/Content/CardContentDefinition.cs), [exemplo Uno Clash](../../data/configs/uno-clash/Resources/cards/uno.json), [reload](../../src/Core/Content/ContentReloadService.cs), [manifestos ativos](../../src/Core/Content/ContentManifestProvider.cs).

## 4. Contrato proposto para fechar as lacunas

Usar um grupo `/api/v1/admin/authoring` com DTOs tipados, IDs de domínio e `operationId` estáveis. Os caminhos seguintes são sugestões. `S` representa `/settings/{settingId}` dentro desse grupo.

| Prioridade | Operação proposta | Responsabilidade da engine |
| --- | --- | --- |
| P0 | GET `/capabilities` e GET `/settings` | Informar versão de autoria, workspace/instância, settings e diagnósticos, permissões e operações habilitadas (leitura, escrita, aplicação, desfazer). |
| P0 | GET `S/kinds` e GET `S/kinds/{kind}/schema` | Expor os tipos e os metadados da tela, inclusive campos, defaults, discriminadores, referências, editabilidade, versão do schema e descrições. Começar com cartas e expandir a partir do `ContentKindRegistry`. |
| P0 | GET `S/definitions/{kind}` e GET `S/definitions/{kind}/{id}` | Expor fonte de autoria e visualização efetiva com proveniência e ETag/token de versão. Paginar/buscar. Mostrar se o dado é editável, herdado ou somente leitura. Pode coexistir com a consulta de snapshots via API pública. |
| P0 | GET `S/references/{kind}` | Buscar opções paginadas de referências por contexto, incluindo tags detectadas, bundles e recursos. Informar capacidade de criação e tipo de editor. Reusar a mesma descrição de relações utilizada pela validação. |
| P0 | POST `S/changes/validate` | Validar lote sobre a versão base da fonte sem gravar, incluindo intenção de exclusão e impactos. Devolver código, severidade, ID e JSON Pointer por erro/aviso. |
| P0 | POST `S/changes` e GET `S/changes/{changeId}` | Aplicar lote de operações `create`, `update` e `delete` com ID idempotente e comparação da base, devolver estado canônico e recibo. A consulta decide se uma requisição com resposta perdida foi aplicada. |
| P0 | POST `S/changes/{changeId}/undo` e `.../redo` | Gravar novos lotes compensatórios sobre a fonte corrente com checagem de conflito. A engine sabe quais operações são reversíveis e devolve novo recibo, ETag e revisão. |
| P1 | GET `S/definitions/{kind}/{id}/usages` e ampliação de `changes/validate` | Retornar usos, herança, dependências e ação permitida de exclusão/substituição para todos os tipos. P0 já precisa de bloqueio autoritativo e diagnóstico útil para referências à carta. |
| P1 | Operações adicionais em `changes` | Autorizar edição dos próprios settings, pacotes, regras e políticas, incluindo IDs e dependências. Introduzir supressão herdada ou renomeação relacional quando o modelo do Core estiver definido. |
| P2 | Listagem de mudanças, drafts compartilhados, revisão de propostas e eventos de autoria | Auditoria e colaboração centralizada, com identidade individual, permissões e armazenamento compartilhado. Push é opcional. Polling condicional resolve a primeira instância compartilhada. |

**Semântica da gravação.** Enviar `If-Match` com ETag forte da visão de autoria, além do `changeId`. A base deve representar as fontes e os pacotes relevantes ao lote, inclusive alterações externas ainda sem publicação. Separar `sourceVersion` de `activeRevision` no recibo. P0 pode serializar por workspace se um pacote for usado em múltiplos settings, e evoluir para versões por documento/recurso quando houver concorrência suficiente. Revalidar a base sob coordenação, compilar com uma visão candidata dos arquivos e validar os settings afetados. Em seguida preparar o runtime e persistir a mudança das fontes com recuperação e publicação coordenadas. Uma substituição atômica por arquivo não dá atomicidade ao lote. Para múltiplos arquivos, preparar versões em staging, registrar o lote e definir recuperação após interrupção e comportamento para leitores externos do Git. Dentro da engine, expor apenas estados concluídos ou recuperação explícita. Aplicação rejeitada por validação preserva a última revisão utilizável e o rascunho na UI. Acertar a ordem de fontes, bundle e ponteiro ativo antes de reutilizar o reload atual. Não executar mudança enquanto o workspace estiver em checkout/merge externo detectado ou com outra instância gravadora sem coordenação.

**Conflitos e diagnósticos.** `412` para falha de `If-Match`, `409` para conflito de operação (como reaproveitamento do ID com payload diferente), `422` para violações de schema/regra. Retornar `ProblemDetails` consistente, revisão/base atual quando pertinente e recibo com arquivos/definições afetados, warnings e estado da ativação. Informar operações de exclusão autorizadas pela engine. Na fatia inicial, restringir exclusão às cartas de propriedade do pacote editável e recusar referências em uso com seus diagnósticos. Para pacotes afetando vários settings, a API deve informar o alcance e validar o conjunto afetado antes de publicar. [RFC 9110, semântica HTTP](https://www.rfc-editor.org/rfc/rfc9110.html) · [RFC 9457, Problem Details](https://www.rfc-editor.org/rfc/rfc9457.html).

**Permissões locais.** Hoje `Admin:Enabled` é desabilitado por padrão, `X-Admin-Key` identifica o acesso e `ToolCapabilities.AdminOperations` pode vetar a operação. Servir os assets e a API na mesma origem em loopback, com um perfil de autoria opt-in e origem explícita. Durante desenvolvimento, usar proxy Vite restrito à instância da engine. Para a fatia, usar autenticação pela engine ou uma sessão efêmera de autoria no servidor. Evitar chave administrativa no bundle, na URL ou no armazenamento persistente do browser. Se a sessão usar cookies, proteger mutações por origem e token de solicitação. A API resolve os arquivos permitidos a partir de IDs e da raiz configurada, com gerência de caminhos confinada ao workspace. Na transição para acesso compartilhado, adicionar HTTPS, identidades e permissões por workspace e operação. [Middleware administrativo](../../src/API/Middleware/AdminKeyMiddleware.cs).

## 5. Fluxo do designer

O registro do Core tem 53 tipos neste checkout. A cobertura completa deve incluir também a configuração do setting e dos pacotes, que são modelos distintos dos artefatos. Mapear os tipos registrados nas áreas abaixo é um ponto de partida para a navegação. A montagem definitiva do catálogo deve vir dos metadados da engine.

| Área proposta | Tipos do registro |
| --- | --- |
| Cartas | cards, card-component-bundles, card-upgrades, card-modifier-items, card-selections, card-zone-systems, condensation-recipes |
| Regras e fluxos | combat-rules, flow-rules, phase-sequences, modes, capability-policies, content-binding-policies, replay-policies, timeline-policies |
| Efeitos e status | actions, modifiers, status-effects, effect-resolution-policies, gambits, powers, keywords |
| Recursos e fórmulas | resources, actor-resource-lifecycle-policies, calculation-pipelines, formulas |
| Entidades | entities, enemies, enemy-pools, companions, races, supports, support-routines |
| Decks e pools | decks, card-pools, deck-composition-policies, deck-modules, deck-compartment-profiles, deck-compartment-policies, loadout-policies, support-loadout-policies, inscriptions |
| Runs e progressão | runs, run-progression-policies, profile-progress-policies, daily-challenges, dialogues, run-events, preparations, shops, relics, boards, zones |

### Navegação e troca de setting

- Barra superior discreta com **Game setting** e o nome selecionado, modo de edição, status e menu de preferências. Sidebar de **Cartas**, **Regras e fluxos**, **Efeitos e status**, **Recursos e fórmulas**, **Entidades**, **Decks e pools**, **Runs e progressão**, **Settings e pacotes**. Áreas sem editor mostram o aviso no corpo, com os subtipos descobertos quando houver metadados.
- Ao escolher outro setting, atualizar URL e contexto imediatamente. Preservar rascunho pendente do setting anterior no histórico da sessão. Renderizar skeleton na lista e no detalhe enquanto o novo contexto é preparado. Cancelar leituras obsoletas, impedir que respostas antigas alterem a tela atual e nunca reutilizar dados de outro setting como placeholder. Uma gravação já enviada continua vinculada ao setting original, com status recuperável ao voltar. Uma página carregada mantém foco e anuncia mudança com status acessível.
- Chaves de cache por instância/workspace, setting, revisão/versão de autoria, tipo e consulta. Carregar rotas e painéis avançados sob demanda. Buscar referências ao abrir o controle, com filtro digitável, estados vazio/erro e paginação. Conservar layout, indicar carregamento com `aria-busy` e respeitar preferência de movimento reduzido. [React lazy](https://react.dev/reference/react/lazy) · [Chaves do TanStack Query](https://tanstack.com/query/latest/docs/framework/react/guides/query-keys) · [Cancelamento do TanStack Query](https://tanstack.com/query/latest/docs/framework/react/guides/query-cancellation).

### Tela de Cartas

Lista com busca e filtros, editor principal em seções, e painel de diagnóstico/diff recolhível. Cobrir `cardId`, raridade, tags, metadata, preços, recompensas de decomposição, bundles de componentes, slots de transformação e componentes ordenados. O editor guiado da fatia concentra-se em identificação, tags, raridade e uma condição ou custo já suportado. Os demais campos da carta ficam acessíveis na área avançada com schema/API, preservando valores e propriedades. Os componentes são polimórficos por `type` (por exemplo, `cost`, `condition`, `effect` e `targeting`); opções e pendências vêm da versão da engine. Para páginas futuras, gerar cobertura inicial por JSON Schema e metadados da engine e adicionar widgets especializados por tipo quando o fluxo pedir. O modo JSON por definição permite inspeção e edição técnica no mesmo contrato de validação. Arquivos de pacote e dados sem schema semântico precisam de metadados explícitos antes de ganhar interface guiada. [Componentes de cartas](../../src/Core/Run/Content/CardComponentDefinition.cs).

Em **Tags**, oferecer busca de strings existentes, inclusão e remoção por teclado e opção **Criar tag e adicionar**. Em uma referência tipada, **Criar novo** abre formulário sobreposto quando a capacidade está ativa. Em seguida seleciona o item e mantém o foco na carta. O lote pode incluir a criação da dependência e a alteração da carta. Se o tipo ainda não possui editor, mostrar link para a página com aviso e disponibilidade de visualização. Oferecer prévia da carta, indicadores de origem/override, diagnóstico clicável, comparação de alterações e confirmação de exclusão com usos reportados. A prévia semântica deve vir da engine e ser marcada com a revisão consultada. Avaliar um endpoint para prévia de definição de carta, pois as rotas de apresentação existentes são orientadas a instâncias de run.

### Salvamento, desfazer e refazer

| Evento | Modo padrão | Hot Reload |
| --- | --- | --- |
| Editar | Alterar rascunho local por ação. Validação remota sem persistir quando solicitada. | Alterar visualmente e enfileirar a ação concluída para aplicar e ativar. Para texto, usar confirmação/saída do campo ou pausa curta após digitar, com composição/IME respeitada. |
| Salvar/aplicar | Enviar um lote por setting quando o designer clicar em **Salvar**. Preservar ações novas feitas durante a requisição. | Serializar comandos por workspace de autoria conforme a política da API. Estados visíveis **Pendente**, **Aplicando**, **Aplicado**, **Falhou** e **Conflito**. |
| Ctrl+Z e Ctrl+Shift+Z | Desfazer/refazer rascunho por ação lógica. Após um **Salvar**, preparar no rascunho a inversão/reaplicação do lote confirmado a partir do recibo da API; **Salvar** confirma. | Antes do envio, editar a fila local. Depois da confirmação, chamar undo/redo do servidor. Se a ação estiver em trânsito, aguardar seu recibo antes da compensação. |
| Falha ou conflito | Preservar rascunho e exibir base anterior, minha edição e versão atual; permitir reaplicar após revisão. | Preservar a intenção e pausar operações que dependam do resultado. Consultar o recibo antes de repetir; atualizar com chave idempotente e base correta. |

Para o modo padrão pós-salvamento, definir uma prévia de compensação sem escrita no mesmo contrato de validação, ou aceitar um `undoOf`/`redoOf` no lote de **Salvar** que a engine resolve. No Hot Reload, as operações diretas podem ser adaptadores desse contrato. Uma nova edição após desfazer invalida a pilha local de refazer daquele contexto. O foco em campos de texto e no editor JSON deve preservar o histórico de digitação e evitar que o atalho seja tratado duas vezes. Disponibilizar os mesmos comandos em botões/menu **Editar**. A primeira fatia mantém as pilhas por setting e modo durante a sessão, com restauração de rascunho local como evolução. Registros do servidor devem sobreviver ao reinício no período definido pela política de retenção. Idioma `pt-BR`/`en`, tema claro/escuro/sistema e último setting são preferências locais. O idioma da interface pode mudar a qualquer momento, mantendo IDs e textos de autoria intactos. Erros devem ter código e parâmetros traduzíveis, com detalhe original para diagnóstico. [Fallbacks do i18next](https://www.i18next.com/principles/fallback).

## 6. Framework e distribuição

| Opção | Benefícios | Custos para a HeroScript |
| --- | --- | --- |
| **Web local, React + TypeScript + Vite** | SPA estática, execução no navegador existente, componentes com carregamento sob demanda, implantação simples na mesma origem, possibilidade de usar o mesmo app com uma engine compartilhada. | Segundo ecossistema para build, navegador precisa da engine em execução e integração nativa limitada. Requer contratos tipados e testes de integração. |
| Web local, Svelte + TypeScript + Vite | Componentes compilados e arquitetura estática equivalente. Alternativa se a equipe já domina Svelte. | Templates e bibliotecas de UI próprios. Avaliar editor JSON e componentes de formulário na fatia caso a equipe prefira Svelte. |
| Web local, Blazor | C# de ponta a ponta, hospedagem alinhada ao ASP.NET e possibilidade de compartilhar DTOs. | WebAssembly baixa runtime e assemblies, enquanto Interactive Server requer conexão SignalR. O ganho depende da experiência da equipe e bibliotecas de interação complexa. |
| Desktop leve, Tauri | Reaproveita a UI web, oferece integrações de desktop e usa WebView2 no Windows. | Acrescenta distribuição, atualização, testes de WebViews e ferramentas Rust para a camada nativa. A API da engine continua necessária. |
| Desktop, Electron | Navegador consistente por distribuição e integrações nativas maduras. | Empacota Chromium e Node.js, acrescentando tamanho de distribuição e atualização do runtime. |

Escolha recomendada **React + TypeScript + Vite**, TanStack Query para estado remoto e i18next para idiomas. Usar um estado local pequeno para rascunhos e histórico e um cliente HTTP tipado a partir do contrato de autoria. Hospedar a compilação estática em `/editor/` pelo ASP.NET Core, com fallback de deep links restrito a esse caminho e assets com hash. Usar proxy Vite durante desenvolvimento. A engine já fornece a camada HTTP e a aplicação funciona sem SSR ou server JavaScript adicional na distribuição. Para deixar aberta a possibilidade de Tauri, manter todas as operações no cliente HTTP e as integrações com a plataforma em torno da abertura da ferramenta. A comparação está baseada nas arquiteturas documentadas, sem medição comparativa de memória ou tamanho no workspace.

Fontes oficiais: [Vite com backends](https://vite.dev/guide/backend-integration) · [Assets no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0) · [Svelte](https://svelte.dev/docs/svelte/overview) · [Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models?view=aspnetcore-10.0) · [Tauri e WebViews](https://v2.tauri.app/concept/process-model/) · [Electron](https://www.electronjs.org/docs/latest/).

## 7. Git e evolução para colaboração

- **Primeira entrega.** Uma engine por checkout ou worktree, com porta e store de runtime separados. Git continua sendo a ferramenta de branch, commits e PRs. O gravador deve produzir JSON estável, preservar elementos desconhecidos, minimizar mudanças de formatação e manter diffs pequenos. Mostrar origem da definição e os arquivos alterados no recibo. Rodar compilação/validação de settings afetados na CI e desejar arquivos menores por área quando os conflitos reais justificarem migrar catálogos monolíticos. [Git worktree](https://git-scm.com/docs/git-worktree).
- **Instância compartilhada assíncrona.** Reaproveitar o contrato HTTP, as precondições, os IDs de operação e o histórico. Adicionar workspace lógico, autenticação por pessoa, autorização e auditoria central. Etags por recurso e atualização por polling permitem editar itens distintos. Confirmação por versão ainda protege recursos e dependências compartilhados. Adicionar merge assistido por campo antes de considerar edição simultânea do mesmo documento.
- **Propostas revisáveis.** Um serviço de autoria futuro pode associar mudanças a drafts duráveis, revisão/aprovação e um branch/worktree gerenciado, gerando um PR. O cliente pode continuar usando os mesmos comandos e distinguir estágio de revisão no recibo. Presença em tempo real e edição simultânea no mesmo campo ficam para quando houver demanda. Evitar vincular a semântica de domínio a comandos Git ou CRDTs na primeira fatia.

## 8. Primeira fatia, sequência e critérios de saída

| Etapa | Entrega verificável |
| --- | --- |
| 1. Contrato e cenário | Escolher dois settings reais com cartas distintas, como `uno-clash` e `circular-table`, em workspace isolado. Fixar fluxo de editar carta própria e cenário de carta herdada para diagnóstico. Versionar o inventário e o contrato P0 de autoria. |
| 2. Estrutura navegável | Menubar com seletor discreto, preferências de idioma/tema, páginas anunciadas, skeleton, lazy loading, diagnóstico de conexão e troca de setting com rascunho pendente. Conectar catálogo e leitura real desde esta etapa. |
| 3. Autoria vertical | Listar/buscar, abrir, criar, alterar e excluir carta própria. Adicionar tag existente ou criar no fluxo. Editar um custo ou condição, validar pela engine, mostrar qualquer outro campo em área avançada e persistir na fonte com diff revisável. |
| 4. Aplicação e recuperação | Exercitar **Salvar** e **Hot Reload**, desfazer/refazer nos dois modos, falha de validação, conflito de base, resposta perdida e recuperação após reinício. Checar consistência do bundle com o JSON e isolamento de runs antigas. |
| 5. Sessão com game designers | Pedir para encontrar uma carta, mudar regra/tag, desfazer/refazer, trocar de setting com rascunho aberto e localizar o motivo de uma falha. Observar erros de navegação e ajustar ordem dos campos, feedback e vocabulário antes de ampliar os tipos. |
| 6. Expansão | Próximas telas após a fatia: bundles e pools, recursos/efeitos/status, regras e fases, demais tipos, composição de settings e publicação/preview. Medir cobertura do registro por tipo e campo, inclusive tipos novos descobertos. |

A fatia passa quando os sete casos seguintes forem reproduzíveis:

1. Trocar de setting sob latência e respostas fora de ordem mostra skeleton e depois apenas dados do setting selecionado. A volta recupera o rascunho anterior. Páginas pendentes exibem aviso em ambos os idiomas.
2. Uma carta pode ser criada, alterada e excluída via API. O diff do Git mostra os arquivos esperados. Excluir uma carta em uso dá diagnóstico e mantém o conteúdo.
3. O designer seleciona uma tag existente ou digita uma nova no mesmo controle, e a engine retorna o resultado correto em uma releitura. A UI preserva todos os demais campos.
4. No modo padrão, o JSON e a revisão ativa mudam apenas após **Salvar** bem-sucedido. No Hot Reload, a revisão atualiza após cada ação concluída e a interface reflete o recibo.
5. Os dois atalhos e os botões funcionam em sequência, inclusive para criação e exclusão. Um conflito posterior impede desfazer por cima da alteração externa.
6. Enviar um candidato inválido, perder a resposta da requisição ou interromper a gravação produz um estado recuperável. Reiniciar a engine recompõe a revisão a partir das fontes confirmadas. Uma run existente mantém o conteúdo fixado.
7. Com teclado e leitor de tela, os designers conseguem trocar setting, encontrar a carta, editar, identificar erro no campo e salvar, em português e inglês.

Medições a registrar na fatia: tempo da shell até a primeira lista, da troca de setting ao primeiro dado útil, da ação ao recibo de Hot Reload (p50/p95, frio/quente) e tamanho carregado por rota. Usar settings reais e rede com atraso simulado antes de fixar metas de desempenho.

## Anexo — Inventário HTTP

Gerado a partir do `paths` retornado pelo `GET /openapi/v1.json` do host isolado. Cada entrada é uma operação.


- `GET /api/v1/admin/content/drafts/{draftId}`
- `POST /api/v1/admin/content/drafts/{draftId}/publish`
- `POST /api/v1/admin/content/drafts/{draftId}/validate`
- `POST /api/v1/admin/content/reload`
- `POST /api/v1/admin/diagnostics/cache/invalidate/{cacheName}`
- `POST /api/v1/admin/diagnostics/cache/invalidate/{cacheName}/key`
- `GET /api/v1/admin/diagnostics/cache/stats`
- `GET /api/v1/admin/diagnostics/cache/stats/{cacheName}`
- `GET /api/v1/admin/diagnostics/health/cache`
- `GET /api/v1/admin/settings`
- `GET /api/v1/admin/settings/{settingId}`
- `POST /api/v1/admin/settings/{settingId}/drafts`
- `POST /api/v1/admin/settings/{settingId}/validate`
- `GET /api/v1/admin/telemetry`
- `DELETE /api/v1/admin/telemetry`
- `GET /api/v1/admin/telemetry/{eventId}`
- `GET /api/v1/admin/telemetry/categories`
- `GET /api/v1/admin/telemetry/combats/{combatId}`
- `GET /api/v1/admin/telemetry/combats/{combatId}/stream`
- `GET /api/v1/admin/telemetry/severities`
- `GET /api/v1/admin/telemetry/stream`
- `GET /api/v1/capabilities`
- `GET /api/v1/challenges/daily/current`
- `POST /api/v1/challenges/daily/current/attempts`
- `GET /api/v1/challenges/daily/current/leaderboard`
- `POST /api/v1/challenges/daily/current/submissions`
- `GET /api/v1/combats/{combatId}`
- `GET /api/v1/combats/{combatId}/actors/{actorId}/attributes`
- `GET /api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation`
- `GET /api/v1/combats/{combatId}/cards/evaluations`
- `POST /api/v1/combats/{combatId}/cards/payment-preview`
- `POST /api/v1/combats/{combatId}/commands`
- `GET /api/v1/combats/{combatId}/events`
- `GET /api/v1/combats/{combatId}/events/stream`
- `GET /api/v1/combats/{combatId}/history`
- `GET /api/v1/combats/{combatId}/journal`
- `GET /api/v1/combats/{combatId}/legal-actions`
- `GET /api/v1/combats/{combatId}/resolutions/{commandId}`
- `GET /api/v1/combats/{combatId}/timeline`
- `POST /api/v1/combats/{combatId}/timeline/{sequence}/branches`
- `GET /api/v1/combats/{combatId}/timeline/{sequence}/state`
- `POST /api/v1/combats/{combatId}/verify`
- `GET /api/v1/content/{kind}`
- `GET /api/v1/content/{kind}/{definitionId}`
- `GET /api/v1/content/{settingId}/revisions/{revision}/gameplay-events/catalog`
- `GET /api/v1/content/{settingId}/revisions/{revision}/gameplay-responses/schema`
- `GET /api/v1/content/revisions`
- `GET /api/v1/content/revisions/{revision}`
- `GET /api/v1/content/settings`
- `POST /api/v1/content/validate`
- `GET /api/v1/health/live`
- `GET /api/v1/health/ready`
- `GET /api/v1/operations`
- `GET /api/v1/operations/{name}`
- `GET /api/v1/operations/categories`
- `GET /api/v1/profiles/{playerId}`
- `GET /api/v1/profiles/{playerId}/achievements`
- `GET /api/v1/profiles/{playerId}/runs`
- `GET /api/v1/profiles/{playerId}/stats`
- `GET /api/v1/profiles/{playerId}/unlocks`
- `POST /api/v1/runs`
- `GET /api/v1/runs`
- `GET /api/v1/runs/{runId}`
- `GET /api/v1/runs/{runId}/available-commands`
- `GET /api/v1/runs/{runId}/branch-tree`
- `POST /api/v1/runs/{runId}/branches`
- `GET /api/v1/runs/{runId}/branches`
- `GET /api/v1/runs/{runId}/capabilities`
- `GET /api/v1/runs/{runId}/card-modifier-items`
- `GET /api/v1/runs/{runId}/card-modifier-items/{itemInstanceId}/preview/{cardInstanceId}`
- `GET /api/v1/runs/{runId}/card-selections`
- `GET /api/v1/runs/{runId}/card-selections/{selectionInstanceId}`
- `GET /api/v1/runs/{runId}/card-zones`
- `GET /api/v1/runs/{runId}/cards/{cardInstanceId}`
- `GET /api/v1/runs/{runId}/cards/{cardInstanceId}/transformation-preview`
- `GET /api/v1/runs/{runId}/cards/{cardInstanceId}/upgrade-options`
- `GET /api/v1/runs/{runId}/cards/presentations`
- `GET /api/v1/runs/{runId}/character-attributes`
- `POST /api/v1/runs/{runId}/commands`
- `GET /api/v1/runs/{runId}/commits`
- `GET /api/v1/runs/{runId}/commits/{sequence}`
- `GET /api/v1/runs/{runId}/commits/{sequence}/gameplay-events`
- `GET /api/v1/runs/{runId}/commits/{sequence}/presentation`
- `GET /api/v1/runs/{runId}/content/activation-preview`
- `GET /api/v1/runs/{runId}/deck-compartments`
- `POST /api/v1/runs/{runId}/deck-compartments/preview`
- `GET /api/v1/runs/{runId}/deck-loadout`
- `POST /api/v1/runs/{runId}/deck-loadout/preview`
- `GET /api/v1/runs/{runId}/encounters/current`
- `GET /api/v1/runs/{runId}/events`
- `GET /api/v1/runs/{runId}/events/stream`
- `GET /api/v1/runs/{runId}/journal`
- `GET /api/v1/runs/{runId}/loadout`
- `POST /api/v1/runs/{runId}/loadout/preview`
- `GET /api/v1/runs/{runId}/map`
- `GET /api/v1/runs/{runId}/preparations`
- `GET /api/v1/runs/{runId}/preparations/{preparationInstanceId}`
- `GET /api/v1/runs/{runId}/relics`
- `GET /api/v1/runs/{runId}/session`
- `GET /api/v1/runs/{runId}/shops`
- `GET /api/v1/runs/{runId}/shops/{shopInstanceId}`
- `GET /api/v1/runs/{runId}/support-loadout`
- `POST /api/v1/runs/{runId}/support-loadout/preview`
- `GET /api/v1/runs/{runId}/timeline`
- `POST /api/v1/runs/{runId}/verify`
- `POST /api/v1/sandbox/runs`
- `GET /api/v1/sandbox/runs/{runId}/scenario`
- `GET /api/v1/sandbox/runs/{runId}/snapshot`
- `POST /api/v1/sandbox/scenarios/validate`
- `POST /api/v1/simulations`
- `GET /api/v1/simulations/{simulationId}`
- `GET /api/v1/simulations/{simulationId}/result`
- `GET /api/v1/simulations/formulas`
- `GET /api/v1/simulations/formulas/{name}`
- `POST /api/v1/simulations/formulas/evaluate`
- `POST /api/v1/simulations/math/expression/evaluate`
- `GET /api/v1/version`
