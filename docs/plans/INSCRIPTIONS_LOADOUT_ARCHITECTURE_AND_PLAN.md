# Inscrições, relíquias e modificadores de cartas

Data: 2026-10-15. Estado: arquitetura proposta; primeiro marco é um laboratório visual isolado.

## 1. Decisões e limites

Definido pelo autor:

- Cartas são ações; seus modificadores continuam itens que transformam uma carta específica.
- Inscrições são peças equipáveis para construir a especialização do personagem.
- Relíquias são reservadas a mudanças fundamentais de regras, não a simples incrementos de atributos.
- Exemplos de relíquias: perder bônus de dano crítico e obter roubo de vida nos ataques; perder shield e obter armor, um atributo excepcionalmente raro.
- Regras e conteúdo pertencem à engine e aos JSONs revisionados; Godot apresenta dados e captura intenções.
- Manter determinismo, snapshots imutáveis, replay, branches e separação de progressão entre settings.
- Começar pela cena de loadout para validar a experiência antes de integrar mudanças à run.
- Inventário de inscrições separado por personagem deve ser uma opção válida, configurável pelo setting; não presumir inventário compartilhado obrigatório.

Propostas iniciais, ainda sujeitas à validação:

- Seis posições e orçamento de 12 pontos no laboratório. Não são constantes da futura engine.
- Equipamento reorganizável em preparação/descanso/locais autorizados pelo modo; inicialmente não durante combate.
- Relíquias não consomem os espaços/orçamento das inscrições. Seu limite e aquisição são outra política.
- Inscrições favorecem dano direto, crítico, efeitos periódicos, condensação, múltiplos impactos, defesa e cura por contribuições numéricas contextuais. Não concedem novos impactos ou reescrevem uma carta.
- Sem polaridades, fusão, ranking ou poder permanente no primeiro ciclo. Desbloqueios de opções permanecem separados da posse durante a run.

## 2. Fronteiras de responsabilidade

| Sistema | Dono e finalidade | Exemplo | Não deve fazer |
| --- | --- | --- | --- |
| Carta | Instância do deck; executar ação | Aplicar queimadura | Conhecer o inventário do herói |
| Modificador de carta | Item vinculado/consumido em uma carta | Transformar um impacto em três | Virar equipamento global do personagem |
| Inscrição | Instância possuída e equipada pelo ator | Aumentar o atributo de bônus de dano periódico do personagem | Implementar uma segunda pipeline ou alterar o dano base de cada carta |
| Relíquia | Fonte de regras excepcionais da run/ator | Trocar bônus crítico por roubo de vida | Ser apenas outra inscrição com outro nome |

Reutilizar a mesma gramática de influências, efeitos, condições e resultados não significa fundir os domínios. Identidade, propriedade, aquisição, instalação e projeções permanecem distintas. O processador recebe efeitos/quantidades sem decidir regras pelo nome da fonte.

## 3. Diagnóstico da engine atual

Inspeção do código atual, não inferência a partir da interface:

| Capacidade | Situação | Evidência / lacuna |
| --- | --- | --- |
| Pipeline genérica com Add, AddPercent, Multiply, Set, limites, fórmulas e traces | Existe | `src/Core/Calculations/CalculationModels.cs`, `CalculationEngine.cs`. Não criar cálculo de inscrição na Godot. |
| Filtros por canal, bucket, tags, escopo e prioridade | Existe | `ContextualInfluencePolicies.cs`, `CalculationInfluenceProviders.cs`. O setting escolhe onde cada bônus entra. |
| Atributos tipados e mutações autorizadas | Existe | `src/Core/Entity/EntityAttributeTransitions.cs`. Equipar não deve aplicar mutações aditivas irreversíveis ao atributo base. |
| Projeção de atributos pelo cálculo canônico | Parcial | `ActorAttributeProjectionService.cs` calcula base/efetivo e traces. `RelicCalculationInfluenceProvider` exige `context.Combat != null`; falta equivalência fora/dentro de combate. |
| Relíquias com identidade, owner, revisão, stacks, influências e triggers | Existe | `src/Core/Run/RelicState.cs`, `Combat/Flow/CombatRelicLifecycle.cs`. Ainda não há composição geral de regras excepcionais vindas delas. |
| Efeitos encadeados, fórmulas e resultados tipados | Existe como fundação | `Effects/EffectDefinition.cs`, `EffectTriggerExecutor.cs`. Não equivale a um interceptor global de todo ataque. |
| Roubo de vida global vindo de uma relíquia | Parcial | Não há contrato específico de augment pós-aplicação em todas as fontes. Evitar copiar cura encadeada em cada carta. |
| Restrição de shield enquanto relíquia estiver ativa | Parcial | Recursos e limites são genéricos (`Resources/ResourceDefinition.cs`), mas falta provar/aplicar restrição dinâmica por dono em todas as entradas, não apenas no preview de dano. |
| Crítico global por setting e exceção de carta/efeito | Existe parcialmente para esse uso | `Effects/EffectResolutionPolicy.cs` combina regras do modo e overrides locais de efeito/carta. Ainda não coleta overrides globais de relíquias por owner. |
| Itens modificadores de cartas | Existe e deve continuar separado | `Run/Content/CardModifierItemDefinition.cs`, planner e `API/Controllers/CardModifierItemController.cs`: posse, vínculo/consumo e preview por carta. |
| Inventário/slots/orçamento de inscrições | Não existe | Não há `Loadout`/`Inscription` em `RunState` ou no registry de conteúdo. `Run.Modifiers` não substitui posse/equipamento. |
| API e comandos de loadout | Não existe | A aquisição/equipamento precisa ser integrada ao gateway canônico, não a um controller que altera estado diretamente. |
| Conteúdo publicado e hot reload | Existe como infraestrutura | `ContentKindRegistry`, `ContentGraphValidator`, `RunContentCompatibilityValidator`: registrar e validar os novos tipos/referências. |
| Unlocks | Parcial | `Meta/ProfileProgressPolicy.cs`: `UnlockTargetKind` cobre Card, CardUpgrade e Relic, não inscrições. |
| Journal, replay, branches, sessão compacta | Existe como infraestrutura | Novo estado e novos comandos devem entrar no agregado e nas projeções existentes; não criar um save paralelo de loadout. |

