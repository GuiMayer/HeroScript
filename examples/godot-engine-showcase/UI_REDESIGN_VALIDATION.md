# Redesign funcional — evidências de implementação

Data: 2026-11-22. Escopo: MVP (`prototype-test`) e Alisyum. Referência: [plano original](PLANO_UI_LEGIBILIDADE_USABILIDADE_GAME_UI_DATABASE.md).

As verificações usam o Godot 4.7.2, contextos de apresentação e projeções determinísticas. Os testes de persistência que exigem preferências graváveis usam um diretório de usuário temporário. O executor de captura de menu usa SubViewport com dimensões de pixel verificadas; os outros usam a renderização real das cenas de teste. As capturas ficam em `.runtime/ui-redesign/`, ignorado pelo Git, para não introduzir imagens de diagnóstico no pacote do jogo.

## Matriz de etapas

| Etapa | Mudança | Evidência |
|---|---|---|
| 0 | Baseline e executor de resolução efetiva | `ui_viewport_capture_contract`: 18 verificações; `00-baseline/` |
| 1 | Tokens, contraste semântico, escala de texto até 200%, persistência | `ui_readability_contracts`, `settings_contracts`; `01-typography/` (150%) |
| 2 | Cabeçalho fixo de duas linhas, regras maiores, menos repetição, custos alternativos com espaço, identificação de runas | `card_titles`, `card_face_rows` (816), `card_presentation`, `card_cost_symbols` (35); `02-cards/01-forge-before.png` |
| 3 | Leque mais baixo e faixa maior entre cartas a partir de nove instâncias, sem perder a interação da mão convencional | `reference_hand_geometry`, `hand_ux`, `mvp_status_panel` (40), `rune_chain_presentation`; `03-combat/`, `08-certification/mvp/` |
| 4 | Filtro de estoque por categoria, contador de ofertas, cabeçalho mais curto, ações fixas fora da rolagem | `shop_activity_recovery` (8 em headless), `activity_reconciliation` (23), `reward_choice` (13), `ui_shop_stock_contract`; `04-shop/`, `08-certification/all-stock.png`, `08-certification/runes-bottom.png` |
| 5 | Comparação antes/depois, painel com rolagem em janela pequena, inventário de runas mais compacto | `rune_workbench_drag`: 5 verificações sem renderizador; teste de arraste com captura nas duas resoluções, inclusive 960×540; `05-forge/` |
| 6 | Orientação explícita para próximo destino publicado, inscrições legíveis | `map_completion_status`, `map_selection`, `map_certification`, `unified_loadout_layout`, `unified_loadout_autosave` (13); `06-map-loadout/` |
| 7 | Menu empilhado em janela estreita/escala grande, botões com quebra de linha, diálogos mais compactos e navegação por foco | `ivory_menu`, `dialogue_reading` (isolado), `ivory_run_visual`, `ui_viewport_capture_contract` a 200%; `07-menus/`, `08-certification/menu-200/` |
| 8 | Certificação adicional: filtro fixo e 17 ofertas, legenda de custo fora do numeral, regressões de acessibilidade | `ui_shop_stock_contract`, `card_cost_symbols` e reexecução de contratos acima; `08-certification/` |

## Verificações concretas

- Mão: 0 a 20 por geometria; foco até 40/100 instâncias pelos casos de UX existentes. Mudar quantidade não desloca a HUD; modo leitura não sobrepõe cartas. O tamanho e custo reais são provenientes da projeção. A regressão final detectou interferência entre uma mão convencional e os controles à direita quando o novo leque era aplicado universalmente; por isso a mudança é restrita às mãos densas, preservando a geometria validada das mãos comuns.
- HUD: cinco status visíveis, contador de excedentes, inspeção focável, Calor da Runa separado, ausência de estado não inventa valor zero; valores largos, números e novos status não alteram a caixa reservada.
- Loja: cinco cartas e doze runas, filtros com contagem, saída e filtro imóveis durante a rolagem, tentativa rejeitada seguida de conclusão sem cobrança extra. Os modelos permanecem imutáveis.
- Forja: arraste por eventos de ponteiro no Godot renderizado, destino incompatível não envia comando; a referência é a instância exata, e a geração antiga é recusada. Comparação cabe na janela de 960×540 com ações de confirmar/cancelar acessíveis.
- Mapa: inspeção não viaja, viagem explícita usa referência publicada, estados corrente e concluído são independentes; alternativas publicadas no painel sem inventar destinos.
- Menus: 1280×720, 1280×800, 1920×1080 e ultrawide em EN/PT-BR a 100–120%; capturas de menu separadas em 960×540 a 2560×1080 e escala 200%, com rolagem quando necessária.
- Regressão MVP adicional: `mvp_reference_base` (113), `mvp_reference_full_composition` (478), `mvp_reference_energy_and_turn` (147 após a correção da geometria) e `combat_mvp_preview` (29). Nesta árvore local, o Godot emite avisos de proprietário dos nós de playback em `combat_mvp_view.gd`, arquivo previamente modificado; esses avisos não impediram as verificações e não foram incluídos neste conjunto de commits.

## Limites da certificação

Não equivale a um playtest humano de duas builds completas. A campanha real, seus quatro personagens e a aplicação de runas pela loja em partidas completas ainda exigem sessão de QA com o catálogo/API reais. As capturas sintéticas não comprovam que todo texto de todas as telas permaneça dentro da viewport a 200%. O menu continua utilizando o seletor de campanhas publicado pelo contrato atual; não foi introduzido um seletor de identidade que suponha IDs ou dados de save. A arte não foi redesenhada.

Não foram incluídas mudanças de combate, conteúdo Alisyum ou arquivos temporários de planilha que já estavam no diretório de trabalho antes da tarefa. Parte dos fixtures de combate também já tinha alterações locais, portanto o resultado de suas verificações adicionais deve ser interpretado com esse contexto.

## Reprodução

Na raiz do repositório:

```powershell
$project = "examples/godot-engine-showcase"
$capture = (Resolve-Path $project).Path + "/.runtime/ui-redesign/08-certification"
godot --path $project --rendering-method gl_compatibility --script res://tests/ui_shop_stock_contract.gd -- --layout-smoke "--capture=$capture"
godot --path $project --rendering-method gl_compatibility --script res://tests/rune_workbench_drag.gd -- --layout-smoke --compact "--capture=$capture/forge"
godot --path $project --rendering-method gl_compatibility --script res://tests/ui_viewport_capture_contract.gd -- --layout-smoke --scale=2.0 "--capture=$capture/menu-200"
godot --headless --path $project --script res://tests/mvp_status_panel.gd -- --layout-smoke
```
