# Validação das animações de cartas inspiradas no UNO

Data: 26/10/2026. Escopo: Godot, laboratório e apresentação de combate/replay.

## Resultado

Implementados pose global compartilhada, curvas temporais normalizadas, acomodação dentro do ciclo, retargeting de hover, arraste com ponto de pega e inclinação filtrada pelo tempo, retorno ao leque, continuidade até o recibo, realce visual de alvos já publicados, compra em curva e cancelamento seguro. O laboratório utiliza os mesmos executores e permite aplicar Swift/Smooth/Linear pelo seletor em cascata existente.

O último refinamento mantém a carta arrastada fora do ScrollContainer enquanto aguarda o comando, evitando clipping temporário. Rejeição devolve a carta com animação; aceitação transfere a mesma instância visual ao registro de trânsito. Nenhum novo cálculo, regra ou comando de gameplay foi introduzido.

## Verificações executadas

- Suíte ampla em cópia isolada: **24 execuções, 512 checks aprovados**, incluindo renderização em 1920×1080. Cobriu pose, curvas, mão, seleção, paridade, compra, turnos, limites, planner, director, envelopes, histórico e controles do laboratório.
- Depois do último ajuste de clipping: **82 checks aprovados** em `uno_motion_pending` (5), `uno_card_drag` (8), `animation_live` (40) e `animation_online` (29).
- API real isolada em `http://127.0.0.1:61989`: comandos nos settings default, ascendant e volatile-core; arraste no Ascendant; histórico autorizado; verificação canônica do replay. A apresentação não alterou o estado publicado nem enviou um segundo comando.
- Exportação da árvore preparada para commit: importação e testes básicos das etapas passaram independentemente das alterações não relacionadas do workspace. A primeira importação limpa sofreu um encerramento nativo durante importação de fonte; a repetição e a importação incremental passaram. Não houve correção de código para mascarar esse encerramento.
- Renderizações adicionais com limites de 30 e 120 FPS: 15 checks por execução, com retornos, compra, uso, retenção e queima. Capturas inspecionadas do leque de duas cartas e da compra em ultrawide.
- Sem alterações nos arquivos de regras da engine ou no conteúdo de gameplay nesta implementação. Snapshots/fatos de entrada foram comparados antes/depois; as verificações de replay real retornaram válidas.

Os contadores acima são por execução e incluem verificações repetidas entre suítes; não representam quantidade de testes unitários únicos.

## Tempos observados

Execução local com Godot 4.7.2, renderizador Compatibility/OpenGL, Ryzen 5 5600G e Radeon RX 7600. São amostras locais, não uma garantia de desempenho para outros equipamentos ou cargas.

| Setting | Recibo após input | Primeiro movimento autorizado | Envelope pronto | Conclusão |
|---|---:|---:|---:|---:|
| default | 70,3 ms | 71,2 ms | 135,8 ms | 570,0 ms |
| ascendant, com arraste | 84,7 ms | 85,6 ms | 189,3 ms | 593,5 ms |
| volatile-core | 63,3 ms | 64,1 ms | 135,2 ms | 574,8 ms |

Essa amostra foi executada com preset **fast**, que divide a duração visual configurada por dois. Não significa que o ciclo normal de 1 segundo foi reduzido. O recibo inicia a saída antes da leitura completa; custos, efeitos e destino continuam dependendo dos fatos autorizados. O marcador `sent` fica junto à chamada de submissão da interface e não mede o instante exato de emissão de bytes pelo sistema operacional.

| Renderização | Limite solicitado | p95 do intervalo de frame observado |
|---|---:|---:|
| 1920×1080 | 60 FPS | aproximadamente 16,72 ms na primeira amostra |
| 2560×1080 | 60 FPS | 16,73 ms |
| Janela solicitada 1280×720, viewport observado 1278×720 | 30 FPS | 33,39 ms |
| 2560×1080 | 120 FPS | 23,49 ms |

O teste a 120 é um limite solicitado; **não atingiu 120 FPS estáveis**. A meta estrita de p95 ≤16,7 ms também não deve ser declarada certificada com base em amostras de 16,72–16,73 ms. Não foram feitos ajustes de qualidade, resolução ou VSync para forçar esse número. Os testes de trajetória/pose e duração verificam progresso temporal e extremos exatos, sem usar contagem fixa de frames como regra de animação.

## Evidências locais

Artefatos ignorados pelo Git em `.runtime/uno-motion-qa/`:

- `full-validation.json`: suíte ampla.
- `integration-validation.json`: seleção/integração com API, anterior ao último ajuste de clipping.
- `final-integration-validation.json`: repetição após a proteção de clipping.
- `committed-validation.json`: exportação da árvore preparada para commit.
- `visual-1920/`, `visual-2560/`, `visual-1280-fps30/`, `visual-2560-fps120/`: capturas.
- `render-30.stdout.log`, `render-120.stdout.log`: métricas renderizadas adicionais.
- `runs/`, `content/`, `telemetry/`: armazenamento exclusivo da API de QA; nenhum save do jogador foi utilizado.

## Limites e diagnósticos

- Aprovação subjetiva de peso, ritmo e amplitude cabe ao teste visual no laboratório. Não houve sessão manual com o usuário nesta execução.
- Alguns testes de laboratório emitiram avisos de recursos retidos no encerramento. A execução verbose identificou `AudioStreamOggVorbis`/`OggPacketSequence` do som `kenney-cards/card-play-1.ogg`; não foram identificados ghosts de carta ou handles de apresentação órfãos nesses checks. O encerramento do decoder de áudio permanece uma limitação separada desta certificação visual.
- O aviso de watchdog em `presentation_director` é provocado deliberadamente pelo cenário de recuperação de handle travado.
- Não foi realizado benchmark controlado antes/depois da latência REST. Os tempos acima demonstram separação de marcos e sobreposição segura com sincronização; não quantificam uma aceleração do servidor.

## Reprodução

Pela raiz do repositório, em PowerShell:

```powershell
& tools/test-godot-ui.ps1 -Tests @('uno_motion_baseline','uno_motion_pose','uno_motion_curves','uno_hand_retarget','uno_card_drag','uno_motion_pending','uno_card_flight','uno_motion_batches','uno_motion_interruptions','uno_motion_profiles','card_draw_pose','card_motion_polish','card_animation_parity','motion_policy','lab_animation_controls','animation_live','historical_presentation','turn_hand_cycle')
```

Para a API real, iniciar uma API com armazenamento exclusivo de QA e perfil `ToolAccess:Profile=dev_modder`, e executar:

```powershell
& tools/test-godot-ui.ps1 -Tests @('animation_online') -ApiUrl 'http://127.0.0.1:61989'
```

Esse teste cria novas tentativas de QA. Não direcioná-lo ao armazenamento do jogador.

Para captura renderizada, sem comandos à engine:

```powershell
& tools/test-godot-ui.ps1 -Tests @() -VisualTest 'uno_motion_visual' -VisualOutputDirectory '.runtime/uno-motion-qa/visual-review' -VisualResolution '2560x1080'
```
