# TODO — backlog priorizado do MVP e do client Godot

Atualizado em: 2026-11-05.

Este documento reúne as pendências identificadas na revisão do código, das cenas e dos registros de implementação. A ordem abaixo é a sequência recomendada, não uma declaração de que todos os itens são defeitos reproduzidos. Ausência de certificação, implementação incompleta e decisão de design são indicadas separadamente.

Não houve nova execução de testes nem benchmark para produzir este backlog. As evidências citadas pertencem às respectivas entregas. Nenhum item autoriza mudar regras de gameplay, determinismo, imutabilidade, durações ou disposição visual como efeito colateral de uma correção.

## Ordem de execução

| Ordem | ID | Prioridade | Tipo | Pendência |
| --- | --- | --- | --- | --- |
| 1 | UI-01 | Alta | Certificação | Jornada completa do MVP com API real após as mudanças recentes |
| 2 | UI-02 | Alta | Automação | Regressões Godot na CI |
| 3 | UI-03 | Alta | Implementação | Overflow de status e leitura por foco |
| 4 | UI-04 | Alta | Implementação | Faixa de inscrições no combate MVP |
| 5 | UI-05 | Média-alta | Revisão e certificação | Previsão e explicação dos cálculos |
| 6 | UI-06 | Média | Implementação | Log estruturado de combate |
| 7 | UI-07 | Média | Revisão incremental | Editabilidade das regiões restantes |
| 8 | UI-08 | Média | Certificação visual | Legibilidade, acessibilidade e resoluções |
| 9 | REL-01 | Média; obrigatória antes de distribuir | Certificação | Exportação Godot e servidor em instalação limpa |
| 10 | GAME-01 | Após estabilizar o fluxo | Playtest | Compreensão e equilíbrio do capítulo |

Não foi confirmado um novo bloqueador crítico na revisão que originou esta lista. Falhas reproduzidas durante UI-01 devem ser registradas e priorizadas conforme o impacto real.

## Pendências ativas

### UI-01 — Certificar o fluxo atual com API real

- [ ] Executar ingresso na run, combate, seleção de alvo, pagamento com cartas e cancelamento.
- [ ] Exercitar pause durante apresentação, conclusão da atividade, retorno ao mapa, salvamento e retomada por setting.
- [ ] Registrar revisão do código/conteúdo, ambiente, testes, resultados e falhas reproduzidas, usando stores isolados.

**Conclusão:** o percurso termina sem bloqueios de input, duplicação de comandos ou alterações indevidas de estado. As verificações de replay e imutabilidade permanecem válidas. Certificações anteriores não são descartadas; falta cobrir a composição após as alterações recentes.

Referências: [composição MVP](../plans/MVP_COMBAT_COMPOSITION_IMPLEMENTATION.md), [editabilidade](../plans/GODOT_EDITOR_EDITABILITY_PROGRESS.md).

### UI-02 — Executar regressões Godot na CI

- [ ] Preparar ambiente reproduzível com versão fixada da Godot e importação limpa.
- [ ] Incluir montagem no editor, mão, navegação e integração essencial em jobs apropriados.
- [ ] Publicar logs de falha e tornar erros de script/placeholders impeditivos para aprovação.

**Conclusão:** uma alteração que quebre esses contratos falha automaticamente na CI. O workflow atual em `.github/workflows/ci.yml` cobre C# e API, mas não executa a bateria Godot.

Referências: [runner](../../tools/test-godot-ui.ps1), [regressão no editor](../../examples/godot-engine-showcase/tests/editor_tool_scenes.gd).

### UI-03 — Controlar excesso de status

- [ ] Implementar apresentação de até seis status por combatente e acesso aos restantes por `+N`, conforme o conceito vigente.
- [ ] Garantir detalhes por mouse e foco de teclado/gamepad.
- [ ] Validar muitos status, nomes longos e escala de texto sem deslocar a composição.

**Conclusão:** todos os status publicados continuam consultáveis, sem perda de informação nem mudança de regra. Hoje `actor_panel.gd` instancia os badges ativos sem aplicar esse limite visual.

Referências: [ActorPanel](../../examples/godot-engine-showcase/scripts/ui/actor_panel.gd), [conceito v2](../design/Conceito%20de%20interface%20de%20combate%20%28v2%29.md).

