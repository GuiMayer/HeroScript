# Roadmap atual

**Atualizado em:** 2026-09-09

O núcleo de combate determinístico já possui run persistida, atores genéricos,
conteúdo versionado, cartas/componentes, recursos universais, efeitos,
modifiers, status, relíquias, IA/intents, fases, prioridade/stack, timeline,
replay e branches. A REST v1 é a única autoridade acessível por clientes.

## Situação

| Área | Estado | Próxima prova |
| --- | --- | --- |
| Sandbox de combate | pronto para protótipo Godot | validar UX do loop carta/priority/frame |
| Determinismo e imutabilidade | cobertos por commits, hashes e replay | matriz final multi-runtime |
| Conteúdo e mods data-only | packages/settings/revisões implementados | mais packages reais e tooling de autoria |
| Run/progressão | fluxo canônico implementado | conteúdo jogável e balanceamento |
| Timeline e theorycraft | histórico e branches implementados | navegador visual completo |
| Dashboard | adiado | fora do caminho crítico atual |

## Ordem restante

1. Concluir a verificação arquitetural final: remover resíduos não alcançáveis,
   executar o cenário dourado em processos novos e testar restauração após
   restart/corrupção controlada.
2. Usar o cliente Godot de exemplo para validar o loop core: escolher ação
   legal, reproduzir receipt, passar prioridade e trocar branch.
3. Ampliar conteúdo de demonstração sem introduzir regras na camada visual.
4. Criar ferramentas de autoria de package/setting e diagnóstico de
   proveniência.
5. Retomar dashboard apenas quando o contrato REST estiver consolidado pelo uso
   real do protótipo.

## Fontes de verdade

- [Contrato e guias da API](../api/README.md)
- [Arquitetura e plano do sandbox](../plans/COMBAT_SANDBOX_ARCHITECTURE_AND_PLAN.md)
- [Consolidação dos sistemas centrais](../plans/REMAINING_CORE_SYSTEMS_CONSOLIDATION_PLAN.md)
- [Timeline, replay e branches](../architecture/timeline-system.md)
- [Packages e settings](../content/packages-and-settings.md)
- [Viabilidade das mecânicas do GDD](gdd-mechanics-feasibility.md)

Os arquivos em `roadmap/phases` preservam decisões históricas e não descrevem o
contrato atual.