### Pontos de atenção

1. A influência de relíquia pode aparecer no combate e não no painel fora dele. Resolver propriedade sem fabricar um combate fictício para habilitar providers.
2. `CalculationSourceKind` ainda não tem Inscription. Acrescentar a identidade de origem sem renumerar membros existentes serializados.
3. Armadura é atributo, shield/block é recurso conforme o setting. Não converter um no outro por nomes hardcoded.
4. Não proibir todo conteúdo antigo de relíquia ao introduzir a direção de design: auditar e migrar os JSONs do jogo explicitamente. A engine continua genérica.
5. Existe teste numérico de `LIFE_STEAL_CONVERSION` em `tests/Core.Tests/Math/MathEngineTests.cs`, mas ele só prova `input × 0.2`. Não prova roubo de vida global após aplicação real. `GameplayContentValidationTests.InstancePoliciesTriggersAndInfluenceDestinationsAreValidated` também demonstra que um boundary arbitrário `OnHit` sem lifecycle executável é rejeitado. Dar esse nome a um trigger não implementa o hook.

## 4. Arquitetura proposta

### 4.1. Conteúdo e estado

- `InscriptionDefinition`: ID, apresentação, tags/família, referências de componentes e custo de equipamento. Influências usam a gramática existente.
- `LoadoutPolicyDefinition`: escopo do inventário, slots e compatibilidades, orçamento, duplicatas/exclusividade, janelas de reorganização e comportamento em mudanças de política. Valores definidos pelo JSON do setting.
- `InscriptionInstance`: identidade determinística, definição, inventário proprietário, revisão de aquisição e proveniência. Posse não implica equipamento, e dono do inventário não é necessariamente o ator equipado.
- `InscriptionInventoryState`: identidade e escopo do proprietário, com as instâncias possuídas. Uma única autoridade de posse, seja o inventário individual ou compartilhado.
- `ActorLoadoutState`: identidade estável do ator e associação ordenada slot → instância acessível pela política. Não duplicar instâncias possuídas nem guardar um segundo conjunto autoritativo de atributos efetivos.
- Estado no agregado da run, opcional/ausente até habilitação. Nunca em Preferences ou no perfil global da Godot.
- API de consulta retorna projeção própria, não referências mutáveis ao agregado.

Esboço de conteúdo (proposto, ainda não é schema publicado):

```json
{
  "inscriptionId": "calibrated_focus",
  "equipmentCost": 3,
  "tags": ["critical"],
  "influences": [{
    "influenceId": "critical_chance",
    "channel": "critical_chance",
    "bucket": "flat",
    "scope": "Actor",
    "value": 0.25
  }]
}
```

Canal/bucket/unidade precisam existir no conteúdo do setting. `0.25` só representa 25 pontos percentuais se o contrato desse canal usar probabilidade fracionária. Não aplicar conversões implícitas por nome.

#### Escopo configurável do inventário

O plano anterior mencionava owner/ator, mas não formalizava a escolha entre inventário individual e compartilhado. A política deve oferecer explicitamente:

| `inventoryScope` proposto | Posse e acesso | Equipamento |
| --- | --- | --- |
| `Actor` | Cada personagem da run tem seu próprio inventário. A aquisição identifica o personagem destinatário. Consultar/equipar com outro personagem não expõe nem utiliza essas peças. | Loadout individual, usando somente o inventário daquele personagem. |
| `Run` | A run tem um inventário compartilhado entre os personagens autorizados pela política. | Cada personagem continua com seu próprio loadout; compartilhar a coleção não aplica os efeitos a todos. |

O campo pertence ao JSON revisionado da política, não às preferências da Godot. Os nomes são propostas de contrato para a etapa 1, não campos já implementados. A escolha inicial do jogo ainda deve ser definida; suportar `Actor` não significa obrigar todos os settings a usá-lo.

- Usar a identidade persistente do personagem na run, não o ID temporário da entidade criada para um encontro, nem a identidade da definição de conteúdo. Dois personagens da mesma definição não compartilham posse acidentalmente.
- Separar propriedade do item e destinatário de suas influências. O provider usa o personagem equipado, inclusive quando o inventário pertence à run.
- Não duplicar a mesma instância ao trocar o personagem exibido ou ao compartilhar o inventário. Proposta inicial: uma instância ocupa no máximo um loadout por vez; eventual reutilização simultânea exigiria outra política explícita, não seria inferida do escopo compartilhado.
- Transferência entre inventários individuais não acontece implicitamente ao equipar. Se o setting permitir transferir, será uma ação autorizada e atômica, com origem/destino, janelas e tratamento de item equipado definidos na engine.
- Morte, remoção/substituição de personagem e troca de escopo por hot reload precisam de política explícita: rejeitar ou executar migração completa, nunca reunir/apagar inventários silenciosamente.
- O isolamento por setting e por run permanece nos dois modos. Inventário compartilhado da run não é inventário permanente da conta; desbloqueios de opções continuam separados da posse e não concedem poder persistente.
- Na integração da UI, selecionar personagem muda inventário acessível, loadout e inspector juntos; drafts são identificados por run + personagem + inventário + revisão. Trocar de personagem exige confirmar ou descartar alterações pendentes, sem aplicar o rascunho ao novo alvo.

O laboratório atual tem um único rascunho de personagem: ainda não demonstra seleção de personagens ou os dois escopos. Acrescentar fixtures com dois personagens e um seletor é um próximo marco visual, antes da integração real.

### 4.2. Planejamento e comando atômico

`LoadoutChangePlanner` recebe snapshot + personagem alvo + draft completo + política revisionada. Resolve o inventário acessível e valida posse, escopo, owner, uso por outros personagens, slots, duplicatas, compatibilidade, orçamento, janelas e revisão. Produz sucessor imutável e motivos estruturados. O cliente não escolhe um inventário arbitrário para contornar a política.

Uma troca de duas peças é validada pelo estado final, não por duas operações intermediárias que podem exceder o orçamento temporariamente. O preview e o comando usam o mesmo planner.

`APPLY_LOADOUT` pelo gateway existente leva apenas IDs/slots e a versão esperada. A engine resolve custos, regras e resultados. Manter commandId, expectedSequence/expectedStep, idempotência, transação completa, journal e recibo. Nenhum POST de preview altera RNG, progresso, inventário ou journal.

Aquisição é outro fluxo: ofertas/recompensas/lojas com proveniência e elegibilidade; ferramentas de grant continuam protegidas pelas capabilities do modo. Não aceitar criação arbitrária de instâncias pelo cliente normal.

### 4.3. Aplicação aos cálculos

Novo `InscriptionCalculationInfluenceProvider` coleta apenas peças equipadas pelo owner correto. Reutiliza filtros/ordenação/avaliação revisionada e produz `CalculationSourceKind.Inscription` e SourceId da instância.

As contribuições pertencem aos atributos do personagem: vida/recursos máximos, críticos, armor/dodge e bônus de especialização/tag, conforme o setting. As ações consomem esses atributos na pipeline canônica quando elegíveis. Não instalar a inscrição em cada carta, criar um upgrade oculto ou reescrever valores base de cartas para representar o bônus do personagem.

Não transformar inscrições em `Run.Modifiers` permanentes. Remover equipamento remove sua contribuição; não subtrair um bônus previamente somado ao atributo base. Isso evita drift, duplicação após reconexão e crescimento após equipar/desequipar repetidamente.

Corrigir a resolução de owner/contexto compartilhada para projeções fora e dentro de combate. Uma diferença só é válida se a condição realmente depender do combate/target; o painel deve mostrar essa condição em vez de prometer um número universal.

### 4.4. Relíquias que mudam regras

O nome da relíquia nunca seleciona um branch de código. Introduzir componentes genéricos de contribuição de regra/augment, compostos numa fronteira única antes da resolução, e extensões mínimas onde a gramática atual não basta.

