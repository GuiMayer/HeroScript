# Arquitetura de simulação em lote e bots de teste para o HeroScript

*Proposta de arquitetura para rodar muitos bots (aleatórios, heurísticos, busca e machine learning) contra a engine, com foco em balancear um roguelike de cartas com sistema de builds.*

**Nota sobre as fontes:** este documento vem de conhecimento geral de simulação e aprendizado por reforço (RL), não de pesquisa específica. A adequação ao HeroScript parte do que o README do repositório descreve (engine determinística, estado imutável, comandos legais, snapshots, journal e replay). Não li o código, então os detalhes de integração precisam ser conferidos contra a implementação.

---

## Princípio central

Separe dois problemas que costumam ser misturados:

1. **Rodar muitas partidas rápido.** É um problema de infraestrutura.
2. **Decidir o que os bots fazem.** É um problema de IA.

Misturar os dois gera treino lento e difícil de depurar. Resolva o primeiro com um runner de alta vazão e só depois pense em qual algoritmo vai decidir as ações.

---

## Visão geral

```
                    ┌─────────────────────────────┐
                    │  Orquestrador (Python)      │
                    │  seeds, políticas, métricas │
                    └──────┬───────────────┬──────┘
                           │ batch (gRPC)  │ logs
              ┌────────────▼───┐      ┌────▼─────────────┐
              │ Workers C#     │      │ Armazenamento    │
              │ N runs em      │─────►│ Parquet + DuckDB │
              │ paralelo, sem  │      └────┬─────────────┘
              │ HTTP interno   │           │
              └────────────────┘      ┌────▼─────────────┐
                                      │ Análise/notebooks│
                                      └──────────────────┘
```

---

## Componentes

### 1. Runner em lote dentro do processo (C#)

Uma chamada HTTP por ação é lenta demais para treino de ML, que precisa de milhões de passos. A REST API continua útil para testes de integração e para agentes externos simples. Para simulação em massa, crie um host em C# que referencia o Core diretamente e roda centenas de runs em paralelo por processo.

O determinismo e o estado imutável ajudam muito:

- clonar um estado é barato, o que abre caminho para busca em árvore;
- toda falha vem com uma seed e pode ser reproduzida bit a bit;
- runs independentes paralelizam sem compartilhar estado.

### 2. Interface estilo Gymnasium

O ecossistema de RL em Python espera esta interface:

| Chamada | Retorno |
|---|---|
| `reset(seed)` | observação inicial |
| `step(ação)` | observação, recompensa, fim da run, **máscara de ações legais** |

A engine já devolve os comandos legais, então a máscara vem quase pronta.

**Como ligar Python ao C#:**

| Opção | Vantagem | Desvantagem |
|---|---|---|
| gRPC com chamada em lote (um passo para vários ambientes) | melhor custo-benefício, desacoplado | exige definir o protocolo |
| Processo C# via stdin/stdout | simples | serialização manual |
| pythonnet | mínimo de código | mais frágil, acopla versões |

### 3. Codificação compacta do estado

O JSON do snapshot é pesado demais para uma rede neural. Escreva um encoder que gere tensores:

- cartas por ID, com embedding aprendido;
- tags como vetor multi-hot;
- recursos (vida, energia, stacks) como números normalizados;
- relíquias e modificadores ativos como conjunto.

Mão, baralho e relíquias não têm ordem natural. Redes que tratam **conjuntos** (atenção, deep sets) costumam funcionar melhor que um MLP simples.

### 4. Espaço de ações variável

O número de ações legais muda a cada turno. Duas abordagens:

- a rede **pontua cada ação legal** e escolhe entre elas;
- um espaço grande fixo com **mascaramento** das ações ilegais.

Em Stable-Baselines3, o `MaskablePPO` do pacote `sb3-contrib` já implementa o mascaramento.

### 5. Telemetria padronizada

Grave cada run como eventos estruturados em Parquet e consulte com DuckDB. Registre sempre:

- seed;
- revisão de conteúdo;
- versão da engine;
- versão e parâmetros da política do bot.

Esses campos já fazem parte do contexto reproduzível da engine. Qualquer anomalia pode ser refeita depois.

---

## Bots em camadas

Não comece com RL. Cada camada abaixo custa menos que a seguinte e já gera informação útil.

