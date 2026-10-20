# Shaders simples para o protótipo Godot

Data: 2026-10-20. Escopo: auditoria do código atual e pesquisa técnica; **nenhum efeito foi implementado nesta revisão**.

Atualização posterior: os três efeitos P1 foram implementados **somente para validação no laboratório**, com componentes compartilhados. Ver [laboratório de efeitos](ART_EFFECT_LAB.md). O inventário e as recomendações abaixo preservam o diagnóstico anterior à implementação; o combate ainda não dispara os novos efeitos automaticamente.

## 1. Conclusão

Há espaço para melhorar a experiência com poucos shaders locais, principalmente para **confirmar impactos, destacar alvos e comunicar alterações**. Não recomendo um pós-processamento permanente de tela nem transformar todas as cartas em superfícies animadas.

Primeiro conjunto recomendado:

1. Flash localizado de impacto na arte do ator.
2. Contorno de alvo válido/selecionado, sem modificar sua área clicável.
3. Reflexo breve na arte ao receber um upgrade/modificador ou equipamento.

Depois: pulso de recurso, revelação de arte e uma variante visual de condensação. O objetivo é facilitar a leitura, não acrescentar espera às ações.

Essas são recomendações de design para este projeto, não resultados de benchmark. A compatibilidade prática e o custo precisam ser certificados na Godot renderizada.

## 2. Estado atual verificado

| Elemento | Situação no código | Consequência |
|---|---|---|
| Renderizador | `project.godot`: `gl_compatibility`, inclusive configuração mobile | Priorizar CanvasItem 2D e evitar depender de recursos exclusivos de Forward+ |
| Shaders próprios | Foi encontrado apenas `shaders/card_burn.gdshader` | A maior parte do feedback atual usa controles, desenho e Tweens |
| Queima | `card_burn_view.gd`: captura GPU da CardView em SubViewport transparente, atualização única, saída TextureRect; `progress` explícito | Já há uma referência reutilizável para efeitos sobre a carta inteira |
| Captura da carta | Renderização em 2× a dimensão da carta | Boa nitidez, mas quadruplica a área em pixels frente à resolução 1×; não copiar esse custo para toda a mão permanentemente |
| Transparência da queima | Shader usa `blend_premul_alpha` | Novos efeitos sobre essa captura precisam preservar o contrato de alpha |
| Impacto atual | `ArtSlot.flash_hit()` usa `modulate` e Tween | Melhorar esse ponto é mais simples do que criar outro sistema de feedback |
| Orquestração | `CombatFeedback`, `SemanticCues`, `Playback`, handles e política de movimento existentes | Novos efeitos devem se integrar a esses donos, não criar uma segunda fila |
| Arte e interação | `CombatSceneryView` guarda as artes; controles de combate fornecem anchors e superfícies de interação | Aplicar shader na arte/overlay, preservando hitboxes e layout |
| Configuração visual | `VisualContext`, `CardVisualStyle`, `SceneryVisualProfile`, manifesto e perfis de movimento | São os pontos de extensão; regras continuam na engine |

A busca por ShaderMaterial/arquivos `.gdshader` é uma auditoria estática do workspace, incluindo alterações locais pendentes. Não representa uma varredura de todos os shaders internos gerados pela Godot.

CanvasItem shaders atendem aos elementos 2D e à GUI. Para estes efeitos, um `TextureRect` ou `Sprite2D` com ShaderMaterial normalmente basta; não é necessário mudar a engine REST. [Referência oficial](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/canvas_item_shader.html).

## 3. Catálogo priorizado

Custos abaixo são relativos e estimados pelo tipo de operação, área coberta e amostras de textura, não medições de GPU.

| Prioridade | Efeito | Aplicação recomendada | Técnica simples | Custo/risco estimado |
|---|---|---|---|---|
| P1 | Flash de impacto | Arte do ator atingido | Mistura da cor original com tinta clara/avermelhada, preservando alpha | Baixo; geralmente uma amostra da textura |
| P1 | Contorno de alvo | Silhueta do ator válido ou selecionado | Comparar alpha com 4–8 vizinhos | Baixo a médio; exige margem para não recortar o contorno |
| P1 | Reflexo de melhoria | Arte da carta/oferta/inscrição após mudança confirmada | Faixa diagonal estreita, animada por `progress` | Baixo; não atravessar texto nem repetir continuamente |
| P2 | Pulso de recurso | Barra, símbolo ou halo do ator | Máscara radial/linear com amplitude decrescente | Baixo; depende de uma indicação visual publicada/configurada |
| P2 | Revelação de tinta | Entrada/criação de arte de carta | Máscara por gradiente + textura de ruído existente | Baixo na arte; médio se capturar toda a carta |
| P2 | Condensação gráfica | Ícone de stacks e ponto do proc confirmado | Máscara de ruído convergindo, seguida por um pulso | Médio; coordenar com a remoção real dos stacks |
| P2 | Dessaturação seletiva | Arte de item indisponível ou ator derrotado | Mistura com luminância + contraste ajustável | Baixo; não pode ser o único indicador de estado |
| P3 | Pulso em circuitos | Caminho escolhido no mapa ou ornamentação de uma confirmação | Máscara do traçado + faixa móvel | Baixo/médio; desenho nativo pode ser melhor |
| P3 | Microdistorção | Região da arte de um efeito especial | Deslocamento UV pequeno, com máscara e envelope | Médio; evitar em números, fontes, bordas e tela inteira |