**Crítico → roubo de vida:**

- Separar remover o bônus de dano crítico de proibir críticos. Como hipótese inicial, bônus por tier fica zero, mas tiers ainda existem; isso precisa de validação de design.
- Aplicar a restrição depois dos bônus de inscrição, sem uma inscrição reabilitar acidentalmente algo proibido. Prioridade/conflitos explícitos e traces.
- Augment pós-aplicação gera um efeito genérico de restauração usando a perda efetiva do recurso configurado no alvo, não o valor bruto pré-armadura/absorção nem overkill.
- Seleção de ataques diretos/DoT/reflexo/autodano, escopo por impacto e limite de ativação são dados. Primeiro exemplo: ataques elegíveis de seu owner; cura gerada não dispara a si própria.
- Reutilizar resultados/encadeamento existentes quando suficientes; se faltar contexto pós-aplicação, estender essa fronteira genérica uma vez, não adicionar lifesteal especial ao processador de DAMAGE.

**Shield → armor:**

- O setting identifica o recurso bloqueado e o atributo concedido. A engine não assume `shield == block` nem `armor` como resource.
- A restrição de recurso precisa alcançar ganhos, regeneração, refunds, efeitos e mudanças de limites, preservando custos/transações. Filtrar só cartas de defesa deixa bypasses.
- Definir em JSON se a troca descarta, preserva inacessível ou converte o saldo existente; não deduzir da frase “perde shield”.
- Armor continua na pipeline de mitigação com diminishing returns configurado. Não criar redução de dano fora dela.
- No primeiro modo, essa relíquia é uma das poucas fontes de armor. Essa raridade pertence ao catálogo/pools, não a um `if` na engine.

Conflitos entre relíquias precisam de resultado explícito: precedência ou rejeição com motivo. Limitar profundidade/quantidade de augments e impedir recursão sem silenciar efeitos válidos.

### 4.5. API proposta

| Interface | Finalidade |
| --- | --- |
| Conteúdo revisionado existente + kinds `inscriptions`/`loadout-policies` | Catálogo e políticas do setting |
| `GET /api/v1/runs/{runId}/loadout?actorId=...` | Personagem alvo, escopo/identidade do inventário acessível, posse, equipamento, uso por outros personagens, capacidade, janelas, motivos e versão |
| `POST /api/v1/runs/{runId}/loadout/preview` | Draft completo; relatório before/after e legalidade, sem mutação |
| `POST /api/v1/runs/{runId}/commands` + `APPLY_LOADOUT` | Confirmação canônica atômica |
| Sessão compacta e projeção de atributos existentes | Resumo versionado e contribuições por origem |

Não inventar endpoints por tipo de inscrição ou relíquia. Não ampliar cada refresh com inventários/traces enormes: resumo na sessão; detalhes consultados sob demanda, paginação onde necessária.

## 5. Primeiro marco: laboratório de loadout

Cena separada: `examples/godot-engine-showcase/scenes/inscription_loadout_lab.tscn`.

- Mesmo tema marfim e componentes básicos de UI do jogo; catálogo/slots/presets carregados de JSON de laboratório.
- Inventário de inscrições, slots, orçamento ilustrativo, seleção, equipar/mover/remover, limpar, restaurar e presets.
- Detalhe da inscrição no centro com bônus aos atributos do personagem e contrapartidas. Painel direito dedicado ao personagem, com colunas Base/Equipado e origem dos bônus em tooltip; sem dano final de cartas na Godot.
- Relíquias e modificadores de cartas não são carregados nem exibidos na cena de inscrições. São sistemas independentes, não conteúdos dos slots de inscrição.
- Draft apenas local à cena. Selecionar/mover uma peça não envia comando, não salva perfil, não altera cartas e não concede itens.
- Contador de custo soma somente os custos de apresentação do fixture; extrapolar é permitido e sinalizado. Isso não é validador de loadout nem regra de combate.
- Exibir claramente “protótipo visual / sem aplicação à run”; não simular sucesso de um endpoint inexistente.
- A amostra do laboratório declara atributos base e bônus numéricos em unidades de apresentação no JSON. Um adapter exclusivo de `scripts/labs` soma essas contribuições para validar a UX; não implementa críticos, mitigação ou rolagens. Substituir esse adapter pela projeção autoritativa ao integrar a REST, sem transportar a soma local para o combate.
- Idiomas PT/EN, rolagem, teclado/foco, layout adaptável em 1280×720, 1920×1080 e 2560×1080.
- Acesso direto por F6 no editor e pelo menu principal em “Loadout lab”.

O laboratório valida legibilidade, organização, quantidade de slots, comparação e fluxo de montagem. Não certifica balanceamento, legalidade autoritativa, efeitos das relíquias nem integração com combate.

