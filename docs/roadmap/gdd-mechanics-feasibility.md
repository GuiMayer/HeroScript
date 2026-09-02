# Viabilidade das mecânicas do GDD na engine atual

**Data da análise:** 2026-09-01  
**Base do projeto:** `09f598d`  
**GDD analisado:** `C:\Users\usuario\.gemini\antigravity\scratch\hero-script\game-design-core.md`  
**Escopo:** comparar as ideias centrais do GDD com caminhos realmente executáveis
na Core, na API REST, no conteúdo JSON e nos testes do HeroScript.

## 1. Resumo executivo

A HeroScript está arquiteturalmente bem preparada para receber o GDD, mas ainda
não implementa seu principal diferencial: a construção emergente de build por
Ascensão, ScalingPowers e poderes que mudam o comportamento do herói.

No estado atual, a engine já permite prototipar combate por cartas, mapa, oferta
de cartas, loja, matemática aditiva/multiplicativa, crítico em camadas e um
sandbox completo. Ela ainda não permite validar se a fantasia central do GDD
funciona, porque o material consumido não transforma efetivamente a carta alvo e
os sistemas de poderes, companions e reações ainda não formam um fluxo jogável.

Portanto, o projeto atual é um bom **simulador genérico e determinístico de
combate**, mas ainda não é um protótipo fiel do **núcleo emocional e mecânico do
GDD**.

## 2. Critérios de classificação

- **Completamente:** a mecânica é executável de ponta a ponta, determinística,
  persistida e acessível pela API.
- **Parcialmente:** existem estruturas ou subsistemas relevantes, mas falta uma
  integração ou regra essencial para representar o comportamento do GDD.
- **Sem respaldo nenhum:** não existe caminho executável para a mecânica. Um nome
  reservado no manifesto de conteúdo não conta como implementação.

A contagem abaixo atribui o mesmo peso a mecânicas de complexidades diferentes e
serve somente como mapa de cobertura:

| Classificação | Quantidade | Proporção simples |
| --- | ---: | ---: |
| Completamente | 7 | 27% |
| Parcialmente | 14 | 54% |
| Sem respaldo nenhum | 5 | 19% |

## 3. Mecânicas completamente viáveis

| Mecânica | Evidência e limite atual |
| --- | --- |
| Combate por cartas | Executa ações, custos, dano, cura, status, turnos, alvos e consumo de cartas pelo fluxo autoritativo da run. |
| Mapa em nós | `RunMapTransitions` mantém grafo, visita, resolução, caminhos legais e bloqueio durante encontros. |
| Seleção de três cartas | `CardSelectionTransitions` oferece quantidade configurável, escolhe cartas, adiciona ao descarte e permite reroll gratuito ou pago. |
| Loja | Ofertas, preços por raridade/tag, compra e reroll são configuráveis e cobertos por integração. |
| Matemática aditiva e multiplicativa | O damage pipeline separa dano base, bônus aditivos, multiplicadores `more`, crítico e mitigação. |
| Crítico em camadas | Chance acima de 100% produz tiers garantidos e chance de tier adicional, com multiplicador crescente. |
| Combat Sandbox | Permite declarar deck, inimigos, recursos e efeitos iniciais, além de timeline, branches, simulações e hot reload. |

### 3.1 Evidências principais

- `src/Core/Run/RunMapTransitions.cs`
- `src/Core/Run/CardSelectionTransitions.cs`
- `src/Core/Run/ShopTransitions.cs`
- `src/Core/Damage/GenericBucketProcessor.cs`
- `data/configs/default/Resources/Pipelines/DamagePipeline.json`
- `src/Core/Run/Sandbox/CombatSandboxService.cs`
- `src/API/Controllers/CombatSandboxController.cs`

## 4. Mecânicas parcialmente viáveis

