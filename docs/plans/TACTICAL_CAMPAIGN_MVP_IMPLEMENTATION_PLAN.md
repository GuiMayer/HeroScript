# Plano de implementação do MVP — campanha tática, baralhos modulares e companions

Data: 2026-11-02. Estado: **em execução — etapas 0–5 concluídas; etapas 6–17 pendentes**. Consulte [o registro de execução](TACTICAL_CAMPAIGN_MVP_IMPLEMENTATION_PROGRESS.md) para evidências e limites.

Escopo: engine em `src/Core`, REST em `src/API`, conteúdo em `data/configs` e cliente `examples/godot-engine-showcase`.

Referência de produto: `C:/Users/usuario/.gemini/antigravity/brain/7e671731-6f08-4a09-8880-fb0b3e8f7b66/direcionamento_heroscript.md`, revisão de 2026-10-30. Este plano considera também a análise do MVP aprovada na conversa. Os documentos anteriores de game design permanecem preservados como referência histórica; não serão sobrescritos silenciosamente.

## 1. Resultado esperado e critério de MVP

Entregar um capítulo jogável completo que permita:

1. Continuar uma campanha do personagem sem perder seu progresso ao morrer.
2. Ler uma introdução curta e preparar a build.
3. Entrar em combate com 15 cartas do grupo do herói e 5 do módulo equipado, embaralhadas juntas.
4. Administrar Calor, reter cartas, escolher sacrifícios e aproveitar statuses/condensação.
5. Configurar um companion de suporte por meio de rotinas predefinidas.
6. Vencer e decidir entre guardar uma recompensa na reserva ou decompilá-la.
7. Aplicar inscrições ao personagem e modificadores às cartas, respeitando suas autoridades distintas.
8. Perder, voltar à preparação, ajustar a build e tentar novamente o mesmo encontro.
9. Salvar, fechar, reabrir e continuar em estados consistentes.
10. Concluir o chefe e encerrar a demonstração com o progresso salvo.

A validação de produto é o jogador compreender por que perdeu e obter uma mudança perceptível ao ajustar sua build. Exibir muitos sistemas sem fechar esse ciclo não satisfaz o MVP.

### 1.1. Conteúdo inicial

| Área | Recorte do MVP |
| --- | --- |
| Personagem | Um herói de Calor |
| Companion | Um suporte, com duas rotinas predefinidas selecionáveis |
| Módulos | Dois módulos alternativos: pagamento por cartas da mão e pagamento por cartas do descarte |
| Baralho ativo | 15 posições de herói + 5 de módulo; quantidades definidas no JSON |
| Reserva | Poucas alternativas para permitir substituições efetivas em ambos os grupos |
| Inscrições | Aproximadamente seis itens, com custo/capacidade configurados |
| Mutação de cartas | Duas ou três transformações estruturais, reutilizando os componentes existentes |
| Combates | Introdução, teste de sinergia e chefe |
| Narrativa | Introdução, transições curtas, reação à derrota e encerramento |
| Arte/áudio | Programmer art e componentes/executores atuais; sem nova direção visual |

Posições não equivalem a designs únicos: o deck inicial pode conter repetições. Os números desta tabela são parâmetros de conteúdo, não constantes da engine.

### 1.2. Fora do primeiro MVP

Campanha completa, múltiplos heróis jogáveis, editor livre de gambits, crescimento permanente entre campanhas, árvores extensas de crafting, todos os cinco tipos de módulo do direcionamento, modo arcade infinito, Workshop/Cloud/conquistas, arte final e integração comercial. Os sistemas existentes continuam disponíveis; não precisam aparecer nos menus desse setting.

## 2. Diagnóstico de implementação

| Sistema | Evidência atual | Uso no plano |
| --- | --- | --- |
| Cartas como containers | `Run/Content/CardComponentDefinition.cs`, resolução efetiva, bundles, patches e modificadores | Reutilizar; não criar processador especial para cartas do MVP |
| Cálculos | Pipelines, atributos, políticas globais, crítico por tiers e redução configurável | Configurar o setting e validar combinações |
| Status e condensação | Status payloads e receitas genéricas, incluindo liberação de stacks | Reutilizar; definir explicitamente momento de captura e multiplicadores |
| Zonas | `CardZoneDefinitions`, executor, grants e provisionamento por participante | Reutilizar circulação, reserva física e movimentação; não atribuir significado universal aos IDs |
| Compartimentos | Associação de cartas a grants/componentes, com capacidades e filtros | Manter separados da seleção do baralho ativo |
| Pagamentos | `ActionCosts` e `ActionEffectCosts` orientados a recursos; `CardPlayExecutionRequest` recebe alvos e opção de custo | Estender para seleção e pagamento com cartas, com transação única |
| Composição 15+5 | Não foi encontrado contrato completo de deck ativo/reserva/módulo com cotas exatas | Introduzir política genérica de composição |
| Recompensas | `CardSelectionTransitions.Pick` usa grants; `Decompose` já gera recursos | Configurar destino e adaptar a decisão terminal guardar/decompilar |
| Retry | `RunEncounterRetryPolicy.RestartActivity`, transição de encontro e lifecycle de recursos | Reutilizar; completar as políticas da tentativa e a UX de preparação |
| Inscrições | Inventário Actor/Run, slots, capacidade e workspaces | Reutilizar no personagem atual da campanha |
| Companion | Gambits, atores, ações legais e automação; kind `companions` ainda é JsonElement e a UI é placeholder | Acrescentar contratos tipados e integração de loadout/lifecycle |
| Progressão | Run persistida e perfil de desbloqueios | Usar a Run como sessão de campanha; não transformar o perfil de desbloqueios em inventário paralelo |
| Godot | Cenas editáveis, loadout unificado, combate, diálogo, mapa, histórico e replay | Reutilizar composição, controllers, gateway e executores atuais |

