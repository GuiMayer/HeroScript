# Conceito de interface de combate (v2)

Este documento registra o conceito visual e funcional da interface de combate: um RPG narrativo de cartas com duelos táticos 1v1, personalização de builds e planejamento de turnos, inspirado no direcionamento de HeroScript. É uma **referência de interface**. Personagens, inimigos, textos e números da imagem de referência são placeholders.

> **Mudanças em relação à v1:** custo da carta movido para o canto superior esquerdo; "Timeline" removida do cabeçalho; terminologia de rodada e turno definida; Calor removido (Brasas vivem na carta); paleta funcional definida; faixa de inscrições, painel de informação do inimigo, previsão de resultado, transparência do cálculo, equivalentes de controle e critérios mensuráveis adicionados.

Referência visual: [Imagem do conceito](combat-gameplay-concept.png). A imagem ilustra a **composição**; suas cores e artes não definem a estética. Antes da passagem para arte, gerar uma versão da referência já com a paleta da seção 3.

---

## 1. Princípios

1. **Leitura antes de beleza.** Toda informação necessária para decidir está acessível sem abrir menus.
2. **Nenhum canal único.** Cor nunca é a única forma de transmitir significado; sempre há forma, glifo ou padrão equivalente.
3. **O cálculo é transparente.** O jogador entende por que uma carta causa o valor que causa.
4. **O cliente apresenta; a engine decide.** Regras, resultados, opções e recursos vêm da engine. A interface não reimplementa lógica.
5. **Genérico por dados.** Recursos, zonas e status são componentes dirigidos por dados, sem telas novas por mecânica.

---

## 2. Glossário (termos únicos em toda a interface)

| Termo | Definição |
|---|---|
| **Rodada** | Ciclo completo: turno do jogador + turno do inimigo. É o número exibido no cabeçalho |
| **Turno** | A vez de um lado. O botão do jogador é **Encerrar turno** |
| **Energia** | Recurso por turno para jogar cartas |
| **Capacidade** | Orçamento de inscrições (fora do combate; em combate aparece só como informação) |
| **Inscrição** | Passiva equipada no herói |
| **Intenção** | Ação que o inimigo mostra antes de executar |
| **Exílio / Vazio** | Zona de cartas banidas, publicada pelo setting |

---

## 3. Identidade visual

**Programmer art.** Formas simples, ícones geométricos, contraste alto, elementos fáceis de substituir. Aplica-se a menus, animações e gameplay. O cenário é discreto e preserva o contraste dos elementos interativos. A arte de personagens e inimigos será definida depois.

### 3.1 Base monocromática
Fundo escuro com texto claro. Cor só entra onde há função (tags, feedback de valor, perigo).

| Token | Valor (placeholder) | Uso |
|---|---|---|
| `bg-0` | `#0E0E10` | Fundo |
| `bg-1` | `#1A1A1F` | Superfícies (cartas, painéis) |
| `line` | `#3A3A44` | Divisores e bordas neutras |
| `fg-0` | `#F5F5F5` | Texto principal |
| `fg-1` | `#B4B4BE` | Texto secundário |

### 3.2 Cores funcionais
| Token | Valor | Uso | Canal redundante |
|---|---|---|---|
| `valor-up` | `#5BD38A` | Valor acima da base | Seta ▲ e negrito |
| `valor-down` | `#FF6B6B` | Valor abaixo da base | Seta ▼ e sublinhado |
| `perigo` | `#FF5A5F` | Intenção letal ou ameaça crítica | Borda dupla e ícone de alerta |
| `foco` | `#FFFFFF` | Seleção, hover e foco de teclado/controle | Anel de 3 px e elevação |

Energia, estado "pronto" e "em recarga" **não usam cor**: energia é identificada por forma (losango); pronto = ícone cheio, em recarga = ícone tracejado com número.

### 3.3 Cores de tag (placeholders)
Cada tag tem três canais: **cor + glifo + silhueta da moldura da carta** (entalhe no canto). A cor ajuda, mas o jogador deve distinguir as tags apenas por glifo e silhueta.

| Tag | Cor |
|---|---|
| Fogo | `#FF7A3D` |
| Vazio | `#9B6BFF` |
| Arcano | `#4DA3FF` |
| Sombra | `#7C8294` |
| Divino | `#FFD54F` |
| Água | `#35D0C0` |

