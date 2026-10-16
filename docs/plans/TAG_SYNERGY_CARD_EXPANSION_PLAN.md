# Expansão de cartas por sinergias de tags

Status: proposta de implementação, sem alterações de gameplay nesta etapa.

## 1. Objetivo e escopo

Expandir o **Ascendant Matrix / mundo 1**, mantendo energia como único custo de
cartas. Adicionar variedade suficiente para testar composição, recursos,
pipeline, status, condensação e zonas — não apenas aumentar dano ou criar
pares de cartas que só funcionem juntos.

Meta inicial: **24 cartas novas**, em seis lotes de quatro. O catálogo ativo de
`cards/world_one.json` passaria de 33 para 57 cartas. Conteúdo antigo de fixture
não entra nessa conta. Valores deste plano são hipóteses de playtest, não
balanceamento definitivo nem compromisso de implementar antes de validar.

“Shield” será tratado inicialmente como o recurso **`block`** do setting atual.
Não criar um segundo recurso equivalente. Se futuramente houver uma barreira
persistente distinta de Block, ela deverá ter definição e ciclo próprios.

Referências locais: [core do jogo](../game-design/CORE_GAME_DESIGN.md),
[conteúdo atual do ato](../content/ASCENDANT_WORLD_ONE.md).

## 2. Diagnóstico do conteúdo atual

- Há dano direto, crítico por tiers, multi-hit, área, queimadura, veneno,
  condensação, reserva defensiva, compra, cura limitada e debuffs.
- As 33 cartas do mundo 1 não têm um efeito de geração de energia.
- `w1_shield_bash` causa dano fixo e depois concede Block: não escala com Block.
- **Já existe** `w1_riposte`: converte 75% do Block atual em dano e depois zera
  Block. Ela será preservada como referência, não duplicada com outro nome.
- `w1_release_dot` seleciona `w1_burn` e `w1_poison` por IDs de definição.
  Uma terceira fonte de residual não participa automaticamente dessa sinergia.
- A seleção de cartas em zonas já suporta `ByTags` e consulta tags efetivas.
- A seleção de stacks já suporta `requiredTags` / `excludedTags`, mas consumo
  continua exigindo autorização explícita em `consumption.allowedRecipeIds`.
- Upgrades do mundo 1 ainda possuem listas de `cardDefinitionIds`; acrescentar
  cartas exige revisar compatibilidade, não apenas inseri-las nas recompensas.
- A apresentação de scalings já consome bindings de atributos/recursos. É
  preferível declarar relações numéricas nesses bindings a esconder tudo em
  fórmulas opacas, quando o contrato expressar o cálculo necessário.

## 3. Diretriz: sinergia emergente, não receita nominal

### 3.1. Permitido e desejado

- Uma defesa gera Block; um ataque lê Block; uma cura também pode aproveitá-lo.
- Uma influência aumenta efeitos com tags `damage` + `fire`, independentemente
  de qual carta, status, relíquia ou inimigo produziu o efeito.
- Uma busca encontra cartas com tag `defense`, inclusive cartas transformadas.
- Uma receita consome payloads compatíveis selecionados por tags.
- Uma carta exige uma circunstância: vida perdida, reserva disponível ou alvo
  protegido. A condição não menciona o nome de outra carta.

### 3.2. Evitar

- “Se você usou a carta X, a carta Y causa dano dobrado.”
- Listas de IDs de cartas para escolher quem recebe um bônus de elemento.
- Tags como `combo_with_w1_guard` ou testes de nome/localização.
- Regras, contagem de stacks ou cálculos implementados na Godot.
- Presumir que qualquer tag altera uma regra sem existir um consumidor dela.

Referências a **definições de regra** continuam normais: `statusId`,
`condensationRecipeId`, `pipelineId` e IDs de fluxos não são proibidos. O problema
é atrelar a sinergia a cartas nomeadas, não usar identidades de conteúdo.

### 3.3. Vocabulário inicial