Esta é uma análise estática de suporte. As novas combinações só serão consideradas implementadas após os gates integrados deste plano.

## 3. Decisões de arquitetura obrigatórias

### 3.1. Regras e conteúdo em JSON

- Criar `data/configs/prototype-test` como setting independente, usando o manifesto/pacotes existentes.
- Nomes como Calor, Piromaníaco, Void e Molhado pertencem ao conteúdo. A engine trabalha com IDs, componentes, recursos, condições, seletores, grupos e políticas.
- Quantidades de cartas, limites, capacidade, custos, timing, elegibilidade, cadência, recuperação e recompensas são definições revisionadas.
- A Godot recebe modelos publicados, opções válidas, razões de indisponibilidade e comandos. Não calcula dano, cotas, pagamento, cooldown ou consequências de diálogo.
- A hierarquia visual continua em cenas `.tscn` e estilos/algoritmos nos recursos existentes da Godot. JSON configura gameplay e dados de apresentação, sem substituir o editor visual por outro construtor procedural.
- Limites técnicos de execução e validação permanecem distintos das regras de game design. JSON não autoriza execução ilimitada.

### 3.2. Uma autoridade para cada dado

| Dado | Autoridade |
| --- | --- |
| Instância possuída de carta | Registro/topologia canônica já existente; não duplicar a carta em outro inventário |
| Seleção do baralho ativo | Novo estado imutável de composição, referenciando as instâncias canônicas |
| Reserva/mochila | Projeção das cartas possuídas elegíveis fora da composição ativa, com localização física definida pelo setting |
| Circulação no combate | CardZoneSystem e seus fluxos |
| Componentes herdados | DeckCompartments e resolver efetivo existente |
| Atributos/equipamento do herói | Atributos persistentes e InscriptionLoadoutState |
| Mutação de carta | Itens modificadores, patches e componentes da própria carta |
| Companion equipado e rotina | Estado persistido de suporte/loadout, associado ao personagem |
| Campanha e tentativas | Run/journal/checkpoints existentes, com identidade explícita da tentativa |
| Layout/seleção/hover | Estado local da apresentação, sem campos inseridos nos snapshots da engine |

### 3.3. Composição não é circulação nem herança

Introduzir uma política de composição com grupos configuráveis, tamanho mínimo/máximo, elegibilidade, duplicatas e seleção de pacote/módulo. A regra 15+5 é uma instância dessa política.

O grupo de uma carta não deve depender somente das tags efetivas, pois uma mutação ou grant pode alterá-las. Usar identidade/proveniência de aquisição e catálogos elegíveis explicitamente definidos na política. Preservar a identidade da instância e suas melhorias ao substituir, guardar ou trocar de módulo.

Uma composição é editável fora de combate conforme a policy. A versão confirmada é capturada ao iniciar a tentativa. A circulação temporária da mão não altera essa associação.

### 3.4. Pagamento faz parte do comando atômico

Estender o contrato canônico de custos para contemplar transferências de cartas entre zonas. Reutilizar o executor de zonas como primitiva de transferência; não confundir um efeito qualquer de banir com um custo completo.

O plano de pagamento deve identificar componente/opção de custo, ator pagador, zonas permitidas, instâncias, filtros, cardinalidade e destino. Validar o conjunto inteiro, inclusive sobreposição entre custos, antes de publicar qualquer alteração.

Na execução: validar versão/revisão e seleções → reservar/validar o plano → aplicar pagamentos → resolver efeitos e respostas na ordem definida → finalizar disposição da carta → persistir um único commit. Falha de qualquer parte não pode deixar pagamento, RNG, equipamento ou journal parcialmente alterados.

### 3.5. Retry não é rollback da interface

Derrota pode resolver a tentativa sem resolver o nó da campanha. O jogador volta à janela de preparação e inicia outra tentativa pelo fluxo canônico.

- Preservar a tentativa anterior, sua causa de derrota e replay.
- Capturar a build atualizada somente ao iniciar a próxima tentativa.
- Reconstituir o estado transitório conforme JSON, sem reverter o journal.
- Não usar branches de debug para implementar o retry normal.
- Não refazer diálogo, conceder recompensa ou pagar preparação novamente por efeito colateral do retorno.
- Caso o estado atual não registre tudo que a política exigir restaurar, acrescentar um baseline imutável restrito à tentativa; não duplicar a Run inteira nem manter uma segunda persistência.

