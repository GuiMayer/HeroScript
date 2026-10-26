# Compartimentos de construção do baralho — arquitetura e plano

Data: 2026-10-26. Estado: proposta de implementação; nenhuma etapa implementada por este documento.

## 1. Objetivo e decisões

Criar compartimentos definidos pelo personagem para alocar cartas durante a montagem da build, fora do combate. Cada compartimento concede um ou mais componentes genéricos ao container efetivo da carta, por composição declarativa. Afinidade de fogo é apenas um exemplo de conteúdo, não um tipo especial reconhecido pelo sistema. A carta continua sendo a mesma instância ao circular pelas zonas de compra, mão, descarte e exílio.

Decisões confirmadas pelo autor:

- Alocação ocorre fora do combate.
- Compartimentos são configuráveis por personagem.
- Esta é outra mecânica: não estender, reutilizar como estado, nem atribuir essa responsabilidade a `CardZoneDefinition`.
- Regras são interpretadas pela engine a partir de conteúdo; Godot apresenta e envia comandos REST.
- Preservar determinismo, imutabilidade e separação entre sistemas.
- Carta é um container de componentes. Compartimentos podem conceder efeitos, custos, condições, targeting, triggers e demais tipos suportados pelo contrato canônico; não são limitados a afinidades ou efeitos de dano.
- Processadores consomem os componentes resultantes independentemente da fonte. Proveniência serve para composição, validação, explicação e diagnóstico, não para criar execução alternativa por compartimento.

Propostas iniciais deste plano, não decisões previamente confirmadas:

- Uma carta pode ocupar no máximo um compartimento; sua ausência no mapa de alocações significa neutra.
- Capacidade é quantidade de cartas, não quantidade de acertos/componentes. Não introduzir pesos neste ciclo.
- Neutras continuam no baralho e participam do mesmo ciclo de compra.
- Não criar compras separadas, nova energia, bônus de compra ou movimento durante combate.
- Reorganização gratuita nas janelas externas autorizadas pelo modo. O mecanismo admite restringir a preparação, mas não inventa custo econômico.
- Perfil inicial demonstra concessões heterogêneas, com capacidade pequena configurada em JSON, e cartas neutras. Incluir ao menos uma concessão que não seja afinidade. Quantidades são parâmetros de playtest.

## 2. Diagnóstico da base atual

| Ponto inspecionado | O que existe | Implicação |
|---|---|---|
| `src/Core/CardZones/CardZoneDefinitions.cs` | Zonas, capacidade, visibilidade, ordenação e fluxos | São circulação/localização de cartas; não representam associação persistente de build. Permanecem fora desta mudança. |
| `src/Core/Run/CardInstanceState.cs` | Identidade, dono, lifetime e ledger de upgrades | Usar `CardInstanceId` para alocar; não clonar carta nem escrever uma concessão temporária em `Upgrades`. |
| `src/Core/Run/Content/EffectiveCardResolver.cs` | Resolve definição, ledger, patches e gramática de composição | Reaproveitar a composição, acrescentando fonte explícita externa; atualmente recebe apenas definição e instância. |
| `src/Core/Run/Content/CardCompositionGrammar.cs` | Regras e bundles compõem componentes/efeitos conforme seus contratos atuais | Reaproveitar a infraestrutura, auditando os limites de tipos suportados; não presumir que bundles de efeitos já aceitam custos, condições ou outros componentes. Compartimento não ganha executor próprio. |
| `src/Core/Run/Loadouts/LoadoutChangePlanner.cs` | Planejamento candidato, revisão e validação por ator para inscrições | Referência de padrão transacional, não implementação a copiar ou estado a compartilhar. |
| `src/API/Controllers/LoadoutController.cs` | Fronteira REST das inscrições | Criar contrato próprio; não adicionar compartimentos ao inventário de inscrições. |
| `docs/game-design/CORE_GAME_DESIGN.md` e `docs/content/volatile-core-setting.md` | Afinidade, comportamento, scaling e exemplos Ember/Frost | Exemplos úteis para conteúdo, mas insuficientes para certificar concessões genéricas; não substituir automaticamente afinidades existentes em todo conteúdo. |

Não há hoje contrato completo de perfil por personagem, alocação persistente e contribuição de compartimento à carta. Não basta adicionar campos JSON às zonas.

## 3. Fronteiras e vocabulário

