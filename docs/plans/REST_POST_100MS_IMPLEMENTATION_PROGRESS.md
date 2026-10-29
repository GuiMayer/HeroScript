# Implementação do plano POST 100 ms

Guia: [plano aprovado](REST_POST_100MS_OPTIMIZATION_PLAN.md). Base `4a4a9c95`, EngineVersion 34.

O checkout contém 425 entradas locais preexistentes. Staging/exportação usam apenas os deltas desta tarefa. Saves, API e Godot do jogador não são usados nos testes.

## Etapa 1 — baseline e harness

O benchmark de Kestrel usa atores configurados, lê o corpo completo e mantém o append durável. Agora publica bytes e p50/p95/p99/máximo, aceita `HEROSCRIPT_POST_SAMPLES` e tem gate separado `HEROSCRIPT_POST_BUDGET_MS`. Valores de 1000 ms do gate antigo não são usados como aceite deste plano. A amostra padrão continua curta para regressão; percentis com três amostras são apenas diagnósticos, sem confiança estatística. Jornadas devem gerar sequências válidas, não repetir END_TURN após morte.

Baseline anterior: Default POST 546–666 ms e Ascendant 376–413 ms. Nenhum alvo de 100 ms é anunciado como atingido. Etapas 2–13 pendentes.

Baseline nova em `.runtime/post100-01`: três testes API aprovados (inventário e dois settings), Release/Kestrel/store novo.

- POST_SUMMARY setting=default; n=3; p50=764.54; p95=1166.63; p99=1166.63; max=1166.63ms
- POST_SUMMARY setting=ascendant; n=3; p50=375.66; p95=580.59; p99=580.59; max=580.59ms
