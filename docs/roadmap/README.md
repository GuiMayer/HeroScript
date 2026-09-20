# Roadmap atual

**Atualizado em:** 2026-09-20

O núcleo de combate determinístico já possui run persistida, atores genéricos,
conteúdo versionado, cartas/componentes, recursos universais, efeitos,
modifiers, status, relíquias, IA/intents, fases, prioridade/stack, timeline,
replay e branches. A REST v1 é a única autoridade acessível por clientes.

## Situação

| Área | Estado | Próxima prova |
| --- | --- | --- |
| Sandbox de combate | validado pela demo Godot | ampliar conteúdo e validar hardware de input |
| Determinismo e imutabilidade | cobertos por commits, hashes e replay | matriz final multi-runtime |
| Conteúdo e mods data-only | packages/settings/revisões implementados | mais packages reais e tooling de autoria |
| Run/progressão | fluxo canônico implementado | conteúdo jogável e balanceamento |
| Timeline e theorycraft | histórico e branches implementados | navegador visual completo |
| Dashboard | adiado | fora do caminho crítico atual |

## Backlog arquitetural explícito

| Pendência | Quando se torna necessária | Estado atual |
| --- | --- | --- |
| Ownership e autorização por ator/controlador | Antes de multiplayer ou API multi-cliente | single-player usa a mesma fronteira actor-agnostic, sem política de posse externa |
| Resolução automática do fim de encounter | Quando um modo precisar dispensar confirmação do cliente | `ManualAck` é a única estratégia aceita; `Automatic` permanece reservado |
| Telemetria e tooling matemático avançado | Para profiling/autoria especializada | validação, trace e tempo total existem; evento, breakpoints e profiling por operação não |

Essas são extensões deliberadas, não falhas do protótipo atual. Checklists
operacionais e documentos em `roadmap/phases` não devem ser interpretados como
uma segunda lista de tarefas.

## Ordem restante

1. Concluir a verificação arquitetural final: remover resíduos não alcançáveis,
   executar o cenário dourado em processos novos e testar restauração após
   restart/corrupção controlada.
2. Ampliar conteúdo de demonstração e testes manuais de UX/hardware sem
   introduzir regras na camada visual.
3. Criar ferramentas de autoria de package/setting e diagnóstico de
   proveniência.
4. Retomar dashboard apenas quando o contrato REST estiver consolidado pelo uso
   real do protótipo.

## Fontes de verdade

- [Contrato e guias da API](../api/README.md)
- [Arquitetura e plano do sandbox](../plans/COMBAT_SANDBOX_ARCHITECTURE_AND_PLAN.md)
- [Consolidação dos sistemas centrais](../plans/REMAINING_CORE_SYSTEMS_CONSOLIDATION_PLAN.md)
- [Timeline, replay e branches](../architecture/timeline-system.md)
- [Packages e settings](../content/packages-and-settings.md)
- [Viabilidade das mecânicas do GDD](gdd-mechanics-feasibility.md)
- [Desempenho medido e experiência da demo Godot](ENGINE_PERFORMANCE_AND_DEMO_UX.md)

Os arquivos em `roadmap/phases` preservam decisões históricas e não descrevem o
contrato atual.