| Mecânica | O que existe | O que falta para corresponder ao GDD |
| --- | --- | --- |
| Combate → recompensa | Combate e seleção de cartas funcionam separadamente. | A vitória apenas resolve o encontro; não cria automaticamente a recompensa nem governa a transição de fluxo. |
| Elite e Boss | Os tipos de nó são reconhecidos como encontros. | Não há políticas próprias de dificuldade, pool ou recompensa melhor. |
| Descanso | Nós `rest`, `forge` e `upgrade` aceitam upgrade; existe `Preparation`. | Falta um fluxo unificado de cura/escolha/custo que resolva o nó. |
| Ascensão | Uma oferta pode ser decomposta e uma instância possuída pode armazenar upgrades. | A carta consumida gera `PowerPoints` genéricos e não é material aplicado diretamente a uma carta alvo. |
| Modificações permanentes, limitadas e compatíveis | `CardUpgradeDefinition` define cartas compatíveis e `MaxApplications`; `CardInstanceState` persiste upgrades. | Os deltas persistidos não são compostos na definição efetiva da ação durante o combate. |
| Repetir | `EffectDefinition.Repeat` e `EffectModifierType.ADD_REPEAT` executam repetições reais. | Não existe aquisição dessa transformação pelo fluxo de Ascensão. |
| Multi-Hit | Repetição de efeito pode representar vários acertos e consumir rolls separados. | Não existe a regra de dividir o valor total em vários acertos menores nem uma transformação tipada de Multi-Hit. |
| Explosivo | Há alvos `ALL_ENEMIES`, efeitos multi-alvo e transformação de alvo. | Não existem proximidade, alvos adjacentes ou propagação parcial do valor. |
| Eficiente | Custos e custos alternativos são configuráveis por JSON. | Não há transformador runtime que reduza o custo efetivo de uma instância de carta. |
| ScalingPowers | Tags, fórmulas, stacks e multiplicadores exponenciais fornecem primitivas úteis. | Não há propriedade/loadout de poderes nem contagem dos poderes equipados por tag. |
| Sinergias entre sistemas por tags | Cartas, ações, efeitos, pools, modificadores e Gambits possuem tags. | As tags não formam um grafo unificado de triggers e regras entre todos esses sistemas. |
| Companions e Gambits | Há `EntityType.COMPANION`, `GambitController`, condições por recurso/turno e escolha automática de ação. | Companions não pertencem à run, não entram no lançamento normal do combate e não têm loadout persistido. |
| Energia do companion | Recursos e custos genéricos conseguem representar uma reserva de energia separada. | Não existe companion jogável cujo fluxo de Gambit consuma esse recurso. |
| Multi-Hit com crítico independente | Repetições passam pelo resolvedor e podem realizar rolls determinísticos separados. | Como Multi-Hit não é uma transformação formal, a interação ainda não é uma mecânica configurável completa. |

## 5. Mecânicas sem respaldo executável

| Mecânica | Situação atual |
| --- | --- |
| Poderes de comportamento | Não existe slot exclusivo, substituição de regeneração nem trigger como “ganhar energia ao atacar”. |
| Sincronia de companion | Não há reação imediata à ação do herói. `reaction-rules` é um tipo de conteúdo reservado sem runtime. |
| Origem/raça com poder inicial | `races` e `powers` estão previstos no manifesto, mas não há conteúdo padrão, catálogo executável, escolha inicial ou estado correspondente na run. |
| Eventos de mapa | Um nó genérico pode ser marcado como resolvido, mas não existem definições, opções, condições ou consequências de evento. |
| Modo Infinito | Não há definição do modo, progressão infinita, escala de dificuldade ou recorde. |

Os diretórios `Resources/races`, `Resources/powers` e
`Resources/companions` não existem na configuração padrão. A presença desses
tipos em `ContentManifestProvider` é preparação arquitetural, não funcionalidade.

## 6. Lacuna crítica: Ascensão

A Ascensão é a maior divergência entre o GDD e a implementação atual. Hoje o
projeto possui dois fluxos independentes:

1. `CardSelectionTransitions.Decompose` consome uma opção e aumenta
   `RunState.PowerPoints`.
2. `DeckTransitions.ApplyUpgrade` registra um `CardUpgradeState` na instância da
   carta, sujeito a compatibilidade e limite de aplicações.

Não existe um comando atômico com a semântica:

```text
carta oferecida como material
  + carta possuída como alvo
  + modificação compatível escolhida
  -> nova instância imutável da carta alvo
```

Além disso, o caminho de combate em `CombatRunCoordinator` resolve a definição
base da ação pelo catálogo revisionado, mas não compõe os deltas de
`CardInstanceState.Upgrades`. Assim, o exemplo `sharpened_edge` é persistido e
exposto para leitura, porém não altera o dano efetivo da carta.

Esse problema impede validar as perguntas mais importantes do GDD: se a
Ascensão reduz arrependimento, se incentiva o jogador a abraçar a build oferecida
e se as transformações preservam a identidade reconhecível de cada poder.

