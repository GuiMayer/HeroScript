# Plano — MCP próprio do HeroScript

Data: 2026-11-20 · Estado: ferramentas e integração implementadas; ver evidências, instruções e limitações no [README do HeroScript MCP](../../tools/HeroScript.Mcp/README.md).

## Objetivo

Dar ao Codex acesso estruturado às regras, ao conteúdo e ao estado da engine, complementando o Godot MCP (LetsAgents). O Godot observa a apresentação; o HeroScript MCP explica o estado autoritativo, permitindo reproduzir problemas e verificar o balanceamento sem depender de screenshots ou interpretações de logs.

### Escopo inicial

Fluxo MVP e campanha Alisyum: combate, ações legais, status, cartas, gambits, revisões de conteúdo, saves, replay e simulação. Não é necessário reimplementar regras no MCP, controlar a partida real ou criar um segundo backend.

## 1. Arquitetura proposta

```text
Codex
  ├── Godot MCP (LetsAgents) → editor → jogo → UI, screenshot, layout
  └── HeroScript MCP (stdio, .NET 10)
       ├── clientes tipados → API local /api/v1 → estado autoritativo
       ├── diagnóstico/validação → serviços existentes em Core/API
       └── execução isolada → sandbox/replay/simulações → resultados
```

- Novo projeto `tools/HeroScript.Mcp/HeroScript.Mcp.csproj`, alvo `net10.0`, SDK oficial [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk), versão fixa validada no restore. Console/stdio, mensagens de diagnóstico em stderr; nada de stdout fora do protocolo.
- `McpHost`: registro das ferramentas, configuração e ciclo de vida. `HeroScriptApiClient`: DTOs tipados, timeout, cancelamento e respostas de erro. `DiagnosticTools`, `ContentTools`, `ReplayTools`, `SimulationTools`: casos de uso específicos. `McpSafetyPolicy`: separação leitura/simulação, limites e seleção explícita do ambiente.
- Preferir API e contratos existentes em `openapi/heroscript-v1.json` e `openapi/heroscript-authoring-v1.json`. Caso informações semânticas não estejam expostas (ex.: rastreio detalhado de cada gambit), criar serviço de diagnóstico reutilizável e endpoint dedicado com operação somente leitura na API. Não duplicar reducers, abrir arquivos arbitrários nem acessar diretamente a persistência em produção.
- Configurar base URL por ambiente (`HEROSCRIPT_MCP_API_URL`, padrão local `http://127.0.0.1:5271`, confirmar na instalação). Recusar hosts externos por padrão. A API continua responsável por autorização, revisão de conteúdo e `IToolAccessPolicy`.
- Não iniciar/reiniciar o servidor de jogo automaticamente; reportar indisponibilidade com orientação. Nada de credenciais no repositório. A sessão MCP não deve manter referências mutáveis ao estado da engine.

## 2. Ferramentas e contratos

| Ferramenta | Entrada | Saída | Integração / restrição |
|---|---|---|---|
| `get_engine_status` | nenhuma | conectividade, versão/contrato, capacidades | verificação não administrativa |
| `list_settings` | filtro opcional | settings e manifestações/revisões | `GET /api/v1/content/settings` |
| `inspect_run` | `runId` | configuração, sequência, revisão, estado, capacidades | `GET /api/v1/runs/{id}`, `.../capabilities` |
| `inspect_combat` | `combatId` | rodada, ator, HP, block, energia, status, ações | `GET /api/v1/combats/{id}` e `.../legal-actions` |
| `inspect_entity` | `combatId`, `actorId` | atributos, efeitos/contadores, intenções | projeção do combate e atributos existentes |
| `inspect_card` | `combatId`, `cardInstanceId` | custos, alvos válidos, efeitos, impedimentos | `.../cards/{id}/evaluation` |
| `inspect_save` | `runId` | revisão fixada, comparação com atual, sequência, integridade | conteúdo/revisões e run journal, sem migração |
| `inspect_gambits` | `runId`, `combatId`, `actorId`, sequência opcional | regras, condições, prioridades, candidatos, decisão ou motivo | diagnóstico com serviços de política e legalidade |
| `validate_gambits` | `settingId`, revisão, escopo e orçamento | matriz de cobertura, contraexemplos reproduzíveis | validador que reutiliza reducer do Core |
| `validate_content` | `settingId`, revisão/candidato autorizado | erros e warnings com paths e referências | validação existente, sem publish |
| `verify_replay` | `runId`/`combatId`, limites de sequência | divergências, hashes e primeira sequência discrepante | journal/verify e timeline; verificar efeitos da operação |
| `simulate_encounter` | cenário, seed, revisão e limites | resultados, log resumido e métricas | ambiente sandbox exclusivo; etapa posterior |
| `simulate_branch` | `runId`, sequência, comandos explícitos | timeline, deltas e hash final | `POST /api/v1/simulations`, branch de teste |
| `analyze_cards` | setting, revisão, conjunto, cenários/seeds | custo e efeito observados, distribuição e limitações | agregação de resultados reais de simulação |