Cartas com 2-3 tags mostram os glifos em sequência (principal primeiro) e a moldura usa a silhueta da tag dominante.

### 3.4 Requisitos de cor
- Contraste mínimo: **4,5:1** para texto e **3:1** para elementos de interface.
- Validar toda a interface com simulação de deuteranopia, protanopia e tritanopia.
- Modo de alto contraste e opção de substituir cores por padrões nas tags.

---

## 4. Proporções e layout

Projetar e refinar prioritariamente para **16:9**. A composição usa **âncoras**, e não espaçamento uniforme:

- **Mão e energia:** centro-inferior.
- **Coluna de pilhas e ação:** presa à borda direita.
- **Painel de atributos:** preso à borda esquerda (flutuante).
- **Cabeçalho e faixa de inscrições:** presos ao topo.
- **Campo de batalha:** região elástica no meio; absorve o espaço sobrando.

Em outras proporções (16:10, portáteis, ultrawide), as âncoras mantêm cada grupo perto da região de atenção, e só o campo se expande. Todos os elementos permanecem dentro da área visível. Esta adaptação é provisória até existir um projeto para outros formatos. Incluir 16:10 e telas de portátil nos testes.

---

## 5. Cabeçalho

- **Nome do herói:** região superior esquerda, vindo do personagem atual.
- **Rodada:** número destacado ("Rodada 3"), com indicação de **de quem é o turno**.
- **Pausa:** sempre acessível.
- **Registro (log):** botão que abre o histórico de eventos do combate (seção 10).
- **Sem timeline.** O jogo não usa barra de ordem de ações; evitar qualquer elemento que sugira isso.

### Faixa de inscrições
Faixa fina logo abaixo do cabeçalho, alinhada à esquerda, com os ícones das inscrições equipadas (até 12 visíveis; excedente agrupado em "+N"). Não comprime o campo.

- A inscrição que **dispara** pisca e liga uma linha até a fonte do efeito.
- Hover/foco mostra descrição, rank e custo de capacidade.
- Hover numa inscrição realça as cartas e tags que ela afeta.

---

## 6. Campo de batalha

Herói e inimigo em lados opostos. As representações são provisórias.

### 6.1 Vida, escudo e status
Indicadores de vida e escudo persistente ficam junto de cada combatente. Status aparecem como ícones com contador; **até 6 visíveis**, e o excedente agrupa em "+N" com lista no hover/foco.

### 6.2 Intenção do inimigo
Visível antes da decisão do jogador. Mostra ícone de categoria e valor. Como o baralho do inimigo é revelado aos poucos, a intenção respeita o **nível de conhecimento**:

| Nível | Apresentação |
|---|---|
| Silhueta | Forma de categoria + "?" (valor desconhecido) |
| Nome e tags | Nome da carta e glifos das tags; valor aproximado ou oculto |
| Efeito completo | Ícone, valor e condições no tooltip |
| Estratégia | Além do efeito, indicação do plano em curso (por exemplo, fase do plano) |

Ícones de categoria distintos por **forma**: ataque, defesa, preparação, interferência. Intenção letal usa o token `perigo`.

### 6.3 Painel de informação do inimigo
Selecionar ou segurar foco no inimigo abre um painel compacto (não permanente) com:

- tamanho da mão e recursos próprios (meters definidos por dados);
- aura/status elemental atual;
- resistências e fraquezas **já descobertas** (as desconhecidas aparecem como "?");
- cartas reveladas no descarte do inimigo;
- inscrições conhecidas do inimigo.

Isso conecta o combate ao bestiário.

### 6.4 Painel de atributos do herói
Flutuante e recolhível, acessível pela borda esquerda e pelo atalho configurado. Abrir não desloca campo nem mão. Contém:

- atributos do personagem;
- por tag: bônus **bruto** e **efetivo** (ex.: "Fogo +60% → efetivo +37%"), refletindo o retorno decrescente;
- capacidade em uso (informativo).

Vida, escudo e recursos necessários à decisão permanecem sempre visíveis, fora do painel.