Nome técnico proposto: `DeckCompartments`, sob `Core.Run.DeckCompartments`.

| Domínio | Responsabilidade | Não faz |
|---|---|---|
| Perfil de compartimentos | Define compartimentos, capacidades e concessões genéricas de componentes do personagem | Não define onde a carta está na circulação nem cria categorias hardcoded de afinidade. |
| Estado de alocação | Relaciona ator, instância de carta e compartimento | Não duplica coleção, inventário ou carta. |
| Zonas de cartas | Compra, mão, descarte, exílio e fluxos | Não recebem a responsabilidade de conceder componentes por serem chamadas draw/hand. |
| Upgrade/modificador de carta | Transformação própria da instância | Não armazena instalação/remoção de compartimento. |
| Inscrição | Equipamento que altera atributos do personagem | Não contém cartas. |
| Relíquia | Regra excepcional | Não é necessária para a alocação funcionar. |
| Pipeline de cálculo | Calcula magnitudes com influências apropriadas | Não conhece capacidade, drag-and-drop ou compartimentos. |

Fluxo conceitual: componentes da definição + transformações próprias + componentes concedidos pelo compartimento → composição e validação canônicas → carta efetiva → consumidores comuns de custo, condição, alvo, trigger e efeito → cálculo/aplicação quando cabíveis.

Concessão é uma camada derivada: não modifica a definição-base, não clona a carta e não escreve no ledger de upgrades. Remover a alocação recompõe a carta sem essa camada, em vez de executar patches inversos sobre componentes compartilhados. A mecânica conhece containers, componentes tipados, referências e contratos de compatibilidade; não interpreta nomes como `fire`, `dot` ou `heal`.

Afinidade (`fire`), comportamento (`dot`) e ação (`heal`) não são sinônimos. Tags são identificadores semânticos; adicionar uma tag não cria um efeito por magia. Tags concedidas entram pelos mecanismos canônicos de metadados/composição, sem inventar um tipo de componente se o modelo atual as representa fora da lista de componentes. Regras declarativas podem compor comportamentos a partir delas; o sistema de compartimentos não executa essa interpretação por conta própria.

## 4. Conteúdo e vínculo ao personagem

Novos tipos de conteúdo revisionado:

- `deck-compartment-profiles`: composição do personagem, IDs locais estáveis, capacidades, concessões, compatibilidade e apresentação.
- `deck-compartment-policies`: habilitação/janelas externas e políticas de ciclo de vida do modo.

Concessões referenciam bundles genéricos de componentes revisionados. Reutilizar o formato canônico existente se ele suportar o contrato; se hoje estiver restrito a efeitos, generalizá-lo de forma compartilhada e tipada antes de integrar compartimentos. Não criar uma gramática paralela exclusiva desta mecânica. IDs e namespaces distinguem componentes adicionados por fontes diferentes.

Vincular o perfil à definição do personagem por um component tipado, por exemplo `DeckCompartmentProfileComponentDefinition`, usando o mecanismo existente de components de entidade. Evitar tabela por nome de herói ou perfil global do modo aplicado indistintamente a todos os personagens.

O modo autoriza a mecânica e as janelas; o personagem escolhe a estrutura. Personagem sem component não possui compartimentos. Modo sem política mantém comportamento anterior, sem componentes concedidos implicitamente.

Exemplo conceitual (contrato final depende dos serializers e compiler existentes):

```json
{
  "profileId": "wanderer.deck_build",
  "compartments": [
    {
      "compartmentId": "utility",
      "capacity": 3,
      "grant": {
        "componentBundleIds": ["grant.extra_cost_and_resource_effect"]
      },
      "presentation": { "nameKey": "compartment.utility" }
    }
  ]
}
```

O bundle do exemplo pode conter um componente de custo adicional e um componente de efeito de recuperação de recurso, usando os mesmos schemas de uma carta autorada. Outro bundle pode conceder apenas condição, targeting ou trigger, respeitando cardinalidade e compatibilidade. São exemplos, não novas regras obrigatórias.

Não criar um segundo formato livre de scripts/efeitos. As referências devem fechar componentes/regras/bundles na revisão de conteúdo com o mesmo compilador usado pelas cartas. Tags de origem devem possuir proveniência, sem remover tags homônimas da base quando a carta for desalocada. A API de alocação aceita IDs de cartas/compartimentos; autoria de novos bundles pertence ao fluxo autorizado de conteúdo, não ao payload de equipar.