### UI-04 — Concluir a faixa de inscrições

- [ ] Vincular a faixa exclusivamente às inscrições equipadas publicadas pela engine.
- [ ] Implementar ícones, tooltip e overflow de 12 conforme o conceito.
- [ ] Apresentar realce cruzado de cartas afetadas com base nas relações fornecidas pelos modelos, sem reconstruir regras no cliente.

**Conclusão:** a faixa representa o loadout atual, inclusive estados vazio e excesso. `InscriptionStrip` ainda está oculto e contém `metadata/review_todo` em `combat_mvp_view.tscn`.

Referência: [cena MVP](../../examples/godot-engine-showcase/scenes/ui/components/combat_mvp_view.tscn).

### UI-05 — Revisar previsão e explicação de valores

- [ ] Confrontar o componente atual com o contrato de resultado por alvo/impacto.
- [ ] Mostrar somente contribuições relevantes, etapas do cálculo e origem dos bônus publicados.
- [ ] Certificar correspondência entre previsão e execução, respeitando informação oculta.
- [ ] Documentar eventual informação ausente na API antes de propor extensão; não calcular o resultado na Godot.

**Conclusão:** o jogador entende o valor previsto e suas causas sem informações redundantes. Parte da infraestrutura já existe; não é uma proposta para substituir a bucket pipeline.

Referências: [conceito v2](../design/Conceito%20de%20interface%20de%20combate%20%28v2%29.md), [limites da pipeline](../plans/BUCKET_PIPELINE_IMPLEMENTATION_PROGRESS.md).

### UI-06 — Evoluir o log de combate

- [ ] Consumir eventos estruturados com origem, alvo, ordem/rodada e cálculo resumido quando disponível.
- [ ] Oferecer filtro por rodada e comportamento definido ao reabrir/retomar o combate.
- [ ] Preservar a distinção entre log consultável, timeline e fila de animações.

**Conclusão:** o registro atende ao conceito sem depender apenas do texto da animação. Hoje `combat_mvp_view.gd` acumula `detail_label.text` em uma lista local de até 200 entradas.

Referência: [implementação do MVP](../../examples/godot-engine-showcase/scripts/ui/combat_mvp_view.gd).

### UI-07 — Ampliar a editabilidade por região

- [ ] Inventariar os overrides restantes que impedem ajustes humanos no editor.
- [ ] Transferir apenas valores estáticos para cenas/recursos; manter fan, hit testing, câmera e coleções variáveis em seus algoritmos atuais.
- [ ] Ampliar prévias úteis nas telas que ainda dependem do fluxo completo, verificando cada região antes de avançar.

**Conclusão:** mudanças visuais da região auditada não são silenciosamente sobrescritas em runtime e não alteram comportamento. Não significa eliminar todo `.new()` ou tornar toda cena executável isoladamente.

Referências: [guia de edição](../design/GODOT_UI_EDITING_GUIDE.md), [progresso recente](../plans/GODOT_EDITOR_EDITABILITY_PROGRESS.md).

### UI-08 — Fechar a validação visual e de acessibilidade

- [ ] Validar fontes, contraste, limites de texto, tooltips e foco com PT/EN e escala de texto.
- [ ] Conferir 10–12 cartas, muitos status e ausência de dependência exclusiva de cor.
- [ ] Inspecionar capturas renderizadas em 16:9 e registrar limitações nas demais proporções.

**Conclusão:** resultados mensuráveis e aprovação visual registrados. A adaptação fora de 16:9 continua provisória; não há certificação de todas as combinações possíveis de conteúdo e resolução.

Referências: [conceito v2](../design/Conceito%20de%20interface%20de%20combate%20%28v2%29.md), [limites da usabilidade](../plans/GODOT_UI_USABILITY_IMPLEMENTATION_PROGRESS.md).

### REL-01 — Certificar distribuição independente

- [ ] Validar export Godot junto do servidor em instalação limpa, sem depender do editor.
- [ ] Exercitar inicialização, persistência por setting, backup/restauração e atualização compatível.

**Conclusão:** pacote distribuível executado em ambiente limpo, com procedimento de recuperação documentado. Testar fontes ou launcher de desenvolvimento não fecha este item.