### 3.1 Flash de impacto: maior retorno imediato

O flash deve tornar o ator atingido identificável sem mudar sua posição ou clarear a tela inteira. A mistura de cor em shader permite controlar separadamente a intensidade e a textura original; é uma alternativa ao `modulate` atual, não uma justificativa para manter dois flashes simultâneos.

- Aplicar no `ArtSlot` do ator, não no container que inclui vida, textos e intenções.
- Cores por papel visual e perfil; crítico só recebe tratamento especial se houver um fato de crítico disponível, nunca deduzido pelo tamanho do dano.
- Sugestão inicial para avaliação: acento de 100–180 ms, **dentro da janela visual já existente**, sem acrescentar outro passo de espera.
- Multihit: reiniciar/mesclar um envelope limitado em vez de empilhar materiais e flashes.
- Não atribuir um flash de dano a todo decréscimo de resource: pagamento, perda de recurso e impacto continuam distintos conforme fatos/cues disponíveis.

### 3.2 Contorno: clareza de alvo, sem mexer na seleção

Usar uma linha fina e estável, próxima da linguagem gráfica Marfim. Diferenciar alvo disponível de alvo selecionado por espessura/símbolo, além da cor. Não animar todos os inimigos em loop.

Um contorno de alpha segue a silhueta **apenas se a textura tiver transparência apropriada**. Numa imagem retangular opaca ele contornará um retângulo; nesse caso, usar marcador/anel nativo ou uma máscara de arte. Também é preciso compensar escala e resolução para que a largura visual permaneça consistente.

O shader não altera a geometria de input. Hover, drag e confirmação continuam usando as superfícies atuais. Não usar deslocamento de vértices para “levantar” cartas: isso pode separar visual e hit test e reintroduzir problemas de seleção.

### 3.3 Reflexo de melhoria: não confundir com identidade da carta

Uma faixa breve pode confirmar que uma carta foi transformada ou uma inscrição equipada. Aplicar **à janela da arte**, não aos custos e valores. Evitar reflexo constante de “raridade”: a raridade já tem dono e representação na borda/tooltip.

Disparar só após aceitação e mudança do modelo. Refresh REST, troca de idioma, resize ou reaplicação do tema não podem reproduzir a confirmação. Duração sugerida de 200–350 ms para experimentar no laboratório, sem alterar os tempos de produção agora.

### 3.4 Pulso de recurso e condensação: feedback, não regra

Um contorno/halo curto pode comunicar recuperação ou ganho de um recurso defensivo. O mapping deve ser visual e configurável; a UI não deve decidir que todo resource chamado `block` é escudo, nem que `health` define morte.

Para condensação, a imagem pode sugerir stacks convergindo e um único proc. Isso deve funcionar para qualquer efeito acumulável, não só dano. Quantidade consumida, resultado e alvos vêm da engine. Se a projeção não publicar informação suficiente, usar um feedback genérico; não reconstruir a regra no shader ou no cliente.

### 3.5 Revelação, dessaturação e circuitos

- **Revelação:** reutilizar o ruído da queima, mas usar um executor/variante de apresentação explícito. Criação, compra e exílio não devem ficar visualmente indistinguíveis. Texto permanece estável/legível durante a revelação da arte.
- **Dessaturação:** bom apoio para derrota/indisponibilidade; preservar rótulo, marcador e explicação. “Não jogável agora” não significa “descartada”, e ofertas ainda devem permitir inspeção.
- **Circuitos:** um pulso único após viajar ou confirmar é coerente com o estilo. Para linhas vetoriais do mapa, começar com desenho/gradiente nativo; só adicionar shader se a máscara/UV do traçado justificar. Não implementar um fundo inteiro de linhas cintilantes.

## 4. Onde não vale a pena usar shader