### 3.6. Companion reutiliza o runtime de ações

Validar primeiro a implementação como participante aliado automático usando ativação, ações legais e gambits existentes. Seu equipamento e comportamento são persistentes; recursos, cooldowns e aplicação de efeitos são estado de tentativa.

As rotinas só podem selecionar ações do próprio suporte, nunca assumir o input do herói. Todo efeito segue o mesmo processador e as mesmas pipelines. A UI pode mostrar um widget compacto em vez de uma terceira grande peça, sem mudar a representação autoritativa.

Targetabilidade, contribuição para vitória/derrota e participação na ordem de ativação precisam ser expressas por políticas existentes ou por extensões genéricas pequenas. Não presumir que um ator aliado, sem essas definições, já cumpre a função de companion.

## 4. Decisões de design e valores propostos para validação

As linhas abaixo são **defaults propostos para o novo setting**, não regras universais nem decisões anteriores presumidas. A etapa 0 deve registrá-las no GDD do MVP e destacar qualquer ajuste do autor.

| Tema | Proposta inicial |
| --- | --- |
| Grupo do herói | Quantidade fixa de 15; identidades substituíveis por alternativas elegíveis da reserva |
| Módulo | Um módulo equipado por vez; cinco cartas elegíveis do módulo, com alternativas próprias |
| Abertura/compra | Cinco cartas iniciais, duas por ativação posterior do herói, limite de oito |
| Retenção | Reter cartas comuns; exceções por componente/tag e fluxo, explicitamente configuradas |
| Calor | Recurso 0–100; geração, queda e limiares descritos em tabelas JSON, com parâmetros de balanceamento |
| Limite de ações | A física de custo de cada carta deve limitar os bursts; não introduzir energia universal sem decisão de design |
| Companion | Disparo a cada três ativações do herói; duas rotinas de seleção de alvo, inicialmente maior/menor valor de um recurso configurado |
| Companion no MVP | Suporte sem dano alto; regras explícitas de alvo e resultado para não impedir derrota do herói |
| Retry | Sem perda de progresso/itens permanentes; estado temporário da batalha reinicializado por policy |
| RNG do retry | Manter inicialmente a sequência determinística da campanha, como alternativa compatível com o modelo atual; não prometer a mesma mão a cada tentativa |
| Vida entre encontros | Recuperação no início de cada tentativa como primeiro preset; permanece configurável |
| Pagamento Void | Selecionar cartas elegíveis da mão; excluir a carta em resolução por default do preset |
| Pagamento necromântico | Selecionar cartas elegíveis do descarte |
| Banimento | Zona temporária de combate, recuperada no encerramento/retry; não destruir permanentemente a carta possuída |
| Recompensa | Uma oferta resolvida por guardar **ou** decompilar; nenhuma entrada automática no deck ativo |
| Persistência | Uma campanha salva do herói no setting; múltiplas campanhas/seleção de heróis ficam para expansão |

Definir também antes do balanceamento: interação entre Molhado e Queimadura; captura de potência do DoT; possibilidade de crítico nos ticks e na condensação; aplicação de Calor na criação/tick/liberação; regra de compra quando a mão está cheia; consequências de abandonar uma batalha; materiais reembolsáveis ou não. Não aplicar o mesmo bônus duas vezes por ausência de uma definição explícita.

Vinte cartas não garantem uma reciclagem a cada quatro turnos: medir o ciclo real com compra, retenção, banimentos e cartas adicionais.

## 5. Contratos de conteúdo e API a acrescentar

Nomes abaixo são propostas de contrato, não funcionalidades já existentes. Confirmar nomenclatura na etapa 1 e atualizar schemas/OpenAPI junto da implementação.

### 5.1. Definições e estado

- `DeckCompositionPolicyDefinition`: grupos, cotas, janelas de edição, duplicatas, filtros e requisitos para iniciar combate.
- `DeckModuleDefinition`: pacote elegível e apresentação, sem algoritmo de recurso próprio na classe.
- `ActorDeckLoadoutState`: seleção por instância/grupo, módulo equipado e versão, associado ao ator atual.
- Extensão tipada de pagamento: seleção por componente, transferências de zona e alternativas combinadas com custos de recurso.
- Política de tentativa: reset/retenção por categoria de estado e RNG suportado, sem opções publicadas que não tenham executor.
- Definição tipada de suporte e perfil de comportamento: vínculo ao ator, ações, rotina, cadência e referências ao runtime existente.
- Política de entrega de recompensa: destino configurado, exclusividade das decisões e referência ao sistema de grant existente.

Não promover automaticamente os kinds opacos `decks` e `companions` a schemas incompatíveis. Inventariar consumidores e usar kind novo ou migração explícita quando necessário. Não manter dois sistemas autoritativos equivalentes.

### 5.2. Modelo ilustrativo de composição

O fragmento demonstra intenção, **não é JSON executável no schema atual**:

```json
{
  "policyId": "prototype_composition",
  "editWindow": "OutsideCombat",
  "groups": [
    { "groupId": "hero", "minimum": 15, "maximum": 15, "eligibilityProfileId": "prototype_hero_cards" },
    { "groupId": "module", "minimum": 5, "maximum": 5, "eligibilityFromEquippedModule": true }
  ],
  "requireValidCompositionToStartEncounter": true
}
```

### 5.3. REST e comandos

| Área | Leitura/preview propostos | Mutação autoritativa |
| --- | --- | --- |
| Build | Snapshot de composição, reserva, módulos, slots, elegibilidade e erros; preview de substituição/troca de módulo | Comandos canônicos para alteração válida da composição/módulo |
| Pagamento | Avaliação da carta com seleções de custo; requisitos, candidatos visíveis e motivo de falha | Extensão do comando atual de jogar carta |
| Retry | Resultado da tentativa e ações disponíveis | Resolução e início de encontro pelo fluxo existente; comando novo somente se faltar semântica indispensável |
| Companion | Suportes possuídos, equipado, rotina e opções compatíveis | Comando de equipar/configurar suporte na janela autorizada |
| Recompensa | Oferta, alternativas, materiais e destino publicado | Estender/reutilizar guardar/decompor, com exclusividade e idempotência |

Rotas de leitura novas podem seguir `api/v1/runs/{runId}/deck-loadout` e `.../support-loadout`, com `preview` quando necessário. São propostas; não criar POSTs paralelos que contornem o dispatcher/journal existente. Reutilizar `/loadout`, `/deck-compartments`, `/card-selections` e inspeção de cartas onde já expressarem a operação correta.

Todos os snapshots/requests relevantes devem transportar identidade de setting, run, ator, revisão de conteúdo e versão autoritativa apropriada. Mutations mantêm command identity/idempotência existentes. Previews são read-only: não gravam draft, não consomem RNG da sessão e não confirmam recompensas.

## 6. Ordem sequencial de implementação

Cada etapa termina com entrega revisável, testes apropriados e atualização do progresso. Recomenda-se um commit por etapa; dividir etapas grandes em subcommits coerentes sem marcar o gate como concluído antes da validação.

### Etapa 0 — Baseline e contrato de produto

**Trabalho:** registrar arquivos pendentes sem misturá-los à implementação; inventariar schemas, comandos, hashes de referência, saves e cenários existentes. Criar um GDD curto do MVP derivado do direcionamento, preservando os documentos anteriores. Fechar as decisões da seção 4 e registrar propostas ainda não aprovadas. Mapear quais atividades estão acessíveis após uma derrota e quais janelas de equipamento já existem.

**Entrega:** GDD do MVP, inventário de reutilização/lacunas, matriz de estados de tentativa e suíte baseline reproduzível.

**Gate:** separar claramente regra definida, hipótese de playtest e suporte comprovado; não alterar nenhum setting existente para montar a baseline.

### Etapa 1 — Schemas, contratos e validação de publicação

**Engine/API:** definir os contratos da seção 5, registrar kinds tipados, referências e composição de políticas. Validar cotas incoerentes, módulos sem cartas suficientes, referências ausentes, custos contraditórios, cadências inválidas e políticas não suportadas. Definir campos opcionais para modos sem as novas mecânicas.

**Conteúdo:** criar esqueletos mínimos e fixtures válidas/inválidas, sem publicar capacidades de MVP ainda não implementadas.

**Testes:** schema, compilação, ciclos de referências, limites, isolamento de revisão e preservação da serialização/hashes de estados antigos. Campos vazios novos não podem mudar silenciosamente os bytes canônicos históricos.

**Gate:** configuração inválida é rejeitada na publicação com caminho/ID útil; ausência das políticas conserva o comportamento anterior.

### Etapa 2 — Composição persistente e reserva na engine

**Engine:** implementar estado e planners imutáveis de composição. Referenciar instâncias canônicas; validar propriedade, grupo, origem, duplicatas, cotas e módulo. Permitir substituição 1:1 e troca de módulo como operações completas. Manter drafts inválidos apenas na apresentação/preview; o estado confirmado continua válido. Se a UI precisar de estado incompleto, sua semântica deve ser contratada explicitamente, sem liberar combate.

Integrar grants e destruição permanente à reconciliação das referências. Não remover melhorias, inscrições ou grants de compartimento como efeito colateral da simples mudança de seleção. Não duplicar a mesma instância em reserva e deck ativo.

**Testes:** 15+5, política alternativa com outros números, módulos incompatíveis, cartas mutadas, duplicatas, perda de propriedade, tentativa de editar outro ator e rollback da troca parcial.

**Gate:** uma troca inválida preserva integralmente o estado anterior; um setting sem composição continua iniciando partidas como antes.

### Etapa 3 — Captura da build e bootstrap das zonas

**Engine:** capturar a composição confirmada ao iniciar encontro; alimentar a pilha de compra somente com suas cartas. Reserva fica fora dos fluxos de compra/reciclagem. Definir a localização persistente e o retorno das instâncias usando o CardZoneSystem e lifetimes existentes.

Preservar provisionamento por participante e revisão vinculada. Suportes e inimigos não recebem o deck do herói por coincidência de fluxo. Não criar outro embaralhador ou RNG.