Arquivos do primeiro marco:

| Arquivo no cliente Godot | Responsabilidade |
| --- | --- |
| `scripts/labs/inscription_loadout_draft.gd` | Seleção e associações do rascunho de UI; cópias defensivas; não decide legalidade |
| `scripts/ui/loadout/loadout_view.gd` | View passiva, componentes estáveis e intenções; não conhece sessão/REST |
| `scripts/labs/inscription_loadout_lab.gd` | Composition root do laboratório; carrega fixture e conecta intenções |
| `data/labs/inscription_loadout_lab.json` | Quinze inscrições, seis posições, três presets e atributos de um personagem de treino |
| `scripts/labs/loadout_attribute_sample.gd` | Projeção de exemplo, isolada do gameplay; adição em unidades de exibição, sem fórmulas da engine |
| `scripts/ui/loadout/character_attributes_view.gd` | Apresentação passiva dos atributos base/equipados, sem calcular regras |
| `tests/inscription_loadout_lab.gd` | Interação, isolamento, localização, layout e capturas renderizadas |

Para avaliar: reabrir o cliente e usar **Menu principal → Laboratório de inscrições / Loadout lab**, ou F6 na cena. Selecionar uma inscrição, selecionar uma posição e usar **Equipar no rascunho**. Selecionar outra posição e equipar a mesma instância a move. **Remover**, **Limpar rascunho** e **Restaurar exemplo** não afetam a run. Busca e presets são locais.

## 6. Ordem de implementação

### Etapa 0 — laboratório e aprovação visual (escopo desta entrega)

Construir fixture, draft local não autoritativo, view reutilizável, cena e entrada no menu. Testar independência de sessão/preferências, localização, estados vazios, peças movidas sem duplicar IDs e resoluções. Registrar imagem renderizada quando possível.

Aceite: usuário consegue montar/inspecionar um exemplo e distinguir seus bônus de personagem de modificações em cartas; nenhum efeito real é aplicado.

### Etapa 1 — contratos de domínio e conteúdo

Criar definições/políticas imutáveis, registrar kinds, schemas/exemplos, validação de IDs/canais/buckets/unidades/referências. Adicionar vínculo de política ao modo/ator sem pressupor herói único. Formalizar `inventoryScope` com `Actor` e `Run` e a identidade estável dos proprietários. Fechar duplicatas/exclusividade, custo e janelas nos JSONs iniciais.

Aceite: publicação rejeita conteúdo inválido antes do runtime; settings sem inscrições mantêm o comportamento atual.

### Etapa 2 — posse e planejamento puro

Adicionar estado opcional ao agregado, aquisição determinística e planner da transação completa. Não duplicar inventário de modificadores de cartas. Cobrir IDs ausentes, outra run/owner/revisão, slot inválido, orçamento, movimentos, conflitos, removals e trocas simultâneas.

Cobrir dois personagens nos dois escopos: isolamento de posse em `Actor`; coleção compartilhada com loadouts independentes em `Run`; rejeição de uso simultâneo da mesma instância na política inicial; aquisição com destinatário correto; recriação da entidade de combate sem perder a identidade/inventário do personagem.

Aceite: plano inválido não altera snapshot; mesmo input produz o mesmo sucessor/erro; não há soma cumulativa ao reequipar.

### Etapa 3 — gateway, persistência e projeção

Registrar comandos/handlers e contratos de preview/query; integrar journal/recibo, sessão, recuperação e replay. Controllers apenas validam transporte e delegam. Publicar OpenAPI e contrato de cliente. Revisar versão determinística/schema explicitamente, preservando valores de enums e hashes quando o estado novo estiver ausente.

Aceite: retries não equipam duas vezes; versão obsoleta retorna conflito; preview não escreve nem avança RNG.

### Etapa 4 — provider e atributos canônicos

Introduzir provider de inscrições e resolver de owner reutilizado por relíquias/modifiers/projeções. Expor base, contribuição, condição e efetivo; validar cartas/previews/execução e fora/dentro do combate. Cache limitado por revisão de conteúdo + fingerprint do loadout + contexto, nunca por ID da carta apenas.

Aceite: inspeção e execução usam a mesma pipeline; trocar owner/target/loadout invalida apenas o derivado necessário.

### Etapa 5 — composição de regras excepcionais

Inventariar augments/result bindings existentes e acrescentar somente o que faltar. Formalizar conflitos, restrições por owner, aftermath e políticas de recurso. Implementar as duas relíquias exemplares com testes de multihit, DoT, crítico por tier, absorção, overkill, ganho zero, morte, reflexo e prevenção de recursão.

Aceite: não editar cada carta para suportar lifesteal; não existir bypass de shield por outro módulo; armor não é hardcoded.