### 6.5 Recursos do herói (componente genérico)
Meters globais, contadores e zonas definidos pelo setting usam três componentes reutilizáveis: **medidor** (barra ou contador com limiares), **contador de carta** e **zona**. Qualquer mecânica de baralho (Cristais, Vazio etc.) deve se apresentar com eles, sem tela nova. Um recurso ocupa um "slot de recurso" junto ao herói, com número, limite e ícone.

> **Calor foi descontinuado.** Não existe barra de Calor. Brasas são um **contador na carta** (ver seção 7.3).

### 6.6 Companion
Presença secundária, **sem barra de vida própria** (decisão atual; revisar se companions puderem ser atacados). Apresentação compacta: ícone + recarga (número). Gatilho e estado completo em tooltip. Estados: pronto (ícone cheio), em recarga (tracejado com número). Quando o companion dispara, liga-se por linha à carta ou evento que o ativou.

O conceito visual não pressupõe que a funcionalidade já esteja implementada.

---

## 7. Mão e energia

### 7.1 Energia
Próxima à mão, identificada por losango e número (atual/máximo). Sem cor própria.

### 7.2 Leque
Cartas na parte inferior, em leque adaptativo. **Regra de empilhamento:** cartas à direita ficam por cima das da esquerda, de modo que a **parte esquerda de cada carta sempre fica visível**. Toda carta reserva uma **faixa visível** com custo, nome abreviado e glifos de tag, que não pode ser coberta.

- Dimensionar para **10 cartas** como caso normal e testar com **12**.
- Com mãos grandes, a sobreposição aumenta, mas a faixa visível é preservada.
- Hover/foco: a carta sobe, aumenta e passa à frente, mostrando tudo.
- Hover e seleção respeitam as configurações de acessibilidade (movimento reduzido substitui animações por realce estático).

### 7.3 Anatomia da carta
- **Cabeçalho:** nome; **custo no canto superior esquerdo** (losango).
- **Tags** diretamente abaixo do nome (glifos em ordem de peso; indicador visual de peso: ●●● principal, ●●○ dominante, ●○○ secundária).
- **Arte:** área substituível.
- **Função e descrição** do efeito, com valores legíveis.
- **Valor final:** o número exibido é o **valor calculado** (já com a pipeline). Se difere da base, recebe ▲/▼, cor `valor-up`/`valor-down` e a base fica acessível no tooltip.
- **Marcadores na carta:** por exemplo, Brasas acumuladas por uma carta de Fogo retida, exibidas como contador com ícone. A retenção também é indicada na carta.

Cartas e efeitos exibidos na imagem de referência são exemplos, não conteúdo definitivo.

### 7.4 Previsão antes de jogar
Ao passar por (ou selecionar) uma carta, mostrar sem confirmar:

- **dano previsto** sobre a vida e o escudo do inimigo (resultado esperado);
- **companions** que reagiriam e **inscrições** que disparariam;
- **mudanças em recursos** (ganho/gasto) e em status.

A previsão é calculada pela engine e é apenas apresentada. Quando há incerteza (informação oculta do inimigo), indicar com "?" ou intervalo.

---

## 8. Pilhas e ações

Coluna à direita da mão, com ícones e contagens para compra, descarte, exílio e demais zonas publicadas pelo setting. Abrir uma pilha mostra as cartas em um container de inspeção.

- **Fluxo espacial:** compra → mão → descarte devem ler como um fluxo. Exílio/Vazio fica visualmente separado, por ter regras próprias de retorno.
- **Pilhas do inimigo:** descarte e cartas reveladas são acessíveis pelo painel do inimigo (seção 6.3).
- **Encerrar turno:** botão destacado, com tamanho, posição e peso visual claramente distintos dos ícones de pilha (separado deles por espaço, sem agrupar), para evitar cliques acidentais. Se houver energia e cartas jogáveis, o botão pulsa suavemente (um aviso, não um bloqueio).
- Controles como cancelar seleção, avançar animação ou escolher um custo aparecem somente quando pertinentes. O comportamento e as opções vêm da engine.

---

## 9. Entrada e acessibilidade

Mouse, teclado e controle são suportados, com escala de texto e movimento reduzido.

