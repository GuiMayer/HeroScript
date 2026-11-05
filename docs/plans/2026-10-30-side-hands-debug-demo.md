# Side Hands Debug Demo Scene Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Criar uma cena demo interativa e independente (`examples/clash-web/demo-side-hands.html`), sem conexão com a engine HeroScript, contendo um painel flutuante de debug com controles em tempo real para todos os parâmetros geométricos e de perspectiva das mãos laterais espelhadas (esquerda e direita), mantendo a cena do jogo intacta para contexto visual, sem commitar a cena.

**Architecture:** Um arquivo HTML autônomo baseado no layout de `index.html` e `styles.css` renderiza a mesa de jogo estática completa (mesa oval, anel de órbita, monte, descarte central, bot do topo e mão do jogador). Um script embutido gerencia o painel de debug flutuante com sliders reativos, manipulando propriedades CSS dinâmicas e transformações 3D em tempo real para ambas as mãos laterais com sincronização/espelhamento automático e exportação de código CSS/JS.

**Tech Stack:** Vanilla HTML5, CSS3 3D Transforms & Custom Properties, Vanilla JavaScript (ES6+), zero external dependencies, zero backend connection.

---

### Task 1: Preparar o Arquivo Base da Cena Demo

**Files:**
- Create: `examples/clash-web/demo-side-hands.html`

**Step 1: Criar a estrutura HTML da cena estática**
- Importar `styles.css`.
- Incluir toda a estrutura visual do jogo em estado ativo de 4 jogadores (`data-game-mode="freeForAll"`, `data-game-state="playing"`).
- Incluir mão do topo com 7 cartas, centro da mesa com descarte e compras, e mão do jogador com cartas reais.
- Tornar visíveis ambas as mãos laterais (`.side-opponent-left` e `.side-opponent-right`), removendo a classe `.hidden`.

**Step 2: Verificar abertura no navegador**
- Testar carregamento do arquivo localmente em navegador headless/Chrome para certificar que renderiza a arena de 4 jogadores sem erros de console ou requisições de rede com falha.

---

### Task 2: Implementar a Estrutura e Estilização do Painel de Debug

**Files:**
- Modify: `examples/clash-web/demo-side-hands.html`

**Step 1: Criar a marcação HTML do painel flutuante (`#debugPanel`)**
- Criar container flutuante fixo no canto superior direito (`position: fixed; z-index: 1000`).
- Adicionar cabeçalho com botão de minimizar/expandir (`_` / `⚙️`).
- Adicionar 4 grupos de controles com sliders (`<input type="range">`) e displays numéricos de valor (`<span>`):
  1. **Transformação da Mão**:
     - `handRotation`: Rotação no plano Z (`-90°` a `0°`, default: `-50°`)
     - `topPosition`: Posição vertical `top` (`15%` a `70%`, default: `40%`)
     - `sideOffset`: Distância da borda `left`/`right` (`-20px` a `140px`, default: `8px`)
     - `perspective`: Profundidade de perspectiva 3D (`300px` a `2000px`, default: `900px`)
     - `rotateY`: Inclinação 3D para o centro da mesa (`0°` a `80°`, default: `58°`)
     - `rotateX`: Inclinação de elevação 3D (`-30°` a `30°`, default: `-5°`)
  2. **Espaçamento e Dimensões**:
     - `cardCount`: Número de cartas (`1` a `15`, default: `7`)
     - `cardOverlap`: Espaçamento/overlap entre cartas (`-50px` a `20px`, default: `-12px`)
     - `fanWidth`: Largura do container do leque (`140px` a `360px`, default: `240px`)
     - `cardScale`: Escala das cartas laterais (`0.6` a `1.3`, default: `1.0`)
  3. **Geometria Individual das Cartas no Leque**:
     - `cardAngle`: Rotação individual progressiva (`0°` a `25°`, default: `0°`)
     - `cardArc`: Elevação parabólica no arco (`-30px` a `30px`, default: `0px`)
     - `cardDepthZ`: Profundidade Z por carta (`0px` a `30px`, default: `10px`)
     - `cardAngleY`: Rotação Y individual por carta (`0°` a `15°`, default: `4°`)
  4. **Ações e Presets**:
     - Checkbox "Espelhar automaticamente (Direita = -Esquerda)" (default: marcado)
     - Presets: *Atual (-50°)*, *Horizontal (0°)*, *Arco Suave (-35°)*, *Arco Fechado (-65°)*
     - Botão "Copiar CSS"
     - Botão "Copiar JS"
     - Botão "Resetar"