### Etapa 6 — integrar a cena com a REST

Trocar o fixture por um adapter de leitura/preview/comando; a view continua emitindo intenções. Manter draft e confirmar atomicamente, descartar/cancelar, tratar revisão obsoleta e indisponibilidade. Mostrar pendência honestamente, sem preview otimista apresentado como número autoritativo.

Antes de integrar, ampliar o laboratório para dois personagens nos escopos individual/compartilhado. Na integração, o seletor consulta a projeção do personagem e identifica o dono do inventário; não reaproveita drafts ou respostas de outro personagem. Testar respostas atrasadas e conflito quando dois loadouts disputam a mesma instância compartilhada.

Aceite: confirmação usa versão exata; clicar duplo não duplica operação; carta/relic/modifier nunca atravessam inventários por acidente.

### Etapa 7 — aquisição, mapa e desbloqueios

Ofertas aleatórias determinísticas, recusa, loja/crafting limitado e locais de reorganização autorizados. Acrescentar tipo de unlock/elegibilidade de inscrição e isolamento por setting. Primeiro mundo recebe poucas peças complementares; armor permanece raro por catálogo.

Aceite: nada persiste como aumento de poder entre runs; replay usa elegibilidade capturada e não o perfil atual.

### Etapa 8 — hot reload, branches, desempenho e certificação

Validar migração completa antes de ativar revisão nova: item removido, custo alterado, slots/orçamento reduzidos, regra nova e conflito. Política explícita de rejeição/migração, sem desequipar silenciosamente. Branchs herdam posse/equipamento e o histórico correto.

Incluir troca de `inventoryScope`, transferência autorizada, saída/morte de personagens e recuperação/replay de múltiplos inventários. Consultar um personagem nunca pode resolver posse a partir do personagem atualmente selecionado na UI.

Repetir jornadas completas com/sem inscrições, traces de referência, processos independentes, recuperação e UI. Medir preview/aplicar + sessão em Kestrel e depois clique-a-tela; não sacrificar validação/durabilidade para o SLO de 1 s da matriz local.

## 7. Decisões de design que a cena ajudará a fechar

- Seis posições/12 pontos são suficientes ou congestionam o inventário?
- Equipamento permitido em todos os nós fora de combate, apenas em descansos ou com custo?
- Duplicatas da mesma definição, famílias exclusivas e peças com contrapartida?
- Relíquias são reversíveis? Em quais locais e com que efeito nos recursos existentes?
- “Perder crítico” remove o bônus ou proíbe inclusive o evento crítico?
- “Perder shield” afeta estoque atual, capacidade de gerar ou ambos?

Essas escolhas não bloqueiam o laboratório. Precisam ser formalizadas antes das respectivas etapas da engine, em vez de virarem defaults escondidos no código.

## 8. Estado da entrega

### Implementação canônica em andamento