Todos os resultados devem incluir identificadores, `contentRevision`, sequência ou seed quando aplicáveis e informação explícita se o resultado está incompleto. Formato estruturado + resumo curto; paginação e limites máximos. Erros com código, mensagem, indicação de retry e ação sugerida. Nenhuma ferramenta genérica de execução de shell, SQL, HTTP arbitrário, publicação ou alteração direta de saves.

### Gambits — foco no problema real do Archon

Componentes existentes: `Core/Combat/Gambits/DecisionPolicyRegistry.cs` (`GambitDecisionPolicy`, `GambitDecisionReducer`), `Core/Combat/LegalActions/LegalActionResolver.cs` e `IContentRuntimeResolver`. A política já carrega conteúdo fixado por `run.Determinism.ContentRevision`; preservar esse comportamento.

1. Extrair a avaliação de candidatos para um diagnóstico observável, compartilhando lógica com a política existente, sem consumir RNG ou avançar o estado original.
2. Mostrar por regra: `decisionId`, prioridade, condição avaliada, poderes e alvos considerados, candidato legal correspondente e motivo de rejeição.
3. Enumerar bindings de IA em todas as campanhas Alisyum (capítulo 1, Servo, Gambler e Renegado), confrontando configurações, poderes efetivamente possuídos e ações legalmente disponíveis.
4. Cobrir fases/rodadas, recursos mínimos/máximos, cooldowns, status, ausência de alvo e morte; usar fixtures e cenários reais limitados, não afirmar prova exaustiva a partir de amostragem.
5. Emitir caso reproduzível com revisão, seed, ator, estado, regras rejeitadas e ações candidatas. Não escolher uma ação artificial nem adicionar fallback silencioso automaticamente.
6. Aproveitar os testes `tests/Core.Tests/Combat/Gambits/GambitDecisionReducerTests.cs` e `tests/API.Tests/Integration/AlisyumGambitCoverageTests.cs` presentes no checkout; confirmar o último após integração das mudanças pendentes.

### Replay e saves

Reutilizar `RunReplayVerification`, `RunSemanticReplayService`, `RunReplayQueryService` e endpoints `GET .../journal`, `.../timeline`, `.../commits/{sequence}`. A inspeção de partidas antigas deve distinguir revisão fixada e revision atual: uma correção de conteúdo nova não implica que uma partida antiga foi migrada. Não restaurar/regravar saves automaticamente. Reportar opções; qualquer migração exige procedimento separado, backup e autorização.

### Simulação e balanceamento

`IRunSimulationService` já executa em branch durável dedicado; o source original não é hidratado no processador de comandos. `SimulationController` valida `ToolCapabilities.SimulationRun`; `CombatSandboxController` valida `ScenarioAuthor`. Ambos têm operações que criam estado de teste, não classificá-las como somente leitura.

Usar uma instância de testes com persistência dedicada e revision fixada. Começar com seed explícita, máximo de 100 rodadas e 100 comandos por simulação, até 20 seeds por lote, timeout e cancelamento. Medir vitória/derrota, turnos, HP final, dano recebido, energia desperdiçada, efeitos acumulados, ações rejeitadas e erros de decisão. Retornar agregados, tamanho de amostra, sementes utilizadas e resultados individuais consultáveis. Não substituir o julgamento de game design por uma pontuação universal.