## 5. Estado e propriedade

Novo estado imutável opcional na run: `DeckCompartmentState`, com entradas por identidade persistente de ator.

- Cada ator registra o perfil resolvido e mapa `CardInstanceId → CompartmentId`.
- Revisão do conteúdo é a revisão efetiva da run; não buscar perfil mais recente silenciosamente.
- Elegibilidade exige carta existente, pertencente ao baralho/ator permitido e com lifetime persistente autorizado.
- Cartas de encontro e cópias temporárias não se tornam alocáveis automaticamente.
- Duplicatas da mesma definição são instâncias independentes.
- A associação não muda quando a carta é comprada, descartada, exilada temporariamente ou devolvida ao baralho.
- Destruição definitiva remove a associação na mesma transação que remove a carta. Exílio não significa destruição definitiva.
- Aquisição cria carta neutra; uma alocação inicial explícita é possível na inicialização autoritativa da run.
- Cópias começam neutras neste primeiro ciclo; futura herança precisa de política explícita, não de cópia incidental do objeto.
- Troca de personagem não transfere associação nem converte `$run` em proprietário de outro ator. Adaptar explicitamente o vínculo de propriedade existente, sem reinterpretar IDs por suposição.

Neutra é ausência de concessão, não uma zona extra e não um compartimento obrigatório com nome hardcoded.

## 6. Transações, autorização e concorrência

Planejador puro recebe snapshot, revisão resolvida, ator autorizado e distribuição final solicitada. Retorna candidato válido + diff ou falha sem mutação.

Validar a distribuição inteira antes de aplicar:

1. Run ativa e modo habilitado.
2. Ator pertence à run; na UI normal é somente o personagem atual.
3. Janela externa permitida. Combate ativo, resolução pendente ou atividade incompatível bloqueiam alteração, inclusive por chamada REST direta.
4. Cartas existem e são elegíveis; nenhum ID duplicado ou alheio.
5. Compartimentos existem no perfil do ator e respeitam capacidade.
6. Composição final de cada carta afetada é válida.
7. Versão/sequence da run ainda corresponde à versão esperada pelo comando.

O pedido representa a distribuição final, permitindo trocar duas cartas entre compartimentos cheios atomicamente. Não executar primeiro uma remoção e depois uma instalação em comandos separados.

Preview é read-only: não grava draft, não consome RNG, não incrementa sequence, não publica evento de gameplay. Commit usa o mesmo planejador, revalida no snapshot atual e segue o dispatcher/journal canônicos. A API não aceita componentes ou efeitos arbitrários enviados pelo cliente.

## 7. Composição genérica, compatibilidade e proveniência

Separar fontes na resolução: base, transformações próprias e contribuição de compartimento. Não converter concessão em upgrade permanente ou fabricar `TransformationId` para disfarçar sua origem.

Refatorar a entrada da composição para um contexto tipado e explícito, preservando o caminho sem contexto. Primeiro combinar os componentes estruturais próprios e concedidos, validar bindings/cardinalidade e depois expandir regras declarativas em ordem explícita e estável. Preservar a semântica das composições existentes. Não aplicar um segundo pós-processador sobre efeitos já expandidos nem exigir que toda concessão seja uma regra AfterImpact.

Política estrutural inicial: adicionar componentes, nunca sobrescrever silenciosamente os próprios da carta. Componentes repetíveis coexistem conforme o contrato canônico. Componentes singleton incompatíveis, bindings ausentes/cíclicos e referências ambíguas rejeitam a composição com diagnóstico. O compilador comum, e não o nome do compartimento, define essas restrições. Não fundir custos/condições/targeting por heurística; usar sua semântica canônica e, onde não houver uma, explicitar e testar o contrato compartilhado antes de oferecer essa concessão.

Compatibilidade de conteúdo pode declarar exclusões/capacidades semânticas, incluindo afinidade exclusiva em um setting. Isso não é uma regra estrutural da engine nem uma restrição universal a uma afinidade. Uma carta com afinidade incompatível só é rejeitada se o conteúdo/configuração declarar essa incompatibilidade. Não consumir slot de modifier próprio silenciosamente.