| Dimensão | Tags | Consumo esperado |
| --- | --- | --- |
| Natureza da carta | `attack`, `skill` | Busca em zonas e apresentação. |
| Domínio do efeito | `damage`, `recovery`, `defense`, `block`, `resource_gain` | Elegibilidade do cálculo, sem misturar domínios. |
| Afinidade | `fire`, `poison` | Influências e operadores de afinidade já existentes. |
| Comportamento | `multihit`, `area`, `condensation`, `conversion` | Componentes/receitas, não comportamento implícito pela tag. |
| Fonte de scaling | `block_scaling`, `missing_health_scaling`, `max_health_scaling` | Bindings numéricos declarados. |
| Payload consumível | `residual`, `reserve`, `damage_payload`, `block_payload`, `healing_payload`, `energy_payload` | Seletores de stacks e validação de compatibilidade. |

Tags `world_one` e `starter` continuam classificando distribuição, não potência.
No catálogo atual, `dot` aparece no container de algumas cartas e `residual` no
efeito. Harmonizar essa classificação para as buscas: produtores residuais
devem declarar `residual` no container efetivo; não depender apenas do nome da
carta nem considerar todo consumidor de condensação um produtor residual.
Uma afinidade no container não deve fazer o ganho de energia da mesma carta
receber bônus de dano de fogo: consumidor deve exigir domínio e afinidade.

## 4. Fundamentos antes de publicar as cartas

### 4.1. Cálculo e energia

1. Auditar as pipelines utilizadas pelo mundo 1 e a propagação das tags efetivas.
2. Conceder energia via `MODIFY_RESOURCE`, nunca como dano negativo ou pagamento
   negativo. Preservar mínimo/máximo definidos no recurso.
3. Usar configuração compartilhada de cálculo para ganhos utilitários, isolada
   de aumentos de dano, crítico, Armor e consumo de Block do alvo.
4. Validar o ganho **efetivo**: +2 com energia 2/3 só entrega +1 se o limite for 3.
   Não elevar o máximo silenciosamente para fazer uma carta funcionar.
5. Pagamentos de vida são custos/mutações explícitos, não um dano que Armor,
   Block ou crítico possam transformar em outra troca.

### 4.2. Defesa convertida em potência

- Adicionar bindings de recurso no estágio de origem, com tags de elegibilidade.
- Para o ataque simples, testar `base 4 + 50% do Block atual`, antes dos demais
  buckets aplicáveis. Valor de origem capturado uma vez.
- Passar o resultado pela pipeline normal de dano: buffs pertinentes, crítico,
  Armor, mitigação e aplicação no recurso do alvo.
- Separar **ler Block** de **consumir Block**. Um ataque pode ler sem consumir;
  outro pode converter a reserva em explosão ofensiva.
- Em área/multi-hit, definir orçamento e consumo por ação. Não consumir a defesa
  novamente por alvo ou reaplicar origem a cada impacto indevidamente.
- Não substituir o efeito de `w1_riposte` nem alterar todas as cartas defensivas
  silenciosamente. Qualquer refatoração de sua fórmula exige equivalência.

### 4.3. Condensação extensível, mas tipada

Migrar a seleção de residuais ofensivos de uma lista de status para, por exemplo:

```json
{
  "selection": {
    "stores": ["Status"],
    "requiredTags": ["residual", "damage_payload"]
  }
}
```

Marcar os status existentes compatíveis e manter a autorização das receitas.
Uma tag não concede permissão de consumo sozinha. Criar seleções separadas para
Block, cura e energia; não somar unidades distintas sob o nome genérico `potency`.

Preservar a regra atual do residual ofensivo: 150% da soma das potências
capturadas, **não** a soma de ticks futuros. A condensação continua sendo um
único proc por ação, inclusive para cura, Block e energia. A escolha de pool não
deve permitir consumir buffs ou debuffs alheios sem opt-in.

## 5. Catálogo proposto

Nomes são placeholders. IDs, chaves e fallback em inglês; tradução PT-BR na
camada de apresentação. Cada carta terá uma disposição explícita configurada.