| Função | Mouse | Teclado | Controle |
|---|---|---|---|
| Inspecionar carta/inimigo/inscrição | Hover | Foco (Tab/setas) | Foco com direcional |
| Ver detalhe estendido | Hover prolongado | Tecla de detalhe | Botão de inspeção (segurar) |
| Jogar carta | Clique/arrastar | Confirmar | Confirmar |
| Cancelar | Clique direito | Esc | Botão de voltar |
| Painel de atributos | Borda esquerda | Atalho configurável | Botão de painel |
| Realce cruzado | Hover | Foco | Foco |

Toda informação disponível por hover tem equivalente por foco. Nenhuma informação depende exclusivamente de hover.

---

## 10. Transparência, causalidade e ritmo

### 10.1 Cálculo por etapa
O tooltip de qualquer valor mostra a pipeline:
`Base 10 → Flat +0 → Increased +53% → Foco ×1,25 → 17`
com bruto versus efetivo por tag, e a origem de cada bônus (qual inscrição/atributo).

### 10.2 Realce cruzado
- Hover numa **inscrição**: realça as cartas e tags afetadas.
- Hover numa **carta**: realça as inscrições, companions e atributos que a afetam.
- Hover numa **tag**: realça todas as cartas e inscrições com essa tag.

### 10.3 Encadeamento visual
Quando efeitos disparam em cadeia, a interface mostra **fonte → resultado** por linhas ou realces sequenciais (carta jogada → companion dispara → status aplicado). O jogador deve conseguir seguir quem causou o quê e em que ordem.

### 10.4 Log
Registro consultável do combate: eventos em ordem, com origem, alvo e cálculo resumido. Filtrável por rodada.

### 10.5 Ritmo
- Velocidade de resolução: **1×, 2×, 4×** e opção de pular sequências já vistas.
- "Avançar animação" disponível durante resoluções longas.
- Animações de combos longos podem ser condensadas por configuração.
- Movimento reduzido substitui animação por realce estático.

---

## 11. Critérios mensuráveis (resolução base 1920×1080; valores iniciais a validar)

| Item | Critério |
|---|---|
| Descrição da carta | ≥ 16 px; limite de ~120 caracteres (pt-BR); em estouro, reduzir até o mínimo e mover o excesso para o tooltip |
| Nome da carta | ≥ 18 px |
| Custo | ≥ 28 px |
| Valor da intenção | ≥ 24 px |
| Tooltips | ≥ 16 px |
| Contraste | ≥ 4,5:1 texto; ≥ 3:1 elementos de interface |
| Mão | 10 cartas como alvo; 12 testadas |
| Status visíveis por combatente | 6, depois "+N" |
| Inscrições visíveis na faixa | 12, depois "+N" |
| Resposta ao hover/seleção | ≤ 100 ms |
| Escala de texto | 100% a 150% sem quebra de layout |

---

## 12. Telas de apoio (fora deste documento, necessárias para o loop de "tentar de novo")

- **Preparação:** capacidade em uso, polaridades, inscrições, companions e conhecimento do inimigo.
- **Derrota:** o que foi descoberto na tentativa, com atalho para ajustar o loadout e reiniciar em poucos segundos.
- **Bestiário:** níveis de revelação por inimigo.

---

## 13. Cuidados de implementação

- Evitar painéis permanentes extensos que comprimam o campo ou cubram cartas.
- Manter o cliente visual responsável por apresentação e entrada.
- Validar com: diferentes quantidades de cartas, textos longos, muitos status, cadeias longas de gatilhos, textos em outros idiomas e configurações de cenário.
- Testar a leitura sem cor (escala de cinza) e com simulação de daltonismo.

---

## 14. Decisões assumidas e pontos em aberto

**Assumido nesta versão**
- Timeline removida; o jogo não tem barra de ordem de ações.
- Calor descontinuado; Brasas vivem na carta como contador.
- Companion sem vida própria.
- Custo no canto superior esquerdo.
- Cor permitida apenas onde há função (tags, valor, perigo, foco).

**Em aberto**
- Valores finais de fonte e limites de caracteres após testes de usabilidade.
- Paleta final de tags e glifos.
- Se o companion pode ser atacado (afeta a interface do companion).
- Quantas tags existirão ao lançamento (afeta glifos, silhuetas e a legibilidade).
- Como a aura/resistência elemental aparece (ícone único ou contadores), dependendo do modelo elemental escolhido.