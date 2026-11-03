# Execução do MVP da campanha tática

Início: 2026-11-02. Guia: [plano](TACTICAL_CAMPAIGN_MVP_IMPLEMENTATION_PLAN.md).

## Etapa 0 — Baseline e contrato de produto

Estado: concluída.

- Preservadas as alterações pendentes anteriores à execução. Inventário, diff binário e hashes dos arquivos estão em `.runtime/tactical-mvp`.
- Criado o [GDD curto do MVP](../game-design/TACTICAL_CAMPAIGN_MVP.md), com defaults de playtest, limites e matriz de permanência por tentativa. GDDs anteriores permanecem intactos.
- Reutilização confirmada: custos de recurso, CardZoneSystem, CardSelection/Decompose, RestartActivity, lifecycle de recursos, atributos/inscrições, gambits e cenas canônicas da Godot.
- Lacunas sequenciais: composição ativa/reserva/módulos; pagamentos escolhidos com cartas; UX de retry; propriedade/configuração de suporte; integração do capítulo.
- A política de composição não será implementada por grants fictícios nos compartimentos. O companion não terá processador de efeitos nem relógio de gameplay próprios na Godot.

Baseline de testes: **56 aprovados, zero falhas/skips**, cobrindo serialização canônica, zonas, recursos/retry, compartimentos, inscrições e gambits em diretório de build isolado. Evidência local: `.runtime/tactical-mvp/results/baseline.trx`. Nenhuma configuração jogável foi alterada nesta etapa.

## Etapa 1 — Contratos e publicação

Estado: concluída.

Criados contratos de composição com grupos/cotas/catálogos e módulos, suporte/rotinas/loadout e seleção de pagamento por zonas. Kinds novos possuem identidade e validação semântica de referências na publicação; os kinds opacos anteriores não foram reinterpretados. Cadência do suporte pertence a recursos/respostas/gambits normais, não a um novo relógio do companion. Não foi habilitado vínculo de modo antes de existir o executor correspondente. Tentativa e recompensa reutilizarão as políticas/transições atuais, recebendo extensões somente onde demonstradas necessárias.

Schema: `docs/schemas/build-composition-content.schema.json`. Custos de zona são contrato isolado até sua integração na etapa 5; ainda não fazem parte de ActionCosts. Editar composição/suporte em combate é rejeitado como opção não implementada neste recorte.

Testes: **35 aprovados, zero falhas/skips**, incluindo contratos, erros de publicação e serialização canônica. Evidência local: `.runtime/tactical-mvp/results/stage01.trx`.

## Próxima etapa

Etapa 2: estado/planners imutáveis de composição e reserva. Etapas 2–17 permanecem pendentes.
