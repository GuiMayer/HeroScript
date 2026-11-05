# Integração do MVP de piromante

## Estado observado

O plano executivo descreve um conjunto de conteúdo diferente do setting `prototype-test` carregado atualmente. O pacote lista `Resources/cards.json`, `Resources/entities.json` e `Resources/inscriptions.json`. Os arquivos `*_mvp.json` gerados pela planilha existem localmente, mas não estão listados no pacote. Portanto, sua presença em disco não comprova que sejam publicados ou jogáveis.

Os arquivos gerados também têm contratos que exigem correção antes da publicação:

- `cards_mvp.json` usa raridades como `Básica` e `Rara`, enquanto `CardRarity` aceita `Common`, `Uncommon`, `Rare` e `Legendary`.
- O compilador parte de `cartas_mvp_export.json`. Executá-lo sozinho não atualiza o JSON a partir do ODS.
- Textos em `metadata.ruleText` e `specialRules` descrevem regras, mas não são por si só efeitos, intenções ou comportamentos executáveis.
- A função de inscrições do compilador converte a primeira parcela numérica que consegue interpretar em uma influência e usa `0.20` quando não consegue. As inscrições precisam ser validadas individualmente contra os contratos e regras desejados.
- O pacote gerado não é um setting isolado completo. Ainda são necessárias ligações explícitas de pool, composição de deck, modo, encontro, intenções e entidades para testar o fluxo pela API e pela Godot.

## Capacidades a confirmar na engine

O plano menciona `CardZoneInstanceCounterTrigger` e `DamageSoftCap` como primitivas implementadas. A busca pelos nomes e pelos atributos `damage_soft_cap` e `soft_cap_retention_rate` não encontrou essas implementações no código ou no setting atual. É necessário validar as primitivas equivalentes existentes antes de afirmar suporte.

Para a Forja de Brasas, a engine precisa oferecer uma forma configurável e determinística de incrementar um contador por instância em uma zona, aplicá-lo ao cálculo e limpar ou preservar esse contador de acordo com o fluxo de saída. Para o soft-cap acumulado por turno, precisa expor o total acumulado no escopo correto à pipeline e aplicar apenas a parcela excedente com a taxa configurada. Essas são necessidades de contrato, não uma prescrição para adicionar código específico de personagem ou chefe.

A implementação de leitura de intenção, enrage, transição de fase, reset de debuffs e as demais regras da planilha precisa ser confrontada individualmente com os contratos efetivamente publicados. Os textos de apresentação não são evidência suficiente.

## Certificação necessária

### Progresso da fatia fina

O setting `prototype-test` não usa mais Calor. O recurso, as condições de capacidade, os efeitos de ganho e consumo e os vínculos que escalavam valores por Calor foram removidos. A carta anteriormente chamada Coolant mantém apenas seu efeito de cura e passa a se chamar Field Recovery. Revisões de conteúdo anteriores continuam vinculadas às respectivas runs.

As definições `pyro_golpe_brasa` e `pyro_barreira_cinzas` foram escritas diretamente a partir das linhas de `Cartas_MVP` da planilha e publicadas como parte do pacote `prototype-test`. Elas também integram o pool `prototype_rewards`, disponível na seleção de recompensas existente. A defesa concede oito pontos de bloqueio. O golpe custa duas energias e aplica quatorze pontos de dano antes das etapas configuradas.

O golpe ainda não aplica Queimadura condicional após consumir duas Brasas. O contador de Brasas e sua interação exigem integração e certificação com as primitivas apropriadas.

As duas cartas integram o baralho inicial do capítulo e o pool de recompensas. O primeiro combate usa um Autômato Batedor com 55 de vida e 10 de armadura. Suas intenções configuram um ataque de 5 em turnos ímpares, um ataque de 10 em turnos pares e defesa de 6 quando sua vida estiver abaixo de 30%. As ações não consomem energia, pois a política atual encerra a ativação da IA após uma ação.

O herói começa com 100 de vida e teto fixo de 100 de bloqueio. A definição revisionada do recurso desativa a limpeza no início do turno, mantendo o bloqueio restante após o dano. Se a vida máxima mudar durante a run, o teto não acompanha essa mudança automaticamente. Vincular o máximo de um recurso ao máximo de outro precisa de investigação e possível extensão da engine.

O teste `pyro_thin_slice_online.gd` passou em 22 verificações com a Godot e uma API real isolada na porta 61989. Ele abre o capítulo, monta o combate, joga Barreira de Cinzas, confirma que o bloqueio restante persiste após o ataque e o início da ativação seguinte, verifica a próxima intenção e valida o replay semântico. O componente de carta cria o painel de hover com os dados publicados. O teste de API `ThinSliceCardsUsePublishedCostsAndEffectsDeterministically` verificou custo, efeito, imutabilidade e repetição das duas cartas.

As condições de vida baixa e a leitura numérica completa do hover não foram exercitadas ponta a ponta nesse teste. O ataque ainda não implementa a condição de duas Brasas, de modo que a fatia fina funcional não cobre integralmente a mecânica de retenção descrita na planilha.

1. Preservar o setting atual e preparar uma revisão de conteúdo isolada para a fatia fina.
2. Publicar o pacote e verificar as definições compiladas pela API.
3. Testar herói, defesa persistente, ataque e inimigo por ações canônicas, incluindo replay e repetição com a mesma seed.
4. Confirmar que a Godot mostra o cálculo publicado e o estado real das cartas.
5. Somente após a fatia fina, integrar e validar cada carta, inscrição, delta e encontro com testes de comportamento.
6. Executar simulações e registrar seeds, revisão de conteúdo, política do bot e métricas. Playtest humano requer participantes reais e não pode ser considerado realizado por uma simulação.

Não foram executados os batches de 2.000 a 5.000 seeds nem o playtest humano descritos no plano.