## 3. Etapas de implementação (um commit por etapa sugerido)

| Etapa | Entrega | Critério de aceite |
|---|---|---|
| 1. Fundação | projeto .NET, SDK MCP, DI, cliente REST e testes de protocolo | handshake e `tools/list`; erro útil com API offline; config documentada |
| 2. Leitura | status, settings, run, combate, entidade e carta | dados correspondem aos endpoints; nenhuma ação modifica estado |
| 3. Diagnóstico gambits | serviço de trace, validador por setting/revisão e fixtures Alisyum | reproduz caso Archon sem decisão e identifica regras/ações incompatíveis; campanhas atuais verificadas |
| 4. Conteúdo e saves | validação de definições, revisão fixada/atual e integridade | detecta referências inválidas e save antigo sem publicar nem migrar |
| 5. Replay | timeline, verificação determinística e hashes | mesma seed/revisão produz mesmo hash; divergência localizável |
| 6. Sandbox | simulação com limites e cancelamento, controles de isolamento | source run, sequência e hash inalterados; sandbox não mistura saves reais |
| 7. Balanceamento | métricas por carta/encontro e comparação de cenários | relatório reproduzível, seeds e revisões registradas, falhas contadas |
| 8. Integração | smoke test, documentação, config Codex e roteiro Godot+MCP | fluxo de diagnóstico completo, sem intervenção na partida real |

## 4. Testes e segurança operacional

- Criar `tests/HeroScript.Mcp.Tests`; usar xUnit, `TestWebApplicationFactory` existente para contrato e um cliente MCP para teste stdio real. Tests: descoberta, validação de argumentos, API offline, paginação, timeout, erros e cancelamento.
- Cobrir revisão antiga vs. atual, gambit sem candidato, erro de conteúdo, replay com divergência e simulação interrompida. Verificar hashes/commits antes e depois de consultas; confirmar que journal e saves do usuário não mudam.
- Nenhuma operação administrativa ou de mutação de campanha exposta no MVP. Simulação exige instância explícita de teste e capacidades existentes; sem permissão implícita por ser MCP. Não enviar dados de saves a terceiros; limitar dados retornados ao necessário.
- Manter `dotnet test` e testes Godot existentes como fonte de regressão. MCP complementa testes automatizados; não substitui CI. Rodar `tools/test-godot-mcp.mjs` com o editor em idle para testar integração visual.
- Controlar a superfície de ferramentas (não expor todas as rotas automaticamente), registrar duração e códigos de erro em stderr, tratar conteúdo de jogo como dado e não instrução. Fixar dependências, revisar licença e mudanças de versão.

## 5. Validação final com os dois MCPs

| Caso | Godot MCP observa | HeroScript MCP verifica |
|---|---|---|
| Status alterando HUD | árvore, retângulos e screenshots antes/depois | status aplicado, counter e recursos autoritativos |
| Combate travado | botão inativo, logs e estado da tela | turno, ações legais, motivos dos gambits e revisão |
| Partida antiga | tela de continuidade | compatibilidade do save, revisão fixada e sequência |
| Balanceamento | intenção anunciada, feedback e cartas legíveis | dano/custos, distribuição de resultados e replay |

Critério de conclusão: iniciar o showcase em ambiente de teste, reproduzir o erro conhecido de gambit com fixture antiga, obter explicação sem mutação, repetir com conteúdo corrigido e comparar screenshots e hashes. A ferramenta deve distinguir claramente correção no conteúdo atual de uma atualização da partida antiga.

## 6. Referências

- [SDK C# oficial MCP](https://github.com/modelcontextprotocol/csharp-sdk)
- [Protocolo MCP](https://modelcontextprotocol.io/)
- [Godot MCP Bridge — LetsAgents](https://github.com/letsagents/godot-mcp/tree/v1.0.0)
- Contratos REST existentes: `openapi/heroscript-v1.json`, `openapi/heroscript-authoring-v1.json`.
- Controladores: `src/API/Controllers/CombatController.cs`, `CombatCardController.cs`, `RunController.cs`, `RunJournalController.cs`, `ContentController.cs`, `CombatSandboxController.cs`, `SimulationController.cs`.