- Etapa 0: laboratório registrado em `296f7f1`; inclui os refinamentos visuais e atributos de exemplo.
- Etapa 1: contratos imutáveis `InscriptionDefinition`/`LoadoutPolicyDefinition`, registro canônico e validação do grafo implementados. Escopos Actor/Run, duplicatas, catálogo permitido e janelas explícitos; bônus referenciam atributos/pipelines, não cartas. Modos opt-out não serializam o novo vínculo. Guia em `docs/content/inscriptions.md`.
- Verificação da etapa 1: 64 testes focados de conteúdo/validação passaram. As etapas seguintes ainda não estão certificadas como concluídas.
- Etapa 1 registrada em `db0d8c1`.
- Etapa 2: estado opcional imutável no agregado, inventários Actor/Run, registro de IDs persistentes, aquisição determinística com proveniência e planner de equipamento completo implementados. Há rejeição de posse estrangeira, uso simultâneo compartilhado, tags/slots, duplicatas, capacidade e janelas inválidas; preview não altera o snapshot nem RNG/cartas. Onze testes focados de inscrições passaram, incluindo round-trip e trocas com orçamento cheio. A conexão ao lifecycle/gateway é a etapa 3, ainda pendente nesta revisão.
- Regressão após a etapa 2: os 1.770 testes Core passaram; nenhum setting existente foi habilitado implicitamente.
- Etapa 2 registrada em `762e727`.
- Etapa 3: `APPLY_LOADOUT` pelo codec/gateway existente, inicialização opt-in no lifecycle, consultas/preview versionados, cache derivado limitado e resumo na sessão implementados. Controller delega ao serviço de aplicação; OpenAPI inclui payloads vindos do registry canônico. Branch compartilhada rebasa o inventário sem duplicar instâncias.
- Verificação da etapa 3: 1.773 testes Core e 13 testes focados de contrato/arquitetura/REST passaram; aquisição inicial dos testes é uma preparação de estado confiável, não um endpoint de grant aberto. Dois testes de durabilidade (Actor/Run) reexecutam replay três vezes, recuperam depois de reiniciar, testam retry, conflito e falha de armazenamento sem alteração parcial. A suíte API completa atingiu o limite configurado de 300 s após 228 casos aprovados, sem falhas reportadas; execução abortada não é certificação da suíte completa.
- O primeiro teste OpenAPI encontrou artefatos de build antigos carregando OpenApi 3.5.4 apesar de o projeto já fixar 2.7.5. Reconstruir a saída de testes resolveu, sem atualizar dependências. Os endpoints novos permanecem experimentais; provider/atributos reais e as etapas 4–8 não estão concluídos nesta revisão.
- Etapa 3 registrada em `b82a8d2`.
- Etapa 4: provider de inscrições registrado no mesmo composite usado pela engine, origem de trace acrescentada sem renumerar enums, projeção canônica em query/preview e correção de owner compartilhada para relíquias/modificadores fora do combate. Bônus usam canais/buckets/tags configurados; não reescrevem cartas nem calculam efeitos na Godot. Validação derivada tem memoização limitada com lifetime fraco por runtime/snapshot; a projeção completa continua limitada a 32 entradas por serviço, com identidade do snapshot/personagem no cache.
- Versão determinística avançada de 31 para 32: a correção de influência fora de combate muda resultados de regras existentes. Preservar saves antigos e rejeitar execução em runtime incompatível, sem migrar ou reinterpretar journal silenciosamente. Novos settings de inscrições ainda não foram adicionados ao mundo publicado.
- Verificação da etapa 4: 1.777 testes Core e 13 testes focados de REST/contratos/arquitetura passaram. Cobertura inclui projeção e resolução com o mesmo bônus, remoção sem drift, owner fora de combate, escopo alvo, tags e fórmula na revisão de aquisição. Isso não certifica esquiva, lifesteal ou restrições de recursos ainda não implementadas.
- Etapa 4 registrada em `453d71c`.
- Etapa 5a: reações de relíquias a aplicações primárias de recurso implementadas, sem editar cartas nem duplicar cálculo. Filtros explícitos, origem/alvo configuráveis, revisão de aquisição e subexecução pelo processador existente; perda real desconta absorção e overkill. Reações/descendentes não reentram, compartilham orçamento/profundidade e falham atomicamente. Contrato e limites em `docs/content/relic-reactions.md`. Isso é uma entrega parcial da etapa 5: as duas relíquias completas e a restrição universal de shield continuam pendentes.
- Verificação da etapa 5a: 14 novos casos de reações passaram; regressão final de 1.791 testes Core e 13 testes focados da API passou. Inclui cinco execuções idênticas da mesma ação, preservação do RNG/snapshot, absorção parcial/total, overkill, múltiplos acertos, origem de status, filtros/posse, reflexo sem reentrada, dono derrotado configurável, revisão antiga, round-trip, publicação inválida e orçamento compartilhado. A suíte API completa permanece com a ressalva de timeout registrada na etapa 3.
- Etapa 5b: teto de saldo genérico por dono, materializado de restrições capturadas na relíquia. `DiscardExcess` explícito é a política implementada; remover libera sem refill. Pool/reducer/reset/ganho, regeneração, custos/refunds, rebind e entrada de encontro respeitam o teto, sem mudar o máximo base ou o histórico resolvido. Interseção usa o menor teto; mínimo incompatível rejeita a transação. Contrato em `docs/content/resource-restrictions.md`. As relíquias completas/configuração do setting ainda são a etapa 5c.
- Etapa 5b registrada em `9f42de2`, com seis novos casos focados e 1.797 testes Core aprovados.
- Etapa 5c: as duas relíquias exemplares executáveis estão publicadas no catálogo Ascendant, ainda sem distribuição em ofertas/lojas. `w1_blood_covenant` usa bucket terminal Minimum para impedir bônus crítico mesmo após contribuições adicionais, mantendo chance/tiers; sua reação recupera 20% da perda real por ataque direto contra inimigo. `w1_iron_vow` descarta/bloqueia Block e concede 25 Armor nas mesmas projeções/pipelines de mitigação. IDs, valores, filtros e contrapartidas estão em JSON; nenhuma carta alterada. Distribuição e migração do conteúdo antigo permanecem na etapa 7.
- Verificação da etapa 5c: cinco novos casos com conteúdo real, mais regressão completa de 1.802 testes Core e 13 testes focados API aprovados. Inclui 0/200/350% de chance crítica, bônus adicional incapaz de contornar o cap, repetibilidade, absorção/overkill/multihit, exclusão de autodano e DoT, zero ganho de Block, projeção de armor/curva e remoção sem refill. A compilação real encontrou e corrigiu duas lacunas não cobertas pelos mocks da etapa 5a: manifesto do novo arquivo e reconhecimento de fatos `parent.*` na validação sintática; exemplos também foram ajustados à gramática aritmética efetiva.