### Camada 1: bot aleatório

Roda 100 mil runs e encontra travamentos, loops infinitos, overflow de números e violações de invariantes. Isso já paga a infraestrutura. Com determinismo, cada falha vem com seed para reproduzir.

**Invariantes úteis para checar a cada passo:**
- recursos nunca ficam negativos onde não deveriam;
- zonas de cartas mantêm a contagem total consistente;
- toda run termina em número finito de passos;
- o hash do estado é igual após replay.

### Camada 2: bots heurísticos por arquétipo

Regras simples, escritas à mão:

- "priorize cartas da tag fogo";
- "defenda se a vida estiver abaixo de 30%";
- "use a carta de maior dano que cabe na energia".

Dão uma linha de base e mostram se cada arquétipo de fato existe e funciona.

### Camada 3: busca em árvore (MCTS / ISMCTS)

Aproveita a engine determinística e o clone barato de estado. Para combates com baralho oculto, o **ISMCTS** (MCTS com informação incompleta) é o padrão. Costuma render bom desempenho sem nenhum treino.

### Camada 4: RL com PPO mascarado

Comece com **um encontro isolado**, não com a run inteira. Crédito em runs longas é muito difícil, e recompensa apenas de vitória ou derrota é esparsa demais.

Pode-se usar recompensa moldada (vida restante, dano causado, turnos usados), mas com cuidado: o bot tende a otimizar o número errado e achar estratégias degeneradas.

---

## Duas camadas de decisão

Separe:

- **jogar uma build:** táticas de combate (qual carta jogar, quando defender);
- **escolher a build:** draft, mods, relíquias, atributos.

A primeira pode usar busca ou RL. A segunda tem um tratamento melhor para este projeto.

### Explorando o espaço de builds

Fixe uma política razoável de combate e explore as builds com **algoritmos evolutivos** ou **MAP-Elites** (qualidade-diversidade: mantém um mapa de builds boas **e diferentes entre si**).

Isso responde diretamente a perguntas de balanceamento:

- existe uma build que quebra o jogo?
- algum arquétipo está morto?
- quais combinações de tags dominam?

É mais barato e mais informativo que treinar um agente que vence tudo.

---

## Armadilhas

- **Bot forte não é jogador humano.** O RL pode achar estratégias que ninguém usaria, e humanos cometem erros que o bot não comete. Use o bot como medida **relativa** (comparar cartas e builds entre si) e calibre com dados de playtest humano.
- **Use escala de habilidade.** Se uma build vence mesmo com um bot fraco, provavelmente está forte demais para humanos. Se só vence com o bot forte, é mais segura.
- **Bots exploram bugs.** É um benefício, desde que as seeds estejam registradas.
- **Infra não precisa ser grande.** Um processo por núcleo na sua máquina resolve por bastante tempo. Nuvem com instâncias spot só vale quando o treino realmente travar.
- **Cuidado com a deriva de conteúdo.** Se a revisão de conteúdo mudar, os resultados antigos deixam de ser comparáveis. Sempre grave a revisão junto com cada run.

---

## Ordem de implementação sugerida

| Passo | Entrega | O que se descobre |
|---|---|---|
| 1 | Runner em lote em C# com bot aleatório e checagem de invariantes, 100 mil runs, estatísticas em DuckDB | bugs, travamentos, estabilidade da engine |
| 2 | Dois ou três bots heurísticos e tabela de win rate por arquétipo | se os arquétipos existem e são viáveis |
| 3 | Wrapper Gymnasium e PPO mascarado em um único encontro | se o ambiente é aprendível |
| 4 | MCTS/ISMCTS para combates | teto de desempenho sem treino |
| 5 | MAP-Elites sobre o espaço de builds | builds quebradas e arquétipos mortos |

---

## Decisões em aberto

- **Formato do protocolo gRPC:** como agrupar passos de vários ambientes por chamada.
- **Formato do encoder de estado:** quais campos do snapshot entram e como representar conjuntos.
- **Métricas de balanceamento:** win rate por arquétipo, distribuição de duração das runs, diversidade de builds vencedoras.
- **Calibração com humanos:** quantas runs de playtest são necessárias para ajustar a escala de habilidade dos bots.