Referências: [produção](../PRODUCTION.md), [gates do MVP](../plans/TACTICAL_CAMPAIGN_MVP_IMPLEMENTATION_PROGRESS.md).

### GAME-01 — Executar playtest externo

- [ ] Registrar compreensão do combate e do ciclo derrota → ajuste → nova tentativa.
- [ ] Medir duração, escolhas, viabilidade de builds e possíveis softlocks.
- [ ] Separar falhas técnicas de problemas de equilíbrio e de preferências estéticas.

**Conclusão:** observações reais e próximos ajustes registrados; testes automatizados não são prova de diversão ou equilíbrio.

Referência: [roteiro de playtest](../validation/TACTICAL_CAMPAIGN_MVP_PLAYTEST.md).

## Trilha separada de desempenho

### PERF-01 — Fechar a meta de POST abaixo de 100 ms

- [ ] Reproduzir a medição no código atual antes de escolher a próxima otimização.
- [ ] Identificar os custos restantes por etapa, mantendo append durável, REST e resultados idênticos.
- [ ] Publicar p50/p95/p99/máximo e violações por comando/setting, incluindo host frio e sessão longa quando aplicável.

O último ensaio amplo do caminho mantido no registro consultado mediu 1.000 `END_TURN` por setting: Default p95 **100,91 ms** e Ascendant p95 **166,57 ms**, com violações da meta. São números históricos daquele ambiente, não medições desta revisão. Um experimento posterior não demonstrou ganho e foi removido; seus resultados não devem ser apresentados como estado do código mantido.

**Conclusão:** cumprir o aceite do plano com evidência atual; melhora de média ou p95 isolado não prova que todos os POSTs ficaram abaixo da meta.

Referência: [registro de otimização](../plans/REST_POST_100MS_IMPLEMENTATION_PROGRESS.md).

## Decisões que precedem implementação

- [ ] Definir a apresentação do companion e resolver a questão de ser atacável; não inferir regras no cliente.
- [ ] Definir como a UI comunica os níveis de informação revelada dos inimigos, respeitando o que a engine publica.
- [ ] Aprovar paleta/glifos de tags e apresentação de aura/resistências conforme o modelo de design escolhido.
- [ ] Resolver divergências entre instruções mais recentes e descrições antigas, como o canto do custo da carta, antes de reposicionar componentes.

Esses itens não autorizam adicionar mecânicas ou alterar a engine arbitrariamente. Referência: [pontos em aberto do conceito](../design/Conceito%20de%20interface%20de%20combate%20%28v2%29.md).

## Higiene do backlog e arquivos locais

- [ ] Atualizar checklists antigos que contradizem registros posteriores, preservando o histórico e ligando o registro final.
- [ ] Corrigir a codificação de texto do registro de composição MVP; há trechos com caracteres corrompidos.
- [ ] Revisar a origem dos arquivos locais não rastreados `card_detail_line.tscn` e `card_inline_label.tscn` antes de incluí-los em commits. A revisão não encontrou referências a eles fora do cache do editor; não foram apagados.

**Não recolocar como tarefas abertas:**

- As seis etapas de usabilidade descritas em [seu registro](../plans/GODOT_UI_USABILITY_IMPLEMENTATION_PROGRESS.md).
- As cinco etapas recentes de editabilidade e as correções de placeholders já entregues.
- O deploy inicial do portal apenas porque o checklist do plano original permaneceu desmarcado; consultar o [registro de publicação](../plans/DOCUSAURUS_GITHUB_PAGES_IMPLEMENTATION_PROGRESS.md).
- Calor no MVP: foi removido por decisão de design; pendências térmicas históricas não são requisitos atuais desse setting.
- Dashboard, multiplayer e ferramentas matemáticas avançadas: extensões deliberadas, fora do caminho crítico desta lista.

## Manutenção

Ao concluir um item, marcar seu checkbox e registrar commit, testes, ambiente e limites. Acrescentar falhas reproduzidas com evidência em vez de transformar toda limitação de cobertura em bug confirmado. Este arquivo é o índice de trabalho priorizado; os planos vinculados continuam contendo detalhes e evidências históricas.
