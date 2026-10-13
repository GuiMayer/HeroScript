# Animações de combate: implementação e calibração

Data: 2026-10-13. Plano: `GODOT_COMBAT_ANIMATION_POLISH_AND_RESPONSIVENESS_PLAN.md`.
Base: `5925db3`. Oito etapas implementadas, em commits separados.

## Entregas por etapa

| Etapa | Entrega | Commit |
| --- | --- | --- |
| 1 | Regressão de tempo morto e fixtures de movimentos | `cf93d54` |
| 2 | Pump iterativo, conclusão síncrona sem executor e modo manual sem no-ops | `aa6e7d2` |
| 3 | Reserva de slots, suspensão de efeitos da mão e âncoras por address | `31e6791` |
| 4 | Preparação imediata, área de uso, rejeição e retorno | `cbfd72a` |
| 5 | Transferências curvas, crescimento/chegada, stagger e limites | `91a5beb` |
| 6 | Conclusão por tweens reais; executor de impactos vivo/histórico compartilhado | `d296303` |
| 7 | Cache de manifesto, âncoras, leituras paralelas e métricas | `fe97fcd` |
| 8 | Laboratório, empréstimo da própria carta, calibração e certificação | Commit desta entrega |

A calibração final também corrigiu referências de controles após remoção de carta,
cancelamento de callbacks de gerações anteriores, limpeza de lotes, verso aparecendo
antes do seu stagger e restauração de barras/retratos no skip. A própria `CardView`
da mão é emprestada à camada de efeitos durante uso/descarte; sua face não é reconstruída
para o voo. A entrega de volta restaura escala, rotação, foco e camada de desenho.

## Comparação do tempo morto

Os scripts de `Playback` e do director da base foram carregados numa cópia isolada.
Uma fila de 100 aplicações sem mudança e sem executor precisou de **200 ticks**.
O cursor novo precisou de **1 tick**, com cerca de 2,2 ms de processamento nessa amostra.
O processamento de CPU não desaparece; foram eliminados os quadros de espera impostos.

Com executores/HUD presentes, uma passagem pode ceder após consumir seu orçamento de
4 ms. O laboratório registra essa cessão e drena a fila automaticamente, inclusive
em modo manual. Não cria handle, timer ou clique obrigatório para a cessão.

## Medidas do laboratório

Godot 4.7.2, desktop Windows, componentes reais, sem API. Uma amostra por operação
abaixo, preset normal, incluindo preparação visual local. Não é medição de p95/FPS.

| Operação | Tempo observado |
| --- | --- |
| Uso + saída canônica para a pilha | 244 ms |
| Compra de 1 | 195 ms |
| Compra de 5 | 269 ms |
| Compra de 10 | 354 ms |

O descarte posterior ao uso reaproveita a carta na área de uso e usa `staged_discard`.
Descartes normais continuam usando `discard`. Lotes não multiplicam o tempo integral
de cada voo: usam `stagger` limitado por `batch_span`.

Modo instantâneo elimina espera intencional. Construir/reconciliar dez faces novas
ainda consumiu aproximadamente 100 ms de CPU em algumas amostras; isso não deve ser
descrito como latência zero. Não foi adicionado pooling sem medição de um benefício.

## Integração com a API

Validação conectada: 26 verificações aprovadas nos settings default, ascendant e
volatile-core, além de sandbox histórico autorizado. Cada tentativa é de QA; não
foram abertos nem modificados saves existentes do jogador. O replay canônico permaneceu
válido e a apresentação não alterou a sequência nem reenviou comandos.

Última rodada: uma carta por setting, preset fast. Timestamps de uma amostra por
setting, sem inferir percentis ou uma garantia de desempenho universal:

| Setting | Envio → sincronização | Sincronização → conclusão visual | Preparação da view dentro desse período |
| --- | --- | --- | --- |
| default | 941 ms | 111 ms | 26 ms |
| ascendant | 1.068 ms | 121 ms | 52 ms |
| volatile-core | 1.022 ms | 171 ms | 93 ms |

A espera dominante ainda é a sincronização, apesar do paralelismo entre ações legais
e projeções quando o recibo já fornece o combate. Avaliação da mão continua dependente
dos alvos das ações legais e é validada contra run/step/revisão antes da publicação
atômica. O cliente não publica uma descrição provisória durante essas consultas.

Não foi criado um novo endpoint composto: esta entrega mantém o contrato da engine.
As amostras acima ainda não certificam sincronização inferior a 1 segundo; investigar
esse caminho com amostragem e profiling de servidor/cliente é trabalho separado da
cadência visual corrigida aqui.

## Cobertura e evidências

- `card_motion_polish`: 14 cenários nos presets normal/fast/instant; modo manual,
  movimento reduzido, pausa, resize, skip, chegada visível, limites e encerramento.
- `animation_live`: preparação durante inspeção atrasada, confirmação antes de impacto,
  instância única no voo, pausa conjunta, neutralidade da seleção, saída terminal e
  navegação obsoleta. Capturas renderizadas em 2560×1080.
- `animation_responsiveness`, `presentation_director`, `presentation_envelope` e
  `presentation_planner`: no-ops, causalidade, geração, manual, watchdog e escopo.
- `semantic_cues`, `animation_limits`, `audio_contracts` e `historical_presentation`:
  efeitos instantâneos sem tween vazio, resource arbitrário, pagamento, limites,
  descarte cosmético e histórico independente.
- `card_refresh_stability`, `card_play_selection`, `hand_ux`, `pause_layers`,
  `combat_collaborators`, `connection_epoch` e `ui_lifecycle`: regressões de texto,
  seleção, foco, camadas, cache, conexão e descarte de componentes.
- `ivory_lab`: 68 verificações aprovadas de resolução, inglês/português, escala de
  texto, mãos 0/1/5/12/100, alto contraste e ausência de writes de preferência/API.

Capturas locais de compra em curso, pausa e chegada foram geradas e inspecionadas:
`.runtime/animation-polish/1280x720`, `1920x1080`, `2560x1080` e `combat-2560x1080`.
São artefatos locais ignorados pelo Git. Os testes usam cópias isoladas e não substituem
um playtest completo do ato nem uma sessão longa com métricas de maior frame/p95.

## Reproduzir

Abra `examples/godot-engine-showcase/scenes/card_hand_lab.tscn`. Selecione o cenário
no seletor de movimento e use **Play motion**, **Next** e **Skip presentation**.
Os controles usam os executores do combate e não executam regras da engine.

Em PowerShell 7:

```powershell
./tools/test-godot-ui.ps1 -Tests card_motion_polish,animation_responsiveness,animation_live
./tools/test-godot-ui.ps1 -Tests animation_online -ApiUrl http://127.0.0.1:5271
./tools/test-godot-ui.ps1 -Tests @() -VisualTest card_motion_polish `
  -VisualResolution 2560x1080 -VisualOutputDirectory .runtime/animation-polish/2560x1080
```

O guia atualizado de configuração está em `docs/design/COMBAT_PRESENTATION_AUTHORING.md`.