**Testes:** mesmos IDs antes/depois da batalha; reserva nunca comprada; união dos grupos na compra; nenhuma duplicação em retry; persistência de patches e componentes herdados; cenários com zonas globais, por run e por ator.

**Gate:** iniciar combate só é possível com a composição válida exigida pelo modo, e a ordem inicial é reproduzível para os mesmos inputs.

### Etapa 4 — REST e Godot para deck, reserva e módulos

**API:** publicar snapshots/preview e integrar comandos ao dispatcher atual. Retornar elegibilidade, bloqueios e alterações avaliadas pelo servidor.

**Godot:** evoluir a aba Baralho do loadout unificado com áreas de seleção ativa, reserva e módulo. Reutilizar CardView, detalhes, atributos e feedback de autosave. A troca usa intenção → preview/validação → commit → read model confirmado. Não salvar um deck incompleto a cada clique da substituição; o gesto completo gera uma operação válida. Preservar o módulo do personagem atual e impedir exemplos offline na campanha.

**Testes:** drag/clique/controle, autosave, conflito de versão, retomada, respostas tardias, troca de setting e identidade das cartas/controles durante refresh.

**Gate:** o jogador consegue substituir e trocar módulo sem botões técnicos de refresh/confirmar draft e sem misturar cartas de outra sessão.

### Etapa 5 — Custos genéricos com cartas

**Engine:** estender os custos sem reescrever os custos existentes de recurso. Implementar seleção validada, contabilidade conjunta dos pagamentos e compilação para transferências genéricas. Tratar AND/alternativas de maneira consistente com o contrato atual.

Definir seleção da própria carta, elegibilidade de cartas ocultas, múltiplos pagamentos concorrendo pela mesma instância, quantidade zero e destinos cheios. Usar ordenação estável onde a ordem não for escolha de gameplay; não ordenar quando a ordem escolhida fizer parte da regra.

**Testes:** mão/descarte com outra nomenclatura, recurso + carta no mesmo custo, múltiplos componentes, sobreposição de IDs, carta de outro ator, destino inválido e falha após um pagamento ter sido planejado. Previews repetidos não alteram hash/RNG/journal.

**Gate:** pagamento, efeitos, respostas e disposição da carta são uma transação determinística, com os mesmos processadores usados pelo restante do jogo.

### Etapa 6 — Avaliação e seleção de pagamentos na Godot

**API:** ampliar avaliação/preview e request de jogar carta com seleções por componente. Descrever requisitos sem enumerar todas as combinações de cartas, evitando explosão combinatória. Preservar visibilidade de zonas e dados ocultos.

**Godot:** criar seletor editável reutilizável para pagamentos. Mostrar carta em resolução, cartas elegíveis, quantidade selecionada, custo/resultado e cancelamento. Tratar escolha de alvos e pagamento como intenções locais até o comando final; qualquer mudança de versão invalida/reavalia as seleções.

Reutilizar os executores de trânsito/exile para os pagamentos confirmados. A animação não consome cartas, não chama um POST por sacrifício e não repete o comando quando é cancelada.

**Testes:** sequência completa mouse/drag/controle, cancelamento sem consumo, seleção incompleta, conteúdo atualizado, resposta atrasada e pagamento recusado. Sem opção de custo novo, o gesto atual de jogar carta permanece igual.

**Gate:** duas cartas pequenas de prova demonstram pagar com mão e descarte na API e no mesmo laboratório/componente usado pelo combate.

### Etapa 7 — Política de tentativa e retry persistente

**Engine:** reutilizar RestartActivity e lifecycle de recursos. Definir a matriz de permanência de recursos, statuses, cooldowns, zonas, mutações, equipamento e materiais. Registrar identidade/resultado de cada tentativa e preservar o nó não concluído após derrota retryable.

Implementar somente as extensões que não puderem ser expressas com os fluxos atuais. Resetar o estado transitório por transições, com observações/eventos canônicos. Resolver RNG conforme a policy suportada; nunca usar relógio, GUID aleatório ou novo Random no retry.

**Testes:** derrota/vitória/abandono/empate; iniciar outra tentativa; falha na abertura; reinício do servidor em cada fronteira; recompensa não concedida na derrota; mutações permanentes mantidas; banimento temporário recuperado; nova build capturada na próxima abertura.

**Gate:** morrer não encerra a campanha configurada para retry, e o histórico anterior permanece íntegro/reproduzível.

### Etapa 8 — Fluxo de derrota, preparação e retomada na Godot

**Godot:** apresentar causa da derrota, Ajustar build e Tentar novamente a partir das opções publicadas. Abrir o loadout do personagem atual, aguardar o estado realmente editável/sincronizado após autosave e retornar ao mesmo encontro.

Adaptar apresentação de progresso para capítulo/encontro, sem exigir o mapa ramificado do roguelike. Reutilizar o grafo como fluxo linear e as cenas atuais. Se houver preparação obrigatória, não deixá-la ser pulada pelo autoavanço de opção única.