Namespaces determinísticos separam IDs locais de bundles e fontes; bindings externos precisam de seletores/aliases explícitos, nunca IDs concatenados na Godot. Cada componente/rule trace identifica fonte, perfil, compartimento, grant/bundle e revisão sem fabricar `TransformationId`. A ordem vem de prioridade configurada e desempate estável por IDs, não da ordem de carregamento dos arquivos. Validar dependências também ao retirar a concessão; uma transformação que dependa dela pode tornar a realocação inválida, sem apagar o modificador automaticamente.

O contexto resolve componentes concedidos, metadados, regras fechadas, identidade do perfil/compartimento e proveniência. Essa contribuição entra no fingerprint efetivo. O fingerprint deve mudar ao realocar, mesmo que o ID da carta não mude. Não exigir que o executor conheça compartimentos: ele recebe o mesmo tipo de componente efetivo que receberia de uma carta autorada.

Auditar todos os consumidores do resolver: legalidade, custos, condições, targeting, triggers, execução, metadados por tags, seleção por tags nas zonas, preview, catálogo contextual, recompensas, upgrades, tooltip e simulação. Catálogo sem ator mostra base; projeção contextual apresenta contribuição explicitamente. Testar ações secundárias, status e condensação sem introduzir execução especial pelo nome da afinidade. Componentes concedidos não podem valer apenas no preview ou apenas depois de pagar o custo.

## 8. Persistência, replay e hotreload

- Alocação integra snapshot, checkpoints, hashes, branches e reconstrução da run.
- Comando registra identidade, distribuição normalizada e revisão pelos mecanismos canônicos; evento de mudança é consequência, não uma segunda autoridade.
- Ordem de serialização/enumeração normalizada por IDs; não usar relógio, GUID aleatório ou ordem de Dictionary para resolver conflitos.
- Branch preserva associação do snapshot de origem e evolui independentemente.
- Saves anteriores sem estado mantêm cartas neutras e gameplay anterior. Nenhuma alocação automática em heróis antigos sem uma migração explícita do conteúdo.
- Revisões pinadas não recebem perfil novo só porque a API reiniciou.
- Hotreload só nas janelas externas permitidas. Se perfil/capacidade/regra nova invalidar alocação, rejeitar a migração e apresentar diagnóstico; não desalocar ou apagar cartas silenciosamente.
- Caso o ator/perfil seja substituído por fluxo autorizado, exigir plano explícito de reconciliação antes do commit.

## 9. API e projeções propostas

Rotas sob o prefixo canônico existente `/api/v1`:

| Método e rota relativa | Finalidade |
|---|---|
| `GET /runs/{runId}/deck-compartments` | Perfil, distribuição, capacidades, cartas elegíveis e permissão/motivo de bloqueio para o personagem atual. |
| `POST /runs/{runId}/deck-compartments/preview` | Avaliar distribuição final sem gravar; devolver diff e cartas efetivas antes/depois. |
| `POST /runs/{runId}/deck-compartments` | Submeter distribuição final pelo pipeline canônico de comandos. |

Não duplicar um command handler caso o projeto publique comandos apenas por endpoint genérico: a fachada acima, se adotada, delega ao mesmo handler. Definir isso na etapa de contratos após conferir convenções vigentes.

Contrato inclui run version esperada e identidade de comando/idempotência conforme protocolo atual. Mapeamento consistente de erros: indisponível/janela, conflito de versão, conteúdo incompatível e carta inexistente. Não devolver sucesso parcial.

Projeção traz descrição útil da concessão, `used/capacity`, conflitos por carta, origem de componentes e versão usada no preview. Diff mostra componentes/metadados acrescentados ou retirados e mudanças em custo, condições, alvos e efeitos, não apenas números de dano. Atualizar OpenAPI, exemplos, documentação e capability discovery. Não expor um GET com efeitos colaterais para inicializar estado.

## 10. Godot

Cena/painel próprio de montagem do baralho, separado do laboratório e do loadout de inscrições. Compartilhar componentes visuais de carta existentes.

- Coluna/área de cartas neutras e compartimentos nomeados com capacidade.
- Drag-and-drop e alternativa acessível por clique/teclado/controle.
- Instâncias repetidas distinguíveis sem depender apenas do nome.
- Card face mostra a carta efetiva, incluindo custos/condições relevantes; tooltip informa componentes concedidos pelo compartimento separados dos modificadores próprios. Não fixar ícones/cores de afinidade como único modo de representar uma concessão.
- Troca/reorganização usa draft local e preview autoritativo debounced; confirmar aplica uma transação. Não confundir draft com estado já equipado.
- Cancelar descarta draft; reabrir carrega estado real da engine.
- Sem botões de refresh, seletor de personagem ou exemplos offline dentro da run.
- Respostas antigas de preview não substituem draft mais recente; somente uma gravação em andamento. Conflito atualiza a visão e pede revisão sem reaplicar draft automaticamente.
- Indicar claramente alterações pendentes. Política de navegação ao sair deve evitar perda silenciosa.
- Durante combate, eventual visualização é somente leitura; engine continua sendo a autoridade do bloqueio.