### Lote A — economia de energia

| ID / nome | Energia | Efeito inicial | O que testa |
| --- | ---: | --- | --- |
| `w1_power_surge` / Power Surge | 0 | Ganha 2 energia. Exila após uso. | Ganho utilitário, limites e disponibilidade de ações recalculada. |
| `w1_blood_fuel` / Blood Fuel | 0 | Paga 4 vida para ganhar 2 energia, somente se restar ao menos 1 vida. Exila. | Transação completa de troca entre recursos, sem mitigação do custo. |
| `w1_charge_cell` / Charge Cell | 1 | Armazena 2 de potência em reserva de energia. Exila. | Preparação de um recurso futuro com payload capturado. |
| `w1_release_charge` / Release Charge | 0 | Consome toda reserva `energy_payload` própria e ganha energia em um proc, 1:1. | Condensação não ofensiva e reserva compartilhada entre fontes. |

Charge Cell não concede energia automaticamente e não duplica um pagamento
futuro. Release Charge sem reserva segue a política explícita de seleção vazia.

### Lote B — defesa como recurso ofensivo e de sobrevivência

| ID / nome | Energia | Efeito inicial | Sinergia aberta |
| --- | ---: | --- | --- |
| `w1_guarded_strike` / Guarded Strike | 1 | Dano base 4 + 50% do Block atual. Não consome Block. | Qualquer fonte de Block; crítico e afinidades compatíveis. |
| `w1_guard_wave` / Guard Wave | 2 | Converte 50% do Block capturado em dano por inimigo e depois consome o Block próprio uma vez. | Cobertura em área versus perder proteção. |
| `w1_desperate_guard` / Desperate Guard | 1 | Ganha 4 Block + 20% da vida ausente. | Vida persistente, trocas de vida e recuperação. |
| `w1_shelter` / Shelter | 1 | Cura base 2 + 10% do Block atual. Exila. | Defesa virando sustentação limitada; cura não recebe crítico de ataque. |

Vida ausente é `máximo − atual` no snapshot autorizado, não “dano tomado neste
turno”. Guard Wave precisa escolher os alvos/capturar a origem antes de consumir
Block; alvo inválido não pode gastar a defesa fora de uma ação válida.

### Lote C — residuais com decisões de tempo

| ID / nome | Energia | Efeito inicial | Sinergia aberta |
| --- | ---: | --- | --- |
| `w1_cinder_brand` / Cinder Brand | 1 | Aplica residual de fogo de potência 3 por 4 ativações. | Bom retorno por duração; menor potência imediata para condensar. |
| `w1_volatile_rot` / Volatile Rot | 1 | Aplica residual de veneno de potência 6 por 1 ativação. | Alta potência para antecipação, janela curta. |
| `w1_caustic_guard` / Caustic Guard | 1 | Ganha 3 Block e aplica residual de veneno de potência 2 por 2 ativações ao alvo. | Ponte entre defesa e ofensiva residual, com dois donos/alvos. |
| `w1_lingering_aegis` / Lingering Aegis | 1 | Armazena potência defensiva 3, liberada em Block no início das próximas 2 ativações próprias. | Residual defensivo, expiração e fronteiras de ativação. |

Os dois novos residuais ofensivos usam definições com contratos de payload
compatíveis, sem depender dos IDs de Scorch/Corrosion. Especificar empilhamento,
reaplicação e expiração; não prometer duração individual por lote sem teste.
Lingering Aegis **não** deve entrar no seletor de dano por também ser residual.

### Lote D — consistência e circulação por tags