**Testes:** morrer → editar → retry → vencer; fechar em derrota/preparação e retomar; configuração aberta durante leitura; clique repetido; tentativa enquanto save está pendente; histórico de conversa sem repetição de consequências.

**Gate:** o jogador não precisa voltar ao menu nem iniciar uma nova campanha para se recuperar de uma derrota normal.

### Etapa 9 — Suporte/companion configurável na engine

**Engine:** implementar contrato tipado de propriedade/equipamento/rotina associado ao ator. Reutilizar participantes, ativação, seleção de candidatos legais e gambits. Definir actions/behavior profiles autorizados e escopo de inventário, sem misturá-los a inscrições, relíquias ou cartas do herói.

A cadência usa eventos/contadores determinísticos explícitos, com unidade definida: três ativações do herói não são três ações, animações ou frames. Reset e persistência seguem a policy da tentativa. Regras de desempate usam ordenação canônica.

Resolver targetabilidade e resultado do grupo por configuração; o companion não pode manter a partida aberta quando o herói já perdeu. Uma ativação sem efeito legal deve avançar pelo fluxo configurado, sem travar a prioridade.

**Testes:** cadência exata, duas rotinas, alvo indisponível, empate, hero defeated, retry, persistência, ausência de suporte, outro setting e source/provenance de efeitos no journal. Não criar um segundo executor de efeitos nem um segundo sistema de turno.

**Gate:** o suporte atua automaticamente na engine, sem precisar de timer ou comando automático de gameplay na Godot.

### Etapa 10 — REST e Godot para companion

**API:** publicar catálogo possuído, equipado, opções de rotina, permissões e estado da próxima ativação. Confirmar alterações pelo dispatcher e versões atuais.

**Godot:** substituir o placeholder por uma composição editável real apenas nos modos que habilitam a mecânica. Mostrar identidade, rotina, efeito e alvo/cadência previstos quando autorizados. Autosave segue o loadout unificado. Um widget compacto de combate consome o estado publicado e os mesmos eventos de apresentação.

**Testes:** troca de rotina, bloqueio durante combate conforme policy, falha de save, retomada, dois personagens/escopos em fixtures, snapshot histórico e zero writes por hover/preview.

**Gate:** uma escolha do jogador muda o comportamento do companion de forma observável, sem cálculos locais de regra.

### Etapa 11 — Setting mínimo: Calor, statuses e ciclo da mão

**JSON:** publicar o pacote `prototype-test`, suas referências, modo de campanha e modo de teste autorizado. Definir Calor, recurso-alvo de vida, defesa, limiares, decay, atributos e pipelines; hand cap, abertura, retenção, compra, reciclagem e exceções temporárias.

Configurar Queimadura/Molhado e suas relações. Definir, em tabela, onde cada atributo entra no valor imediato, no payload capturado, nos ticks e na condensação. Crítico por tiers segue a política global; exceções são componentes explícitos, não dependem do nome da carta.

**Godot:** expor recursos e statuses com os componentes atuais; adicionar apenas dados/ícones/descrições necessários. Limiares e próximas alterações são comunicados a partir das projeções da engine.

**Testes:** valores em limites, mão cheia, compra insuficiente, reciclagem, retenção por muitos turnos, calor máximo, pagamento sem combustível e ausência de laço gratuito infinito. Mesma simulação com várias localizações/velocidades produz o mesmo resultado autoritativo.

**Gate:** um combate simples já comprova Calor, mão retida, dois tipos de pagamento e ativação do companion antes de ampliar o conteúdo.

### Etapa 12 — Recompensa guardar/decompilar e destino da reserva

**Engine/JSON:** reutilizar CardSelection, Decompose e gameplay grant flow. O grant de carta guardada aponta para o destino de reserva configurado. Implementar exclusividade terminal da decisão quando o contrato atual de decompor uma opção não encerrar a oferta inteira.

Guardar/decompilar precisa consumir a mesma oportunidade uma única vez. A carta não entra automaticamente na composição nem é comprada por um fluxo de abertura genérico. Materiais usam o sistema de resources, com política de propriedade/persistência explícita.

**Godot:** mesma CardView da carta real; mostrar efeito, mutações, destino e quantidade de material antes da escolha. O botão reflete a ação publicada, sem conceder itens no cliente.

**Testes:** escolher as duas alternativas, POST repetido, conexão perdida após commit, reinício antes/depois da resolução e tentativa de decompilar uma oferta já guardada. Conferir oferta com definição repetida e identidade de opção estável.

**Gate:** vitória → escolha → reserva/material → preparação → próxima batalha, sem inflação do deck ativo.

### Etapa 13 — Inscrições, mutações e preparação da build

**Conteúdo/engine:** configurar capacidade, slots e itens do personagem usando o sistema existente. Configurar duas ou três mutações estruturais via card modifier items/bundles/patches. Inscrição altera atributos do personagem; modificador altera o container da carta. Relíquias permanecem um sistema separado.

Custos e aquisição de materiais seguem o fluxo canônico já existente; ampliar somente uma lacuna demonstrada pelo conteúdo mínimo. Cartas na reserva mantêm suas melhorias ao entrar/sair da composição, com permissões claras de modificação.