## 11. Ordem sequencial de implementação

### Etapa 1 — Contratos e inventário de integrações

Mapear consumidores de `IEffectiveCardResolver`, criação/remoção/cópia de cartas, propriedade `$run`, lifecycle do personagem e comandos atuais. Inventariar todos os tipos canônicos de componente e limites reais dos bundles atuais. Fechar nomes, cardinalidade, bindings, ordem, política aditiva sem sobrescrita e janelas externas. Produzir matriz de consumidores e ADR com exemplos válidos/inválidos.

Aceite: modelo não depende de `CardZoneDefinition`; nenhum schema exige afinidade; todos os tipos de componente têm estratégia explícita de concessão/validação e nenhum limite atual fica oculto como se já fosse suportado. Nenhuma dúvida de propriedade fica resolvida por heurística. Commit de documentação/contratos.

### Etapa 2 — Conteúdo e perfil por personagem

Implementar definitions, component de vínculo, serialização, registro de kinds, package compiler e validação de referências. Generalizar/reutilizar fechamento de bundles para componentes canônicos, incluindo custo, condição, targeting, trigger e efeito; manter validadores tipados compartilhados. Limitar tamanho de perfis, capacidades, componentes, tags e expansão pelos budgets existentes.

Aceite: dois personagens podem ter estruturas distintas; modo desabilitado e personagem sem perfil mantêm funcionamento anterior; referências inválidas impedem publicação. Commit próprio.

### Etapa 3 — Estado e transições puras

Criar estado opcional por ator, validação de distribuição, inicialização explícita e planner atômico. Cobrir swap em capacidade cheia, neutras, IDs repetidos, ownership, fora de janela e remoção definitiva.

Aceite: qualquer falha conserva hash original; planner não consome RNG; zonas de circulação não são alteradas. Commit próprio.

### Etapa 4 — Composição canônica e proveniência

Adicionar fontes externas tipadas ao contexto de resolução; integrar compartimentos à gramática existente, ao fingerprint e ao trace. Preservar o resultado exato do caminho sem compartimentos.

Aceite: alocar/desalocar muda somente a contribuição dessa fonte; modificadores próprios sobrevivem; custo, condição, targeting, trigger e efeito concedidos são indistinguíveis para seus consumidores dos equivalentes autorados na carta. Afinidade + multihit + condensação + cascade não duplicam aplicação. Conflitos de cardinalidade e dependências são rejeitados. Commit próprio.

### Etapa 5 — Integração de todos os consumidores

Encaminhar contexto autoritativo em execução, legalidade, pagamento, condições, alvos, triggers, seleção por tags, projeções e simulações. Resultado efetivo é único, não recomposto na Godot. Revisar recompensas/upgrades para não validar só definição base.

Aceite: preview e execução concordam; uma carta conserva os componentes concedidos na compra, mão, descarte, reciclagem e exílio; catálogo sem contexto não inventa concessão. Commit próprio.

### Etapa 6 — Comandos, lifecycle e persistência

Registrar comando e transação pelo pipeline existente; integrar snapshot, serialização, replay, branches, aquisições/destruições e reconstrução. Bloquear alteração em combate e resultado pendente. Implementar validação de migração/hotreload.

Aceite: comando repetido é idempotente pelo protocolo; stale version não grava; save antigo não muda; replay reproduz hashes; branch isolada. Commit próprio.

### Etapa 7 — REST e documentação

Entregar queries, preview, commit, capacidades/motivos, exemplos e OpenAPI. Testar autorização entre runs/atores e números JSON vindos da Godot; nenhuma conversão silenciosa de IDs/capacidades.

Aceite: mesmo planner em preview/commit; endpoints não mutam via GET e não permitem payload de regra arbitrária. Commit próprio.

### Etapa 8 — Conteúdo demonstrativo isolado