| ID / nome | Energia | Efeito inicial | Sinergia aberta |
| --- | ---: | --- | --- |
| `w1_seek_attack` / Seek Attack | 1 | Move a primeira carta `attack` elegível do baralho para a mão. | Funciona com qualquer ataque, inclusive transformado. |
| `w1_gather_defense` / Gather Defense | 1 | Move até 2 cartas `defense` elegíveis do baralho para a mão. | Busca de defesa sem lista de cartas de tank. |
| `w1_recover_trace` / Recover Trace | 1 | Recupera a primeira carta `residual` elegível do descarte para a mão. | Recicla preparação ofensiva ou defensiva, não apenas veneno. |
| `w1_prepare_guard` / Prepare Guard | 0 | Move a primeira carta `defense` elegível do descarte para o topo do baralho. | Planejamento da próxima compra, sem gerar carta nova. |

“Primeira” usa a ordem canônica configurada da zona, não enumeração de mapa.
Não exige uma nova UI de escolha manual no primeiro lote. Declarar seleção
vazia, capacidade da mão, movimentação parcial e exclusão da carta em resolução.
Não mover cartas exiladas de volta nem duplicar instâncias em buscas.

### Lote E — geometrias ofensivas

| ID / nome | Energia | Efeito inicial | O que muda |
| --- | ---: | --- | --- |
| `w1_needle_storm` / Needle Storm | 1 | 3 impactos com base 2 por impacto. | Contraste explícito com o orçamento total dividido de Flurry. |
| `w1_heavy_impact` / Heavy Impact | 3 | Dano base 28 a um alvo. | Usa energia extra para concentrar potência em um impacto. |
| `w1_break_guard` / Break Guard | 1 | Reduz primeiro 8 Block do alvo; depois causa 4 de dano à vida. | Dano/mutação em outro recurso, ordenação e defesa do alvo. |
| `w1_relay_strike` / Relay Strike | 2 | Dano base 14; excesso de dano de um abate pode continuar para mais um alvo. | Continuação genérica e escolha tática de alvo. |

O impacto no Block de Break Guard terá regras explícitas de elegibilidade:
não pode consumir o próprio Block-alvo como mitigação de si mesmo. Isso é
configuração dos cálculos, não exceção hardcoded no processador de recursos.
Precisar quantificar se esse impacto escala/crita; proposta inicial: valor fixo
utilitário, seguido por dano à vida com regras normais. Ambos devem aparecer
como efeitos distintos na carta e no preview.

### Lote F — recuperação e preparação de turno

| ID / nome | Energia | Efeito inicial | Sinergia aberta |
| --- | ---: | --- | --- |
| `w1_vital_surge` / Vital Surge | 1 | Cura 10% da vida máxima. Exila. | Scaling de recurso Maximum sem depender de outra carta. |
| `w1_mending_echo` / Mending Echo | 1 | Aplica recuperação residual de potência 2 por 3 ativações próprias. Exila. | Payload de cura e escolha de manter ticks. |
| `w1_release_mending` / Release Mending | 0 | Consome os payloads de recuperação compatíveis em uma cura única, 1:1. | Condensação de cura, não soma de ticks restantes. |
| `w1_delayed_recovery` / Delayed Recovery | 1 | Cura 8 vida no início da próxima ativação própria, uma única vez. Exila. | Agendamento por status/fronteira, sem atraso controlado pela Godot. |

Delayed Recovery não pode expirar no fim da ativação em que é aplicada. Testar
a ordem entre proc e expiração; ela não cura na aplicação e não repete a cura.
Não propor inicialmente energia no início da ativação: a renovação padrão já
enche esse recurso, tornando o bônus inútil no fluxo atual. Uma futura versão
desse efeito precisa de um contexto de gasto/limite em que tenha valor.

## 6. Limites de suporte e gates técnicos

### Base confirmada no código

Ganhos/mutações de recursos, fórmulas de recurso como em Riposte, bindings de
Current/Maximum, influências filtradas por tags, seleção de cartas por tags,
status em fronteiras configuradas, payloads, condensação de efeitos genéricos,
continuação, orçamentos multi-hit e crítico por impacto.

### Composição que precisa de spike antes de virar promessa

1. Pagamento de vida em Blood Fuel: janela, insuficiência e derrota precisam de
   política explícita; preferir custo de recurso se o contrato suportar a regra.
