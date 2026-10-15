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
corpo idêntico com diagnóstico ligado/desligado. Ambos passaram.

## Etapa 2 — transporte

Workers HTTPRequest em threads, pool limitado (4), fila com prioridade para
comandos e histórico em baixa prioridade. GETs idênticos em voo compartilham I/O,
mas recebem cópias privadas e cancelamento independente. POSTs não são repetidos.
Cancelamento nativo é agendado fora da pilha de sinais para não bloquear/juntar
uma thread reentrantemente. Epochs continuam descartando respostas de outro host.
Reutilização de nodes não é anunciada como prova de keep-alive TCP; isso permanece
dependente do comportamento nativo do HTTPRequest e da medição do probe.

Testes de loopback: cancelamento antes/durante I/O, consumidores compartilhados,
troca de host, isolamento das respostas e retorno do worker ao pool.

## Etapa 3 — projeção compacta

Contexto de leitura captura uma referência imutável e fingerprint completo,
incluindo identidade/versão. O contrato de UI não serializa encounters passados,
manifesto ou regras resolvidas. No combate, envia apenas o nó atual; no mapa,
envia as rotas necessárias. Atividades de seleção/loja/preparação são filtradas
pelo nó atual. Histórico de diálogo foi preservado para o menu de conversas.
Fluxos de ferramentas são opcionais e filtrados. Teste comprova ausência de
crescimento com 100 encounters passados e preservação do hash original.
