# Apresentação de combate — progresso

Guia: [arquitetura e plano](GODOT_COMBAT_ANIMATION_ARCHITECTURE_AND_PLAN.md).

## Preparação

- Correção anterior das cartas preservada separadamente: `e96008a`.
- Plano versionado: `aaba89f`.
- Nenhum push solicitado nesta implementação.

## Etapas

1. Baseline: regressões mantidas em `animation_contracts.gd`; o modo diagnóstico
   `--animation-baseline` registra defeitos conhecidos. O modo normal exige os
   comportamentos corretos e está incluído na suíte padrão.
2. Envelope contextual: implementado como projeção imutável com validação de
   identidade/sequence e filtragem de transferências visuais ocultas. Sem alteração
   de contrato da engine nesta etapa.
3. Planner causal: implementado; agrupa apenas aplicações contíguas, reconhece
   ancestralidade da condensação e reserva snapshots de frame para o beat final.
4. Director/handles: implementado; conclusão única, trabalho bloqueante distinto
   de cursor, confirmação sem sobreposição, cancelamento/skip e watchdog pausável.
   Alias antigo removido após migrar os consumidores.
5. Integração com combate/mão: implementado; recibo aceito fica retido até
   sincronização, carta sai uma vez antes dos impactos, input usa trabalho
   bloqueante e projeção final determina a permanência da instância.
6. Pause/cancelamento/pós-combate: implementado; cenário e vozes de combate
   pausáveis, skip visual no HUD e reconciliação/navegação protegida por geração.
   Teste de integração cobre leitura atrasada, voo único, pause e confirmação repetida.
7. Feedback semântico/resources: implementado; pagamento por proveniência,
   reações de dano separadas, lanes de texto, status/modifiers e endpoints de
   resource sincronizados ao beat sem alterar snapshots autoritativos.
8. Zonas de cartas: implementado; executor por addresses publicados, reutiliza
   CardView quando permitido e verso para fluxo oculto, sem interpretar finalidade
   da zona. Cancelamento remove todas as representações transitórias.
9. Perfis/acessibilidade/áudio: implementado; manifesto JSON capturado em
   fronteira segura, presets normal/rápido/instantâneo, modo manual existente,
   limites e áudio por cue com chaves/fallback em inglês.
10. Replay: apresentação visual de um comando sob demanda implementada pelo
    endpoint canônico de commit já autorizado. Reutiliza planner/director/feedback
    e componentes de atores, sem consulta pelo combate atual nem nova rota.
    Modo restrito mantém resumo factual; leitor visual não reconstrói mãos ocultas.
11. Limites/desempenho: implementado; efeitos/transfers limitados por manifesto,
    lanes limitadas por alvo, métricas observacionais em anel de 64 comandos e
    descarte de recibos/snapshots após settling. Sem pooling sem evidência de benefício.
12. Certificação/limpeza: implementada. Fluxo antigo e ActorPortrait sem consumidores
    removidos, UIDs versionados, guias atualizados. Testes offline, integração
    conectada aos três settings e replay autorizado executados; limites da matriz
    e do benchmark estão no relatório de validação.

## Limites de validação

Os probes viraram regressões mantidas. A validação online identificou a diferença
entre agregados históricos e DTOs de combate: agora ambos passam por um adaptador
puro compartilhado, sem implementação duplicada ou reconstrução de regras.

Consulte o [relatório](GODOT_COMBAT_ANIMATION_VALIDATION_REPORT.md) para números,
capturas, timeout da suíte ampliada de API e verificações ainda não realizadas.