**Godot:** reutilizar autosave de inscrições, comparação Before/After e fontes de atributos. O jogador deve ver quais cartas escalam com a alteração do herói e o que a mutação modifica; não misturar esses conceitos em um tooltip redundante.

**Testes:** custos insuficientes, incompatibilidade, aplicação repetida, slot/capacidade, mudança de composição, troca de módulo e recuperação após conflito. Mesmo efeito vindo de carta, status ou suporte passa pelos mesmos processadores.

**Gate:** ao menos duas builds distinguíveis são montáveis sem novas regras escritas na Godot.

### Etapa 14 — Capítulo jogável e progressão linear

**Conteúdo:** escrever as três batalhas, o chefe, recompensas e diálogos curtos. Usar poucos inimigos com intenções claras, explorando retenção, pressão de defesa, escolha de sacrifício e momento da condensação. Não exigir uma única receita de build para vencer.

O capítulo oferece preparação e alternativas antes do chefe. Os diálogos não precisam de branching narrativo amplo; consequências continuam em efeitos/flags/transições configuradas. Vincular abertura e encerramento a flags duráveis para impedir repetição de recompensas.

**Godot:** adaptar navegação ao capítulo, preservar menu/pause/configurações, restringir ferramentas técnicas pelas capacidades do modo e encerrar a demo após o chefe. O save continua separado por setting; a sessão do MVP não reutiliza a campanha do Ascendant.

**Testes:** capítulo do início ao fim, retomada em cada atividade, derrota no chefe e retorno à preparação, diálogo fechado/retomado, sem softlocks por opção obrigatória ausente.

**Gate:** loop completo acessível pelo menu normal, sem console, cheats, editor JSON manual ou laboratório.

### Etapa 15 — Legibilidade, input e apresentação integrados

**Godot:** consolidar novas cenas/componentes no catálogo offline: composição, reserva, módulo, pagamento e suporte. Reutilizar o painel de atributos, CardView, tooltip, seletores, pilhas e executores. Evitar remontar árvores ao mudar seleção, status ou quantidade.

Aplicar o layout atual ao novo conteúdo sem redesign global. Validar resolução pequena/ultrawide, EN/PT-BR, escala de texto, teclado/controle, drag, pausa e movimento reduzido. Configurações e assets de apresentação continuam separados da comunicação REST.

Animação apenas para fatos confirmados; ausência de efeito/duração zero não cria espera. Custos com várias cartas usam agrupamento e os limites do executor atual, sem acrescentar uma pausa por item. Não deslocar o restante da tela ao abrir painel/seletor.

**Gate:** o jogador consegue prever custo, alvo, ação do companion e consequência útil da carta sem consultar logs técnicos.

### Etapa 16 — Determinismo, imutabilidade, persistência e desempenho

Executar roteiros com mesma revisão de conteúdo, seed, configuração e identidades de comandos em hosts/stores independentes. Comparar commits/recibos autoritativos conforme contratos existentes: RNG, custos, seleções, efeitos, respostas, composição, suporte, tentativas, recompensas e hashes.

Cobrir reinício após pagamento, derrota, alteração de módulo, recompensa e confirmação de suporte. GETs e previews não modificam a sessão; cancelamento visual não cancela/repete commit já confirmado. Roteiros com escolhas diferentes não devem ser comparados como se exigissem o mesmo hash.

Medir custo frio e quente separadamente, POST, consultas de projeção e tempo até feedback visível. Preservar o objetivo de p95 abaixo de 100 ms para POSTs frequentes do cenário controlado em Release, reportando p99/máximo e ambiente. Isso é um gate de medição, não uma promessa para qualquer máquina ou configuração.

Evitar expansão combinatória de pagamentos, cópia integral de inventário por hover e consultas por frame. Reutilizar caches revisionados com chaves completas; não remover validação, hashing autoritativo ou persistência para atingir uma meta artificial.

**Gate:** nenhuma regressão de contrato nos settings existentes; nenhuma alteração de snapshot retido; histórico antigo legível e reproduzível; orçamento de desempenho documentado com amostras reais.

### Etapa 17 — Certificação do MVP e documentação

Executar a matriz da seção 8 em checkout/export limpo e cliente isolado com API real e stores próprios. Certificar as cenas reais abertas pelo menu, não apenas fixtures. Fazer uma rodada de playtest externo e registrar compreensão, derrotas, alterações de build, duração e softlocks observados.

Atualizar README do showcase, GDD do MVP, documentação REST, schemas, guias de conteúdo e relatório de validação. Guardar seeds, scripts, revisão, hashes esperados e logs reproduzíveis. Não declarar diversão ou equilíbrio comprovados só porque os testes técnicos passaram.

**Gate:** todos os critérios da seção 9 atendidos, ou exceções claramente registradas impedindo a declaração de MVP concluído.

## 7. Marcos e dependências

