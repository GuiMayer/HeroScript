# Runas de sobrescrita — entrega e validação

Data: 22/11/2026. Escopo: Alisyum e MVP (`prototype-test`), sem alterar a partida do autor. As mudanças anteriores de HUD, gambits e arquivos temporários permaneceram fora dos commits desta entrega.

## O que entrou

- Alisyum: Brasa, Geada, Geada Reescrita, Fragmentação, Ruptura, Transbordo, Ressonância e Catalisador. Sobrescritas são compostas por instância nos slots e pipelines já suportados pelo HeroScript. Duas substituições concorrentes do mesmo resíduo não podem coexistir; modificar uma cópia não modifica as outras. Fragmentação distribui o orçamento de impacto, não multiplica gratuitamente o dano.
- MVP: Brasa, Maré, Fragmentação e Ressonância, usando o vocabulário e os pipelines do pacote MVP.
- Descoberta: forja inicial opcional e gratuita, segunda oportunidade após a primeira recompensa e outras ofertas durante as quatro jornadas de Alisyum; rota antiga continua possível. O MVP recebe uma oportunidade inicial e opções em forjas existentes.
- Bancada: seleção por clique/teclado ou arrastar diretamente um token de runa sobre o CardView. Ícones distinguem afinidade e comportamento; contorno indica compatibilidade, carta incompatível escurece. A prévia usa a identidade publicada e a confirmação aplica somente uma escolha legal. Vínculo permanece definitivo neste ciclo.
- Apresentação: marcadores de runa por transformação (sem repetir um mesmo identificador nos traces), comparação antes/depois, e banner causal sobre o combate baseado nos registros recebidos da engine. Sem reparentear mão ou unidades.
- Menus: linguagem de jornada no menu principal, destaque de Continuar quando há save, ocultação de laboratórios e diagnóstico no fluxo normal, placas com acento quente em ofertas/forja, preparo, mapa, diálogo, pausa e fim de jornada. Não há arte de personagem nova.

## Gates automáticos executados

| Gate | Resultado |
| --- | --- |
| Core.Tests: transformações, inventário, condensação, continuação, respostas e ledger | 214 aprovados, 0 falhas |
| API.Tests: `AlisyumRuneCompositionTests` | 3 aprovados, 0 falhas |
| Abertura Alisyum: adquirir runa, prévia, aplicar à instância, seguir ao combate, verificar replay | Aprovado em API isolada |
| Cadeia real Brasa → Burn → Ressonância → Block → Catalisador → compra | Executor real em estado candidato isolado; registros confirmados e hash da run original preservado |
| MVP: resíduo Burn substituído por Wet, mantendo pipeline do pacote | Aprovado |
| Colisão Ruptura/Geada Reescrita nas duas ordens | Rejeitada em composição |
| Godot: `rune_presentation`, `rune_chain_presentation`, `rune_workbench_drag` | Aprovados |
| Godot: `pattern_workshop_screen`, `pattern_workshop_inventory`, `card_modifier_items`, `unified_loadout_autosave`, `map_navigation`, `reward_choice` | Aprovados |
| Godot: `ivory_menu`, `ivory_menus`, `ivory_menu_visual` | Aprovados (1280×720, 1280×800, 1920×1080, 2560×1080; EN e PT-BR, escalas 1 e 1,2 no menu principal) |
| Sandbox recém-publicado: API `/diagnostics/mcp-sandbox` e revisões Alisyum/MVP | Isolamento confirmado; ambos os pacotes publicados e acessíveis |

O teste de composição de cadeia usa o executor real em uma cópia candidata da run de teste, com as peças em mão. Isso comprova a interação e isolamento; não é um replay de uma partida inteira com essa build. O replay verificado corresponde à aquisição e vínculo no fluxo real da API. A regressão de recompensas (tamanho das cartas em fileira) foi evitada preservando o layout previamente validado.

## Capturas reproduzíveis

Diretório local ignorado pelo Git: `.runtime/rune-workbench-validation/`, sob o projeto Godot. Os arquivos são produzidos pelo renderer OpenGL do Godot (não mockups). O teste de bancada usa dados controlados para provar o gesto nativo sobre cartas visualmente iguais; a confirmação mostrada na captura usa uma fixture de fatos antes/depois, enquanto a integração acima prova a aplicação autoritativa da runa.

- `01-forge-before.png`: inventário, duas cópias da carta.
- `02-forge-dragging.png`: token e nome acompanham ponteiro, alvo compatível recebe contorno dourado, alvo incompatível é atenuado.
- `03-forge-drop-selected.png`: soltar escolhe exatamente a instância autorizada.
- `04-forge-comparison.png`: 6 de dano original versus 6 repartidos em três impactos.
- `05-forge-incompatible.png`: arraste sobre alvo incompatível; não deve emitir comando.
- `menus/main/alisyum-pt_BR-1920x1080.png`: campanha Alisyum sem botões técnicos.
- `menus/main/alisyum-continue-pt_BR-1920x1080.png`: ação Continuar em destaque.
- `menus/main/captures.json`: matriz e lista das capturas do menu principal.

Reprodução a partir da raiz do repositório:

```powershell
$project = 'examples/godot-engine-showcase'
$captures = [IO.Path]::GetFullPath("$project/.runtime/rune-workbench-validation")
godot --path $project --rendering-method gl_compatibility --script res://tests/rune_workbench_drag.gd -- --capture=$captures
godot --headless --path $project --script res://tests/rune_workbench_drag.gd
dotnet test tests/API.Tests --no-restore -p:BuildProjectReferences=false --filter FullyQualifiedName~AlisyumRuneCompositionTests
dotnet test tests/Core.Tests --no-restore -p:BuildProjectReferences=false --filter 'FullyQualifiedName~CardTransformation|FullyQualifiedName~CardModifierItem|FullyQualifiedName~Condensation|FullyQualifiedName~EffectContinuation|FullyQualifiedName~GameplayResponse|FullyQualifiedName~PriorityResponseLedger'
```

## Limites para o aceite de produto

- Vínculos móveis/swap são deliberadamente opcionais no plano e não foram implementados.
- A expansão usa as cartas básicas compartilhadas nas quatro identidades; a reescrita dedicada a Magma Slash fica nas ofertas do Piromante. Não há ainda catálogo especializado por identidade.
- A matriz visual completa de todas as telas em todas as resoluções, uma partida completa de cada rota, teste conectado de ponta a ponta da interface, medição p95 de latência e teste humano de diversão ainda são gates de produto, não resultados demonstrados por estes testes. Não confundir fixture e screenshot com uma campanha jogada.
- O conector MCP configurado retornou `API_UNAVAILABLE` mesmo após o sandbox dedicado ficar pronto em 5280. Os testes de composição e replay foram executados diretamente na engine/API isolada; o renderer real produziu a evidência visual. A comunicação pelo MCP ainda necessita diagnóstico próprio.
- Não certificar o feeling do jogo nem a força relativa das builds a partir de heurísticas. A primeira avaliação humana deve tentar duas construções e comparar tempo até a primeira transformação, clareza de disparos e escolhas de sinergia.