- [x] Diagnóstico por código e arquitetura documentados.
- [x] Cena isolada implementada e verificada.
- [ ] Aprovação do autor sobre a cena e as políticas iniciais.
- [x] Etapas 1–4: contratos, planejamento, gateway/persistência e provider canônico.
- [x] Etapa 5: regras excepcionais e duas definições executáveis de referência.
- [ ] Etapas 6–8: integração Godot, aquisição/distribuição/migração do catálogo e certificação/migração de estado.

O GDD legado permanece intocado. Inscrições ainda não estão integradas à run jogável; as duas relíquias novas existem no catálogo, mas não nas ofertas normais. As etapas 6–8 não estão concluídas.

### Verificação do primeiro marco

- 69 checks funcionais headless da nova cena passaram; 75 checks na variante com seis capturas renderizadas passaram.
- Matriz: EN/PT-BR, 1280×720, 1920×1080 e 2560×1080; PT-BR também usa escala de texto 1,2.
- Verificados: mover sem duplicar, substituição/localidade do draft, orçamento excedido apenas ilustrativo, snapshot defensivo, fixture inválido, busca, seleção, foco estável, remover/restaurar, entrada/saída pelo menu e separação entre os três sistemas.
- Nenhuma solicitação à engine, mudança de run, gravação de preferência ou vazamento de tema global nos testes.
- Regressões aprovadas: 40 checks do menu principal marfim, 90 dos outros menus marfim, 20 de contratos UI, 86 de interação das configurações e 38 de UX da mão. Não é certificação da suíte inteira nem teste de mecânicas de inscrição no combate.
- Capturas de trabalho: `.runtime/inscription-loadout-captures/loadout-{en,pt_BR}-{1280x720,1920x1080,2560x1080}.png`.
- A nova cena integra a lista padrão de `tools/test-godot-ui.ps1`.

Um primeiro teste da busca falhou porque a atribuição programática de `LineEdit.text` não emite `text_changed`. O teste foi corrigido para disparar o callback de edição; as execuções finais passaram, sem mascarar o resultado inicial.

### Refinamento do laboratório: encaixes e arrasto

Em 2026-10-15, a revisão baseada em Warframe e Duet Night Abyss adicionou:

- posições vazias verde-acinzentadas, `+` e instrução explícita, distintas dos itens marfim;
- arrasto nativo com preview, destinos destacados, mover/trocar/substituir, cancelamento sem perda e rejeição de payload estrangeiro/obsoleto;
- mesma transição local para arrasto e alternativa por clique; inventário com rolagem independente;
- barra de orçamento ilustrativo, custos visíveis e cores de componente configuráveis.

As regras reais da engine e as etapas 1–8 permanecem pendentes. A [pesquisa e contrato de UX](../design/INSCRIPTION_LOADOUT_UX_REFERENCES.md) distinguem as referências das decisões próprias.

Validação final deste refinamento: 123 checks no laboratório renderizado (incluindo gesto real do mouse e sete capturas), mais 40 do menu principal, 90 dos demais menus marfim e 20 dos contratos UI; zero falhas. Resoluções e idiomas seguem a matriz acima. As capturas estão em `.runtime/inscription-loadout-drag-captures/`. Durante o refinamento, um erro transitório de indentação impediu uma execução; foi corrigido antes da execução final aprovada.

### Refinamento: painel de atributos do personagem

O painel direito agora apresenta atributos base/equipados, com recursos máximos, armor, dodge base 0%, chance crítica e bônus crítico por tier, especializações e bônus por tag. Detalhes de inscrição ficam no centro; relíquias e modificadores de cartas foram retirados tanto do painel quanto do fixture. O catálogo visual passou de oito para quinze inscrições e permite experimentar recursos, defesa, crítico e afinidades.

As descrições agora nomeiam o atributo do personagem, distinguem pontos percentuais e não prometem modificar valores base das cartas. Chance acima de 100% é preservada; não há cálculo local de tiers, mitigação ou esquiva. A soma das amostras de apresentação é isolada em `scripts/labs/loadout_attribute_sample.gd`; a futura engine não está implementada por isso.

Foram verificados base/equipado, penalidade em vermelho, seleção sem equipamento, remover sem drift, fontes/estatísticas defensivamente copiadas, referências inválidas e isolamento da run/preferências, além do arrasto e da matriz de layout existente. As capturas desta revisão ficam em `.runtime/inscription-character-attributes/`.
