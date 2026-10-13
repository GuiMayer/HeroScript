# Apresentação de combate — progresso

Guia: [arquitetura e plano](GODOT_COMBAT_ANIMATION_ARCHITECTURE_AND_PLAN.md).

## Preparação

- Correção anterior das cartas preservada separadamente: `e96008a`.
- Plano versionado: `aaba89f`.
- Nenhum push solicitado nesta implementação.

## Etapas

1. Baseline: regressões mantidas em `animation_contracts.gd`; o modo diagnóstico
   `--animation-baseline` registra defeitos conhecidos. O modo normal exige os
   comportamentos corretos e será incluído na suíte após a implementação.
2. Envelope contextual: implementado como projeção imutável com validação de
   identidade/sequence e filtragem de transferências visuais ocultas. Sem alteração
   de contrato da engine nesta etapa.
3. Planner causal: implementado; agrupa apenas aplicações contíguas, reconhece
   ancestralidade da condensação e reserva snapshots de frame para o beat final.
4. Director/handles: implementado; conclusão única, trabalho bloqueante distinto
   de cursor, confirmação sem sobreposição, cancelamento/skip e watchdog pausável.
   Alias de import antigo será removido após migrar os consumidores.
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
10. Replay: pendente.
11. Limites/desempenho: pendente.
12. Certificação/limpeza: pendente.

## Limites de validação

Os probes da auditoria demonstraram recebimento antes da sincronização, input
liberado durante voo e cenário animando no pause. Esses cenários serão
convertidos em testes de integração do novo fluxo nas etapas 5–6. Testes
offline não substituem certificação online nem inspeção de imagens renderizadas.