Perfis de dois personagens com estruturas diferentes e concessões heterogêneas: um bundle de efeito adicional; outro de custo adicional + condição + efeito compatíveis; fixture adicional de targeting/trigger com composição válida. Afinidade Ember/Frost pode ser um exemplo opcional, nunca o único teste. Ativar em modo de demonstração explícito; não alterar todas as cartas/afinidades do Ascendant de uma vez.

Aceite: pacotes compilam; cartografia de compatibilidade é legível; jogo sem a mecânica continua igual. Commit próprio.

### Etapa 9 — Laboratório com engine real

Montar cena isolada usando mesmos cards e gateway; laboratório offline, se necessário, fica explicitamente separado. Validar antes/depois, conflitos, capacidade, drag/drop, acessibilidade e latência de preview.

Aceite: duas instâncias iguais podem receber componentes distintos; custo, condição e efeito são legíveis sem depender de tag elemental; cancelamento não persiste; visual e resultado REST concordam. Commit próprio.

### Etapa 10 — Integração na montagem da build da run

Adicionar acesso na preparação/janelas autorizadas, carregar somente personagem atual e persistir via comandos. Integrar retorno ao mapa e reentrada sem draft/seleção vazados. Não reorganizar o layout de combate como efeito colateral.

Aceite: fechar/reabrir, trocar setting e continuar run mostram os estados corretos e independentes. Commit próprio.

### Etapa 11 — Performance, certificação e documentação final

Cache derivado com chave de revisão + fingerprint da carta + contribuição relevante, bounded e invalidado por mudança real. Não limpar toda a coleção em hover. Medir baseline e cenário com deck maior; não prometer duração total de um comando com base apenas no resolver.

Executar jornada REST real criar run → alocar → combate → comprar/usar/descartar → concluir → realocar → salvar/carregar → replay/branch. Comparar resultados em execuções repetidas com mesmos inputs. Atualizar guia de conteúdo, guia Godot e relatório de validação.

Aceite: nenhum executor alternativo; nenhuma regressão no setting desabilitado; orçamento de resposta observado e documentado, buscando ações/preview abaixo de 1 segundo no ambiente de referência. Commit próprio.

## 12. Matriz mínima de regressão

| Grupo | Casos obrigatórios |
|---|---|
| Conteúdo | Perfil ausente, diferentes por personagem, IDs duplicados, capacidade inválida, componente/regra/bundle ausente, expansão excessiva, ausência de afinidade não impede concessão. |
| Transação | Alocar, realocar, neutralizar, swap cheio, duplicata, carta alheia, stale version, falha sem mutação, distribuição equivalente em outra ordem. |
| Composição | Efeito, custo, condição, targeting e trigger concedidos; singleton incompatível; namespaces e bindings; ordem determinística; mods preservados; retirar concessão sem retirar tag/componente base; dependência inválida na retirada; filhos não duplicados. |
| Agnosticismo | Mesmo container final autorado diretamente e composto por concessão produz os mesmos resultados semânticos e consumo de RNG; diferenças de proveniência/fingerprint são esperadas. Nomes de compartimento/tag não alteram regras por si só. |
| Circulação | Compra/mão/descarte/reciclagem/exílio mantêm associação e fingerprint; ordem de compra não muda ao reorganizar a build. |
| Lifetime | Aquisição neutra, cópia neutra, temporária rejeitada, destruição limpa associação, ator substituído exige reconciliação. |
| Segurança | API direta durante combate/resultado pendente, ator inexistente, run de outro contexto, conteúdo/revisão errados. |
| Persistência | Save/load, replay semântico, checkpoints, branches, hotreload inválido rejeitado, save anterior sem perfil. |
| Interface | Mesmos cards do combate, preview mais recente vence, navegação acessível, cancelar não grava, erro não mostra draft como equipado. |
| Não regressão | Sem perfil/política conserva gameplay; engine não depende de nomes de compartimentos ou afinidades; zonas e inscrições não recebem novos papéis. |

## 13. Fora deste ciclo

Conteúdo de reações elementais novas e combinações específicas de múltiplas afinidades, compartimentos que transformam o fluxo global de compra, movimentação em combate, pesos/custos variados por carta, expansão de capacidade por relíquias e progressão permanente de compartimentos. São extensões possíveis, não requisitos ocultos para validar esta mecânica. Isso não limita o contrato genérico a afinidades nem exclui componentes de custo/condição/alvo/trigger deste ciclo.