| Necessidade | Preferência |
|---|---|
| Borda de raridade e foco de controles | Theme/StyleBox; já têm política própria |
| Centralização e leque da mão | Geometria e Tween existentes |
| Nó atual + concluído | Indicadores/textos atuais; shader não resolve semântica de estado |
| Número flutuante, duração ou scaling | Label/RichTextLabel e dados publicados |
| Escurecer fundo da pausa | Scrim ColorRect; não exige ler a tela |
| Papel/granulação estática | Textura pequena estática, sem ruído procedural animado |
| Sombra simples da carta | Arte/estilo estático; evitar várias amostras de blur por carta |

Evitaria nesta fase: aberração cromática, CRT, glitch contínuo, vinheta forte, blur durante gameplay e refração global. São estilos pouco alinhados ao Marfim, podem prejudicar acessibilidade e não resolvem os problemas principais de leitura.

## 5. Escolha de nodes e materiais

### Arte isolada: caminho padrão

Usar `ShaderMaterial` no TextureRect existente, com parâmetros como `amount`, `progress`, `tint`, `width_px` e `texture_size`. Compartilhar o recurso Shader; manter parâmetros independentes por instância quando as animações diferirem.

Alterar um ShaderMaterial compartilhado afeta todos os consumidores. A documentação oferece uniformes por instância ou duplicação do material; para este projeto, a opção conservadora é material independente por alvo ativo e shader compartilhado. Uniformes por instância só devem ser adotados após validar a versão/backend usado. [ShaderMaterial](https://docs.godotengine.org/en/stable/classes/class_shadermaterial.html).

### Carta inteira: somente quando necessário

Material no Control pai não converte automaticamente uma árvore de Labels, painéis e ícones em uma textura única. Para um efeito que precise deformar/dissolver a face inteira, reutilizar o padrão de captura GPU existente, com vida curta. Não fazer readback de pixels para CPU no caminho do input.

SubViewport permite atualização única e desativação posterior, adequadas à captura estática. Isso não elimina seu custo de memória/renderização. Para efeitos contínuos na mão, preferir o material apenas na arte ou num overlay recortado. [SubViewport](https://docs.godotengine.org/en/stable/classes/class_subviewport.html).

### CanvasGroup e screen texture: exceções

CanvasGroup agrupa filhos, mas usa backbuffer e um shader próprio; substituir esse material exige preservar sua composição. A documentação também registra conflito com filhos usando `clip_children`. Não trocar as cartas atuais por CanvasGroup em massa sem teste específico. [CanvasGroup](https://docs.godotengine.org/en/stable/classes/class_canvasgroup.html).

Shaders com `hint_screen_texture` exigem cópia de tela/backbuffer; efeitos sobrepostos têm regras de ordem e podem precisar de BackBufferCopy. Logo, um blur curto não é “gratuito” só por ter poucas linhas de código. Nesta fase, **nenhum dos três efeitos P1 precisa ler a tela**. [Screen-reading shaders](https://docs.godotengine.org/en/stable/tutorials/shaders/screen-reading_shaders.html).

## 6. Integração sem regressões

1. **Sem mudança na engine:** todo shader proposto é apresentação Godot. Nenhum novo comando REST é necessário para os três primeiros efeitos. O cliente usa apenas fatos/estados já publicados.
2. **Configuração por dono:** cores/intensidades visuais em Resources/perfis; duração e habilitação na política visual capturada. Não inserir valores de regra dentro dos materiais.
3. **Cursor único:** playback e handlers existentes disparam os executores. Nenhum shader determina quando uma ação se torna válida ou altera o journal.
4. **Sem executor, sem tempo:** efeito desabilitado, duração zero, alvo ausente, movimento reduzido ou perfil instantâneo não podem criar Timer, espera por snapshot ou bloqueio vazio.
5. **Caudas cosméticas:** impacto/reflexo/halo não acrescentam latência serial ao uso da carta. Se já existe uma janela, usar essa janela; término/limpeza não devem segurar a próxima ação desnecessariamente.
6. **Relógio controlado:** usar `progress`/`amount` explícitos para efeitos que pausam. `TIME` da Godot não para com a pausa da árvore; não assumir que `PROCESS_MODE_PAUSABLE` congela um shader baseado em TIME. [CanvasItem shader reference](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/canvas_item_shader.html).
7. **Limpeza:** cancelamento, skip, fim de alvo, troca de cena/branch e dispose restauram parâmetros ou removem o overlay; nada pode deixar um ator tingido ou uma carta invisível.
8. **Imutabilidade:** shader/executor não modifica modelos, snapshots ou atributos. Ruído cosmético não consome RNG da engine. Aparência não precisa ser pixel a pixel determinística entre GPUs; a run precisa permanecer semanticamente idêntica.
9. **Acessibilidade:** sem flashes brancos de tela inteira, loops rápidos ou depender só de vermelho/verde. Movimento reduzido deve remover varreduras/distorções e manter um indicador estático compreensível.

## 7. Desempenho e validação propostos

Compatibilidade é o ponto de partida, não promessa de desempenho. A documentação alerta que a precompilação moderna de pipelines não se aplica ao Compatibility; o aquecimento recomendado envolve desenhar os materiais por ao menos um frame durante carregamento. Só `preload()` não deve ser tratado como garantia contra o primeiro engasgo. [Compilação de shaders](https://docs.godotengine.org/en/stable/tutorials/performance/pipeline_compilations.html).

### Critérios antes de aprovar

- Começar no laboratório com os **mesmos componentes, materiais e executores** que irão ao combate; não fazer uma versão demonstrativa separada.
- Comparar efeito desligado/ligado, em build exportada, no primeiro uso e com cache quente.
- Registrar CPU/GPU frame time, p95/p99, draw calls, materiais/capturas vivos e memória; não apenas FPS médio.
- Meta inicial sugerida: manter 60 FPS nas máquinas-alvo e overhead incremental p95 de GPU abaixo de 1 ms para o conjunto P1. **Meta a medir**, não resultado já obtido.
- Exercitar 2 e 10+ cartas, múltiplos atores, impactos consecutivos, janela/resize, 1280×720, 1920×1080 e 2560×1080; também fonte ampliada e alto contraste.
- Medir tempo input → feedback separadamente de REST e duração artística. Shader não acelera endpoint lento.
- Testar pausa/retomada, skip, instantâneo, movimento reduzido, fechamento, restauração de material e ausência de alvo. Nenhuma espera residual quando não houver animação.
- Verificar duas instâncias com progressos diferentes, sem “contaminação” por material compartilhado.
- Certificar alpha premultiplicado, textos nítidos, bordas sem clipping e espessura consistente sob escala.
- Rodar o fluxo real de combate/replay numa API isolada: hashes/estados e resultados permanecem iguais com efeitos ligados e desligados. Não usar saves pessoais.

## 8. Ordem sugerida de trabalho

1. Adicionar ao laboratório uma seleção de efeitos por categoria e parâmetros limitados, reutilizando o seletor existente; baseline sem shader.
2. Implementar e certificar flash de impacto local; substituir o flash antigo, sem duplicação.
3. Validar máscaras/transparência das artes e integrar contorno de alvo; conservar os testes de hover/drag/click.
4. Integrar reflexo de melhoria somente a mudanças confirmadas, com limpeza e deduplicação.
5. Avaliar pulso de recurso e condensação; descartar variantes que atrapalhem números/intenção.
6. Medir o conjunto no combate real; só então considerar revelação de tinta e circuitos.

Não recomendo começar por blur, auras permanentes ou um “shader universal” cheio de opções. Três efeitos pequenos, opcionais e bem integrados oferecem mais valor e menor risco para este protótipo.

## 9. Arquivos locais de referência

- `examples/godot-engine-showcase/project.godot`
- `examples/godot-engine-showcase/shaders/card_burn.gdshader`
- `examples/godot-engine-showcase/scripts/ui/card_burn_view.gd`
- `examples/godot-engine-showcase/scripts/ui/card_burn_motion.gd`
- `examples/godot-engine-showcase/scripts/ui/art_slot.gd`
- `examples/godot-engine-showcase/scripts/ui/components/combat_scenery_view.gd`
- `examples/godot-engine-showcase/scripts/ui/components/combat_feedback.gd`
- `examples/godot-engine-showcase/scripts/presentation/semantic_cues.gd`
- `examples/godot-engine-showcase/scripts/presentation/motion_policy.gd`
- `examples/godot-engine-showcase/scripts/ui/card_visual_style.gd`
- `examples/godot-engine-showcase/scripts/ui/styles/scenery_visual_profile.gd`
- `examples/godot-engine-showcase/data/motion_manifest.json`
- `docs/design/GODOT_RUN_GRAPHICS_CONTRACTS.md`
- `docs/design/GODOT_VISUAL_PROFILES.md`
- `docs/design/CARD_EXILE_ANIMATION.md`

Pesquisa baseada na documentação oficial da Godot consultada nesta data. As páginas `stable` são móveis: conferir novamente contra a versão efetivamente distribuída ao implementar. Não foi copiado código de packs nem introduzida dependência externa; os custos e tempos sugeridos são hipóteses de projeto a validar.
