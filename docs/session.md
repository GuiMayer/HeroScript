A implementação dessa funcionalidade é o que justifica toda a complexidade da arquitetura **Headless** e do **Kernel** que você construiu. Como o estado do jogo é determinado puramente por dados (.json) e processado de forma determinística pelo Pipeline de Baldes, a **Timeline** deixa de ser um problema de "física" ou "animação" e passa a ser um problema de  **Gerenciamento de Estado** .

Aqui está o resumo do que estruturamos para o sistema de Timeline:

### 1. Snapshot de Estado (Snapshot-based Undo)

Como os seus sub-módulos (`Core.Resources`, `Core.Entities`) são desacoplados, cada ação realizada gera um "Snapshot" leve do estado atual.

* Antes de cada execução da Pipeline de Dano, o Kernel salva uma cópia dos valores dos recursos e das Tags ativas.
* **A vantagem:** Voltar uma ação é simplesmente descartar o estado atual e recarregar o Snapshot anterior no `Core.Entities`. Como você não tem acoplamento com a engine visual, isso acontece instantaneamente na memória.

### 2. O Histórico de Comandos (Command Pattern)

Discutimos que a Timeline não armazena apenas "o que mudou", mas "qual comando causou a mudança".

* Cada jogada é encapsulada em um objeto de comando que contém o `DamagePacket` original e o Delta aplicado.
* Isso permite que você faça um **"Redo"** (refazer a ação) enviando o mesmo pacote exatamente pela mesma pipeline, garantindo que o resultado seja 100% idêntico devido ao determinismo do seu `Core.Math`.

### 3. Timeline Não-Linear (Previsão de Futuro)

A parte mais potente para o  *theory crafting* : como o Kernel é rápido (417 testes em 470ms), o jogo pode projetar a Timeline para frente.

* O usuário pode simular uma sequência de 3 ou 4 cartas e ver o estado final do tabuleiro sem "confirmar" a jogada.
* Se o resultado não for o esperado, ele volta na Timeline para o ponto inicial. Isso só é possível porque o seu sistema de **Deltas Universais** permite aplicar e desaplicar modificadores sem destruir a base dos dados.

### 4. Sincronia Visual (O "Playback")

A Timeline no Kernel funciona como uma fita magnética. Quando você volta uma ação no Kernel:

1. O Kernel avisa a interface (Godot/Unity) que o estado agora é X.
2. A interface apenas executa a animação de "reverso" ou reseta os elementos visuais para a posição X.
3. A lógica nunca "quebra" porque a interface não manda no dado; ela apenas o reflete.

### 5. Implicações para o MVP

Para o seu MVP de combate, isso significa que você pode implementar um botão de "Desfazer" que é 100% confiável. Se o usuário cometer um erro tático em **Vidaria** (como gastar HP demais em uma transmutação), ele pode retroceder, e o Kernel garantirá que cada ponto de vida e cada Tag de sangue volte exatamente para onde estava, validado pelo seu módulo de `Core.Validation`.

Essa capacidade de manipular o tempo reforça que você não criou apenas um jogo, mas uma  **árvore de estados explorável** .



Para suportar a narrativa de diferentes "fontes de poder" (como Vidaria, Crolbia, etc.) mantendo a integridade do  **HeroScript.Core** , o sistema de baralhos foi projetado não como listas de cartas estáticas, mas como  **conjuntos de regras de transformação de dados** .

Aqui está o detalhamento técnico do sistema de baralhos modulares:

---

## 1. Arquitetura de Herança de Dados (Deltas Universais)

A base do sistema de baralhos é a capacidade de um `.json` herdar de outro e modificar apenas chaves específicas.

* **Template Base:** Um arquivo mestre define o comportamento genérico de uma "Carta" ou "Ação" (custo, tempo de recarga, tags básicas).
* **Deltas de Poder:** Cada baralho (ex: Baralho de Vidaria) é um conjunto de arquivos Delta. Eles não reescrevem o código; eles apenas injetam modificadores que alteram o comportamento das ações para refletir a temática daquele poder.
  * *Exemplo:* Uma carta de "Ataque" básica, quando sob o Delta de Vidaria, pode ganhar automaticamente a tag `[Organic]` e um balde de modificação que escala o dano com base no HP sacrificado.

## 2. Injeção de Baldes por Baralho

O sistema de baralhos dita quais **Baldes de Processamento** serão inseridos na Pipeline de Dano quando uma ação é executada.

* **Escopo de Baralho:** Cada fonte de poder possui um identificador único no `Core.Config`. Ao selecionar um baralho, o `PipelineManager` carrega uma sequência específica de baldes de configuração.
* **Composição de Lógica:**
  * **Baralho de Vidaria:** Foca em baldes de *Lifesteal* e  *Sacrifício* . O pipeline de dano prioriza o processamento de "Custo de Sangue" antes da mitigação.
  * **Baralho de Crolbia:** Foca em baldes de *Dano Indireto* e  *Tags de Status* . O pipeline insere estágios de "Proliferação de Efeito" após a resolução do dano.

## 3. Integração com Core.Resources

Os baralhos são o ponto de conexão entre as Cartas e os recursos customizados definidos no `Core.Resources`.

* **Mapeamento Dinâmico:** O .json do baralho define qual recurso ele consome. Isso permite que um baralho use "Mana", enquanto outro use "Sanidade" ou "Calor Mecânico", sem que o `Core.Combat` precise saber o que esses nomes significam.
* **Validação de Custo:** O sub-módulo de `Core.Validation` verifica se a entidade possui o recurso exigido pelo baralho antes de permitir que a ação entre na Pipeline.

## 4. O Sistema de Tags por Baralho

As Tags são o "DNA" que os baralhos injetam no `DamagePacket`.

* **Tags de Afinidade:** Cartas de um determinado baralho carregam Tags que podem interagir com Baldes passivos de outras cartas do mesmo baralho (Sinergia).
* **Interação Trans-Baralho:** Através da API e do sistema headless, o Kernel consegue calcular interações complexas (ex: como uma Tag de fogo de um baralho interage com um Balde de água de outro) de forma determinística, permitindo o *theory crafting* multiplataforma que discutimos.

## 5. Especificações do JSON de Baralho

Um arquivo de configuração de baralho técnico contém:

| **Campo**      | **Descrição**                                                  |
| -------------------- | ---------------------------------------------------------------------- |
| `SourceID`         | Identificador da fonte de poder (ex:`POWER_VIDARIA`).                |
| `GlobalModifiers`  | Modificadores que afetam todas as cartas do baralho (Deltas).          |
| `BucketPipeline`   | Lista ordenada de IDs de baldes que este baralho injeta na execução. |
| `ResourceAffinity` | Lista de recursos do `Core.Resources`que este baralho manipula.      |
| `Version`          | Versão do JSON para compatibilidade com o `Core.Validation`.        |

---

> **Nota de Implementação:** No modo  **Sandbox** , a substituição total do JSON permite que você crie baralhos híbridos apenas mesclando as listas de `BucketPipeline`. Como o sistema é  **Headless** , você pode simular o desempenho de um baralho customizado contra um bot neural simplesmente alterando o ponteiro do arquivo de configuração na API.
>