| Marco | Etapas | Resultado verificável |
| --- | --- | --- |
| A — Build consistente | 0–4 | Deck/reserva/módulos editáveis, com composição e bootstrap corretos |
| B — Pagamento e derrota | 5–8 | Jogar com sacrifício, perder, ajustar e repetir sem perda de campanha |
| C — Núcleo distintivo | 9–11 | Companion configurável + Calor + retenção + dois módulos em combate |
| D — Loop completo | 12–14 | Recompensa, crescimento da build, narrativa e chefe |
| E — MVP certificado | 15–17 | UX validada, reprodução determinística, saves e playtest |

Não ampliar o catálogo antes do marco C. Cartas técnicas mínimas podem ser criadas nas etapas 3/5/9 para provar contratos, sem antecipar um pacote extenso de conteúdo.

## 8. Matriz mínima de validação

| Grupo | Casos obrigatórios |
| --- | --- |
| Composição | Quantidades diferentes via JSON, instância duplicada, origem incompatível, troca de módulo atômica, reserva excluída da compra |
| Identidade | Melhorias/componentes mantidos, propriedade por ator, mesmo item não presente em duas localizações autoritativas |
| Pagamento | Recursos + cartas, mão/descarte, alternativas, seleção parcial, IDs repetidos/ocultos, insuficiência e falha no meio da resolução |
| Cálculos | Calor nos pontos definidos, crítico por hit/tier, snapshot de DoT, condensação sem bônus duplicado, valores zero e limites |
| Turnos | Compra inicial/posterior, retenção, cap, overflow, reciclagem, ativação sem ação e nenhum loop automático infinito |
| Suporte | Cadência por unidade correta, rotina/equipamento salvos, seleção de alvo, herói derrotado, retry e proveniência dos efeitos |
| Tentativa | Derrota, empate, abandono, retomada, recomposição do transitório, permanência da build e ausência de recompensa duplicada |
| Campanha | Diálogo → preparação → combate → recompensa → próximo trecho; fechar/reabrir em cada fronteira |
| API | Idempotência, versões, setting/revisão incorretos, HTTP perdido após commit, normalização numérica e read-only previews |
| Imutabilidade | Snapshot anterior retido, coleções aninhadas não compartilhadas de forma mutável, nenhum dado de UI no agregado |
| Determinismo | Repetições em hosts novos, reinício intermediário, replay antigo, comparação sem normalizar diferenças autoritativas indevidamente |
| Godot | Mouse/controle, drag, custo/target cancelado, modal/pause, tooltip, mão vazia/cheia, EN/PT-BR e resoluções/escala |
| Performance | POST/projeção/animação medidos separadamente; stress de inventário/pagamento; sem crescimento progressivo não explicado |
| Compatibilidade | default, ascendant, volatile-core, circular-table, uno-clash e compartment-lab preservam seus fluxos aplicáveis |

Os testes não devem tocar os saves do jogador nem iniciar a campanha escolhida por ele. Usar diretórios de cliente e stores de API próprios. Reutilizar a infraestrutura de QA; não presumir que uma porta local qualquer esteja isolada.

## 9. Critérios finais de conclusão

- [ ] O MVP é selecionável como setting separado e continua funcionando após reinício.
- [ ] A engine rejeita composição inválida e a UI mostra o motivo publicado.
- [ ] Apenas cartas ativas entram no combate; reserva e módulo preservam propriedade e melhorias.
- [ ] O jogador escolhe pagamentos com cartas e nenhuma recusa/cancelamento local consome estado.
- [ ] Os dois módulos modificam decisões de gameplay de forma observável.
- [ ] Calor, statuses e condensação têm regras explícitas, previews coerentes e traces reproduzíveis.
- [ ] O companion possui uma escolha de comportamento persistida e atua sem timer de gameplay no cliente.
- [ ] Uma derrota permite ajustar e repetir o encontro, sem apagar campanha ou tentativa anterior.
- [ ] Guardar/decompilar é uma decisão exclusiva e idempotente; o deck não cresce automaticamente.
- [ ] Inscrição do personagem, modificador da carta, relíquia e compartimento continuam sistemas distintos.
- [ ] Três encontros, diálogos, preparação e encerramento podem ser jogados sem ferramentas de desenvolvimento.
- [ ] Saves, snapshots, RNG, journal e replays passam nos gates técnicos e regressões dos settings atuais.
- [ ] Novas telas usam cenas editáveis e os mesmos componentes/executores do combate/laboratórios.
- [ ] Resultados de desempenho e limitações são documentados sem usar métricas sintéticas como latência REST.
- [ ] Playtest confirma compreensão do ciclo derrota → ajuste → nova tentativa; problemas de equilíbrio restantes estão registrados.

## 10. Registro de execução futuro

Criar `TACTICAL_CAMPAIGN_MVP_IMPLEMENTATION_PROGRESS.md` na primeira etapa implementada. Para cada etapa, registrar: decisão final de design, arquivos/contratos alterados, configuração do setting, testes e evidências, commit, limitações e próxima dependência. Um campo/schema ou uma tela com placeholder não constitui funcionalidade entregue.

Este plano não modifica a engine, os JSONs jogáveis ou a Godot. Sua execução deve seguir as dependências, mantendo o novo produto opt-in e as garantias dos modos existentes.
