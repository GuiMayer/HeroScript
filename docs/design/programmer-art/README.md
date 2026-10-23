# Programmer art / marco do laboratório

Data: 2026-10-23. Escopo desta entrega: etapas 1–4 do [plano](../../plans/PROGRAMMER_ART_VISUAL_REVAMP_PLAN.md).

## Inventário e propriedade visual

| Aparência | Dono atual | Troca nesta entrega |
| --- | --- | --- |
| Cores e controles nativos | VisualThemeProfile / ViewStyleFactory | Perfil isolado derivado do marfim do laboratório |
| Anatomia, raridade, valores e custo da carta | CardView / CardVisualStyle / CardCostSymbol | Só tinta/acabamento; métricas e símbolos de custo preservados |
| Arte de cartas e objetos | ArtSlot / ArtCatalog / manifestos JSON | Novo catálogo vetorial opt-in |
| Fundo e atores da run | SceneryVisualProfile / CombatSceneryView | Inventariados; substituição prevista na etapa 5 |
| Ornamentação do laboratório | CircuitTrace / CardView.InkKeyline | Mesmos controles e rects; acabamento passivo |
| Mão e animações | CardHandView / CardChoreography / MotionPolicy / CardMotionLab | Congelados |
| Mapa e ícones de nós | MapView / RouteView / MapNodeGlyph | Sem alteração; etapa 6 |
| Diálogo, loja, recompensas, loadout | Views existentes e catálogos específicos | Sem alteração; etapa 6 |
| Menus, configurações, pausa e histórico | Perfis atuais / MenuVisualStyle | Sem alteração; etapa 7 |

`ivory_theme.tres` é a origem do laboratório; `ivory_run.tres` é a origem da run. Fontes Cinzel/Source Sans 3, medidas dos estilos, fonte dos títulos e conteúdo textual permanecem iguais. O catálogo da run já contém SVGs; o problema observado é inconsistência de acabamento e reutilização das mesmas composições.

## Baseline

`tests/programmer_art_lab.gd` instancia a cena real do laboratório, com os componentes reais de mão e carta. Registra 40 casos: três resoluções (1280×720, 1920×1080, 2560×1080), EN/PT, escalas 1 e 1,2, mãos 0/2/5 e quatro presets completos. Cada digest inclui a árvore ordenada de controles, dimensões, mínimos, transformação global, visibilidade, filtro de mouse e foco. Também registra hashes dos executores e configurações de movimento.

O fixture gerado está em `data/labs/programmer_art_baseline.json` no projeto Godot. A captura de referência está em [baseline/lab-1920x1080.png](baseline/lab-1920x1080.png). São dados da árvore local antes do revamp, incluindo correções locais anteriores; não representam uma comparação com outro commit.

Captura inicial renderizada: 42 verificações aprovadas, mais 10 verificações de contrato de temas. O laboratório não enviou requests nem alterou snapshots. O movimento reduzido foi usado somente durante a medição para eliminar entradas transitórias. O código e as configurações de animação de produção não foram alterados.

Como esta entrega fica restrita ao laboratório, as capturas e baselines das telas da run e dos menus serão produzidos antes de suas respectivas etapas. Não se certifica aqui um revamp dessas telas.

## Execução reproduzível

Na raiz do repositório:

```powershell
./tools/test-godot-ui.ps1 -Tests programmer_art_lab,theme_contracts,ivory_lab,card_animation_parity
```

Para capturas, adicionar `-VisualTest programmer_art_lab -VisualOutputDirectory .runtime/programmer-art/after`. A opção `-VisualPhase baseline` é exclusiva da geração de referência antes de mudanças; não deve ser usada para aprovar regressões.

## Diretrizes de produção

SVGs estáticos, cores planas, formas geométricas e três trilhas a 45°. Mapeamentos explícitos por ID e fallback neutro; sem sorteio, API ou interpretação de regras. O número de formas da ilustração é decorativo e não substitui os valores da carta.

O novo perfil é opt-in no laboratório. As configurações e os assets antigos continuam disponíveis para a run. Nenhum recurso já utilizado pelo combate deve ser sobrescrito nesta entrega.

## Etapas 2 e 3

`ProgrammerArtProfile.create(base)` deriva um perfil sem duplicar fontes ou alterar métricas. A paleta fica no perfil, e `CardVisualStyle.keyline_style` seleciona entre a moldura anterior e marcas de registro. `CardView` continua sendo o único componente de carta: a variação muda somente o desenho passivo dentro do mesmo rect. Verificação do perfil: 161 checks de herança, mínimos dos estilos e preservação dos Resources de origem.

O catálogo isolado fica em `data/labs/programmer_art_manifest.json`; os SVGs em `assets/programmer-art`. São 22 famílias, cada uma com três composições de carta 320×150 e um símbolo quadrado 128×128 (88 arquivos), com 94 associações explícitas de cartas. As amostras conhecidas têm associação explícita; conteúdo de mod desconhecido recebe um documento com núcleo geométrico neutro. O nome de placeholder impresso abaixo da arte é preservado.

`tools/build-programmer-art-assets.ps1` contém as receitas vetoriais; `-Check` verifica os arquivos produzidos. Não há geração em runtime, novas dependências ou consumo de RNG. As variações não codificam a quantidade real de acertos/stacks. Os ícones de objetos e atividades ficam preparados no catálogo, sem alterar os consumidores da run nesta etapa. Canvas e preenchimento dos símbolos de custo atuais permanecem intactos.

`programmer_art_catalog.gd`: 312 verificações aprovadas de importação, cobertura das fixtures, fallback, dimensões e preservação do catálogo de cenário. Atores, retratos e fundos continuam com as mesmas associações e arquivos da etapa anterior.