2. Consumo de Block após dano em área exatamente uma vez na transação.
3. Ticks de Block/cura com payload sem passar por scaling ofensivo.
4. Disposição da carta em resolução durante os fluxos de busca/recuperação.
5. Cura futura em fronteira de ativação, sem expirar antes do proc ou repeti-lo.
6. Tooltips de parâmetros derivados de recursos quando a fórmula não puder ser
   decomposta nos bindings existentes.

Se um spike falhar, registrar uma lacuna genérica e implementá-la/testá-la antes
da carta dependente. Não adicionar um handler específico para um ID de carta.

Não incluir nesta expansão inicial promessas de “ao critar, ganhe energia”,
“após a terceira carta de fogo” ou “ao matar, compre”: esses gatilhos exigem
auditar fatos/eventos pós-comando, atribuição do dono e limites de repetição.
Triggers de início/fim de ativação não comprovam suporte a esses eventos.

## 7. Ordem de implementação

### Etapa 1 — baseline e taxonomia

- Fotografar o comportamento das 33 cartas existentes e registrar seeds/testes.
- Definir tags de domínio, afinidade e payload e revisar as tags efetivas de
  cartas transformadas, efeitos derivados e status.
- Criar matriz de produtor/consumidor; nenhuma tag nova fica só decorativa.
- Saída: catálogo de tags e testes de equivalência do conteúdo atual.

### Etapa 2 — contratos compartilhados e spikes

- Validar os seis gates da seção 6 em fixtures pequenas.
- Configurar cálculo utilitário, bindings de Block/vida e seletores de receitas.
- Preservar os resultados de Scorch, Corrosion e Riposte nos mesmos snapshots.
- Saída: nenhuma dependência escondida da Godot ou de nome de carta.

### Etapa 3 — lote A, energia

- Implementar quatro cartas, reserva, receita e descrições.
- Testar energia vazia/parcial/cheia, insuficiência de vida, consumo único e
  ausência de reserva. Validar ações disponíveis após ganhar energia.
- Saída: pequenas sequências de teste sem loops de geração infinita.

### Etapa 4 — lote B, defesa e recursos

- Implementar quatro cartas usando a pipeline, com coeficientes legíveis.
- Testar Block 0/8/20; vida cheia/metade/baixa; Armor e crítico do alvo/origem.
- Validar orçamento em área e consumo único com múltiplos inimigos.

### Etapa 5 — lotes C e F, residuais genéricos

- Implementar oito cartas e status/receitas com separação de unidades.
- Demonstrar ao menos dano, Block, cura e energia no mesmo mecanismo de payload,
  com processador comum e regras declaradas diferentes.
- Testar tick, reaplicação, duração, consumo, valor zero e seleção vazia.
- Saída: condensação não limitada a dano, sem dupla aplicação de origem.

### Etapa 6 — lote D, circulação

- Implementar quatro cartas e fluxos configurados de zonas.
- Testar tag adicionada/removida por transformação, ordem, capacidade,
  descarte vazio e preservação da identidade das instâncias.

### Etapa 7 — lote E, ofensiva

- Implementar quatro cartas e certificar orçamento por impacto/ação.
- Validar tiers críticos, retarget, excesso, alvo que morre e dano a Block.

### Etapa 8 — modificadores e inscrições existentes

- Revisar a elegibilidade das novas bases para afinidades, multi-hit,
  continuação e alterações de custo sem listas nominais de combos.
- Onde upgrades ainda requererem whitelist, registrar a limitação e usar as
  regras de compatibilidade estrutural/tags que a engine efetivamente oferece.
  Não remover uma proteção estrutural para aparentar generalidade.
- Inscrições continuam alterando o personagem; modificadores, as cartas;
  relíquias, regras fundamentais. Não misturar inventários nem sistemas.

### Etapa 9 — distribuição e apresentação

- Disponibilizar primeiro as 24 cartas em pools de teste por tags, preservando
  o pool normal até validação. Depois inserir por raridade e função no ato.