## 7. Aderência aos pilares de design

| Pilar do GDD | Aderência | Diagnóstico |
| --- | --- | --- |
| Descoberta maior que planejamento | Parcial | Ofertas aleatórias determinísticas e pools existem, mas ScalingPowers e Ascensão não criam a descoberta de sinergia pretendida. |
| Escolhas permanentes sem arrependimento | Parcial | Picks e upgrades são permanentes na run, mas o fluxo atual de decomposição usa recurso genérico e não material direcionado. |
| Builds quebradas como objetivo | Parcial | A matemática suporta crescimento elevado, mas falta o sistema de aquisição e composição que forme essas builds organicamente. |
| Sistemas conversam por tags | Parcial | Tags são disseminadas, porém não constituem ainda uma linguagem de reação compartilhada. |
| Complexidade em camadas | Boa base | Modos e capabilities permitem liberar sistemas gradualmente; companions ainda não formam uma camada jogável. |

## 8. O que o protótipo atual consegue validar

- qualidade do combate básico;
- custos, status, alvos e fluxo de turnos;
- comportamento do crítico em camadas;
- diferença entre bônus aditivos e multiplicativos;
- interação de cartas e inimigos em cenários controlados;
- utilidade do sandbox para theorycraft;
- determinismo, replay, timeline e branches.

## 9. O que ainda não pode ser validado

- se a Ascensão produz foco em vez de arrependimento;
- se o jogador descobre e abraça uma build durante a run;
- se ScalingPowers produzem satisfatoriamente o momento em que a build “decola”;
- se poderes de comportamento mudam o ritmo sem se tornarem escolha obrigatória;
- se Sincronia gera agência ou somente ruído;
- qual densidade de decisões de build sustenta uma run completa.

## 10. Ordem recomendada para chegar ao protótipo fiel

1. Criar um resolvedor de carta efetiva que componha definição base, upgrades da
   instância e regras da revisão antes de executar a ação.
2. Substituir `Decompose → PowerPoints` por um comando de Ascensão atômico com
   material, alvo e modificação tipada.
3. Implementar Repetir, Multi-Hit, Explosivo e Eficiente como transformações
   tipadas, com compatibilidade e limites definidos por JSON.
4. Automatizar a política de fluxo `combate → recompensa → próximo nó` sem mover
   regras para o cliente Godot.
5. Criar propriedade e loadout de poderes, incluindo consultas por tag e um
   primeiro ScalingPower.
6. Implementar um slot exclusivo de poder de comportamento e um único caso
   vertical de geração alternativa de energia.
7. Validar o núcleo acima antes de investir em companions, Sincronia, origens e
   modo Infinito.

## 11. Decisão recomendada de escopo

Para o primeiro protótipo fiel ao GDD, companions, origem selecionável e modo
Infinito devem continuar fora do caminho crítico. O recorte mínimo deve conter:

- um herói fixo;
- um mapa curto com combate e recompensa;
- um pool pequeno de cartas com tags legíveis;
- Ascensão funcional com pelo menos Repetir, Multi-Hit e Eficiente;
- um ScalingPower;
- um poder de comportamento;
- crítico em camadas;
- sandbox para comparar combinações e branches.

Esse recorte já permite responder às principais perguntas de design sem exigir
os sistemas secundários mais caros.

## 12. Arquivos que fundamentam a análise

- `src/Core/Run/RunMapTransitions.cs`
- `src/Core/Run/CardSelectionTransitions.cs`
- `src/Core/Run/CardInstanceState.cs`
- `src/Core/Run/DeckTransitions.cs`
- `src/Core/Run/RunManager.cs`
- `src/Core/Combat/CombatRunCoordinator.cs`
- `src/Core/Effects/EffectDefinition.cs`
- `src/Core/Effects/EffectResolver.cs`
- `src/Core/Effects/EffectModifier.cs`
- `src/Core/Combat/Gambits/GambitEngine.cs`
- `src/Core/Damage/GenericBucketProcessor.cs`
- `src/Core/Content/ContentManifestProvider.cs`
- `src/Core/Content/ContentGraphValidator.cs`
- `data/configs/default/Resources/runs/default_run.json`
- `data/configs/default/Resources/card-selections/basic_reward.json`
- `data/configs/default/Resources/card-upgrades/sharpened_edge.json`
- `data/configs/default/Resources/modes/combat_sandbox.json`