**Step 2: Estilizar o painel de debug**
- Adicionar estilos dedicados em tag `<style>` dentro de `demo-side-hands.html`.
- Usar estilo dark glassmorphism coerente com o Clash (fundo escuro semi-transparente, bordas sutis amarelas, tipografia compacta, sliders customizados com feedback visual imediato).

---

### Task 3: Implementar o Motor Reativo de Atualização em Tempo Real (JS)

**Files:**
- Modify: `examples/clash-web/demo-side-hands.html`

**Step 1: Criação dinâmica de cartas**
- Implementar função `renderFanCards(fanElement, count)` que popula `#leftHandFan` e `#rightHandFan` com elementos `.ai-mini-card`.

**Step 2: Função de aplicação de parâmetros (`applyParams()`)**
- Ler valores de todos os sliders.
- Calcular os valores espelhados para o lado direito:
  - Rotação: `left = -rot`, `right = +rot`.
  - RotateY: `left = +rotY`, `right = -rotY`.
  - RotateX: `left = rotX`, `right = rotX`.
  - Side offset: `left = offset`, `right = offset`.
- Aplicar transformações nos containers `.side-opponent` e `.side-hand-perspective`:
  ```javascript
  leftOpponent.style.top = `${params.topPosition}%`;
  leftOpponent.style.left = `${params.sideOffset}px`;
  leftPerspective.style.transform = `translate(-50%, -50%) rotate(${params.handRotation}deg) perspective(${params.perspective}px) rotateX(${params.rotateX}deg) rotateY(${params.rotateY}deg)`;
  
  rightOpponent.style.top = `${params.topPosition}%`;
  rightOpponent.style.right = `${params.sideOffset}px`;
  rightPerspective.style.transform = `translate(-50%, -50%) rotate(${-params.handRotation}deg) perspective(${params.perspective}px) rotateX(${params.rotateX}deg) rotateY(${-params.rotateY}deg)`;
  ```
- Atualizar transformações individuais de cada carta com base em `norm = (index - (count - 1)/2) / ((count - 1)/2)`:
  - `translateZ`, `rotateY`, `rotateZ`, `translateY`.
- Atualizar overlaps (`--bot-card-overlap`) e larguras (`width`).

**Step 3: Event Listeners e Display em Tempo Real**
- Conectar evento `input` em todos os sliders para atualizar valores nos labels e aplicar mudanças instantaneamente sem lag.
- Conectar botões de Presets para aplicar configurações prontas em 1 clique.
- Conectar botões de "Copiar CSS" e "Copiar JS" que formatam o código exato e copiam para o clipboard do usuário com toast de confirmação.

---

### Task 4: Validação Visual e Isolamento do Repositório

**Files:**
- Check: `examples/clash-web/demo-side-hands.html`
- Check: `.gitignore` (garantir que `demo-side-hands.html` ou arquivos temporários de demo não sejam commitados)

**Step 1: Testar o carregamento standalone**
- Executar teste de carregamento no Chrome headless via script sem backend ativo.
- Capturar screenshot da cena demo com o painel aberto.

**Step 2: Confirmar integridade do git**
- Verificar que `git status` não inclui arquivos não autorizados e que a cena demo permaneça estritamente local/não commitada.
