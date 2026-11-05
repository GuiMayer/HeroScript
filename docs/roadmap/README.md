# Roadmap atual

Consulte o [TODO priorizado do MVP e do client Godot](TODO.md) para as pendências ativas revisadas em 2026-11-05, critérios de conclusão e distinção entre tarefas atuais e registros históricos.

**Atualizado em:** 2026-10-16

O núcleo de combate determinístico já possui run persistida, atores genéricos,
conteúdo versionado, cartas/componentes, recursos universais, efeitos,
modifiers, status, relíquias, IA/intents, fases, prioridade/stack, timeline,
replay e branches. A REST v1 é a única autoridade acessível por clientes.

## Situação

| Área | Estado | Próxima prova |
| --- | --- | --- |
| Sandbox de combate | funcional na demo; bloqueios de input reproduzidos e corrigidos com regressões | sessões prolongadas na Godot, recuperação e hardware de input |
| Determinismo e imutabilidade | commits, hashes, replay e jornadas entre processos já testados | manter regressões e ampliar combinações de conteúdo |
| Conteúdo e mods data-only | packages/settings/revisões implementados | mais packages reais e tooling de autoria |
| Run/progressão | fluxo canônico, progressão por setting e primeiro mundo alpha implementados | matriz de builds, eventos/lojas/elites, retomada e balanceamento |
| Timeline e theorycraft | histórico, replay e branches implementados | validar UX e desempenho com históricos longos |
| REST/desempenho | matriz local recente: 526 interações abaixo de 1 s, máximo 802 ms | medir clique-a-tela na Godot e máquina-alvo; não extrapolar o ensaio |
| CI | build, Core e API, incluindo jornadas longas | integrar regressões Godot; ensaios locais não comprovam CI remota |
| Distribuição | launcher de desenvolvimento disponível | export Godot + API, instalação limpa, atualização e restauração certificadas |
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

1. Estabilizar o cliente em partidas completas: cartas, animações, modais,
   navegação, recuperação após falhas e ausência de bloqueios de input.
2. Ampliar a matriz do ato publicado: famílias de build, híbridos, eventos,
   lojas, elites, transformações, vitória/derrota e retomada. Manter os testes
   já existentes de replay e determinismo entre processos.
3. Medir a experiência renderizada e sessões longas na máquina-alvo, incluindo
   memória, armazenamento e API fria/quente; não usar somente tempo do servidor.
4. Automatizar a bateria Godot na CI e certificar o jogo exportado com a API,
   instalação limpa, backup/restauração e atualização com versão compatível.
5. Consolidar contratos de conteúdo/apresentação e ferramentas de autoria,
   produzindo conteúdo e arte incrementalmente antes de escalar todos os mundos.
6. Retomar dashboard apenas quando o contrato REST estiver consolidado pelo uso
   real do protótipo.

## Fontes de verdade

- [Contrato e guias da API](../api/README.md)
- [Arquitetura e plano do sandbox](../plans/COMBAT_SANDBOX_ARCHITECTURE_AND_PLAN.md)
- [Consolidação dos sistemas centrais](../plans/REMAINING_CORE_SYSTEMS_CONSOLIDATION_PLAN.md)
- [Timeline, replay e branches](../architecture/timeline-system.md)
- [Packages e settings](../content/packages-and-settings.md)
- [Viabilidade das mecânicas do GDD](gdd-mechanics-feasibility.md)
- [Desempenho medido e experiência da demo Godot](ENGINE_PERFORMANCE_AND_DEMO_UX.md)
- [Medições REST recentes e limites](../plans/REST_LATENCY_REMAINING_BOTTLENECKS.md)
- [Certificação dos fundamentos e processos independentes](../plans/ENGINE_WORLD_1_FOUNDATIONS_VALIDATION_REPORT.md)
- [Gates abertos do primeiro mundo](../plans/ASCENDANT_WORLD_1_CONTENT_VALIDATION_REPORT.md)
- [Auditoria de input Godot](../architecture/CARD_INPUT_LIFECYCLE_AUDIT.md)
- [Operação e limites de distribuição](../PRODUCTION.md)

Os arquivos em `roadmap/phases` preservam decisões históricas e não descrevem o
contrato atual.