- Revisar `explicitCardIds` dos pools atuais: definir se são curadoria ou limite
  de candidatos antes de migrar seletores. Referenciar IDs no catálogo de
  distribuição não é uma dependência nominal entre cartas; não deixar cartas
  novas inacessíveis por uma whitelist esquecida.
- Montar cenários/decks de teste: energia, defesa, residual, circulação e um
  deck misto. Não tornar esses decks receitas obrigatórias de build.
- Reutilizar carta, tooltip, catálogo de artes e filas de animação existentes.
- Mostrar custos, efeitos, tags, condição e regra de exílio; preview de alvo
  separado. Reservas, ticks e ganhos efetivos precisam ser visíveis.

### Etapa 10 — certificação e liberação

- Verificar a matriz da seção 8, repetir seeds e comparar hashes.
- Testar manualmente ao menos três combinações emergentes não projetadas como
  um par nominal de cartas.
- Ajustar números por contexto e economia, não pelo número de linhas de efeito.
- Atualizar catálogo do ato, guias, traduções e relatório dos testes.
- Fazer commits por etapa funcional, com conteúdos dependentes apenas após os
  contratos que os suportam. Não modificar o deck inicial silenciosamente.

## 8. Critérios de aceitação e testes

### Generalidade

- Uma carta de teste com ID inédito e mesmas tags participa da busca e scaling
  sem editar seus consumidores.
- Remover a tag via transformação faz a carta deixar de participar da sinergia.
- Um status novo autorizado, com payload/tags compatíveis, entra na condensação
  sem adicionar seu ID à receita.
- Um status com a mesma tag mas sem autorização não é consumido.
- Fontes de inimigo/status/carta usam o mesmo processador; nenhuma regra existe
  exclusivamente na descrição ou no código da interface.

### Cálculo e transação

- Energia não recebe bônus de dano, crítico, Armor ou consumo de Block.
- Ataques derivados de Block usam o mesmo caminho de mitigação dos demais.
- Consumir reservas não recalcula atributos de origem já capturados.
- Excesso em continuação não reaplica o orçamento original.
- Cura residual não causa dano nem entra no pool ofensivo por ter `residual`.
- Falha de custo/alvo/versão não consome vida, Block, stacks ou cartas.
- A carta não seleciona a si mesma indevidamente durante um fluxo de zonas.

### Determinismo e imutabilidade

- Mesmos inputs, seed e revisão: mesmos efeitos, RNG, zonas e hashes, em pelo
  menos três execuções independentes dos cenários novos.
- Prévia e inspeção não alteram o snapshot, avançam RNG ou criam instâncias.
- Simulação de comandos mistos usa a versão do agregado correto e não altera
  a origem; repetição do mesmo pedido mantém resultado/identidade esperados.
- Replay reexecutado válido; branches reproduzem a sequência, sem referências
  mutáveis compartilhadas entre snapshots.

### Economia e experiência

- Investigar ciclos energia → compra → recuperação → energia. Limites devem
  estar nas regras de conteúdo, nunca em bloqueios arbitrários da interface.
- Cura gratuita reciclável não permite farmar vida indefinidamente no pool
  normal; modos de laboratório podem flexibilizar isso explicitamente.
- Toda carta tem papel demonstrável em pelo menos um cenário e pode ser
  obtida/testada; nenhuma fica inacessível por falta de pool ou tradução.
- Nenhuma carta precisa de outra carta nomeada para ser jogável: consumidores
  de reservas ficam identificados como ferramentas condicionais, e o cenário
  de teste fornece mais de uma fonte compatível quando houver conteúdo para tal.

## 9. Entrega recomendada

Publicar primeiro o lote de energia e a família de Block, depois residuais e
condensação não ofensiva. São as lacunas mais relevantes para a variedade
pedida. A expansão completa deve resultar em aproximadamente 57 bases, mas a
medida de sucesso é a quantidade de **interações distintas comprovadas**, não
atingir um número de cartas a qualquer custo.
