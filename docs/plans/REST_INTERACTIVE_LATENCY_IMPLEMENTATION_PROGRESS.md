# Implementação da correção da latência REST

Plano: [REST_INTERACTIVE_LATENCY_CORRECTION_PLAN.md](REST_INTERACTIVE_LATENCY_CORRECTION_PLAN.md).

## Base preservada

`bdb1df1`: correções anteriores de ciclo de cartas, seleção e tooltip; plano aprovado.

## Etapa 1 — instrumentação

Diagnóstico opt-in (`Performance:DiagnosticsEnabled`), tempos operacionais por
request, header Server-Timing até envio dos headers e duração completa no logger.
Spans inclusivos de hash, legalidade, inspeção e append; não somar spans aninhados.
Nenhum tempo é persistido em fatos ou hashes. Probe Godot somente leitura em
`tests/rest_latency_probe.gd`, com FPS, host e run opcionais via argumentos.

Baseline da auditoria: GET de 496.341 bytes em 23–33 ms fora da Godot, 1.739–1.766
ms no cliente a 60 FPS. TestServer Release: descoberta inédita 915,8 ms; reuso
36,4 ms; inspeção 385,2/34,6 ms. Amostras pequenas, não p95.

Validação: teste de isolamento de spans e equivalência do hash; teste HTTP de
corpo idêntico com diagnóstico ligado/desligado. Demais etapas pendentes.
