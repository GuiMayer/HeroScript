# HeroScript MCP

Servidor de desenvolvimento local que expõe 14 ferramentas especializadas da engine por MCP (stdio), usando .NET 10 e o SDK oficial `ModelContextProtocol` 2.4.0. Não substitui o Godot MCP; a fonte de verdade continua sendo a API REST. Nenhuma ferramenta de gameplay, restauração, publicação ou administração da campanha real é exposta.

## Instalação no Codex (Windows)

Na raiz do repositório:

```powershell
dotnet build tools/HeroScript.Mcp
$dll = (Resolve-Path tools/HeroScript.Mcp/bin/Debug/net10.0/HeroScript.Mcp.dll).Path
codex mcp add heroscript-mcp --env HEROSCRIPT_MCP_API_URL=http://127.0.0.1:5271 --env HEROSCRIPT_MCP_SANDBOX_URL=http://127.0.0.1:5280 -- dotnet $dll
codex mcp get heroscript-mcp
```

O MCP principal usa a API local já iniciada (por padrão, porta `5271`). Não inicia nem encerra a engine real. Ausência da API gera `API_UNAVAILABLE`, com instrução de recuperação. Reinicie o Codex ou abra nova sessão após o registro. O servidor usa stdout somente para MCP; logs em stderr. `HEROSCRIPT_MCP_SANDBOX_URL` é opcional. Sem ele, as três ferramentas de simulação recusam execução.

## API separada para simulações

```powershell
pwsh -File tools/Start-HeroScriptMcpSandbox.ps1 -Port 5280
```

O launcher publica a API em uma pasta temporária própria e cria diretórios independentes para `runs`, `content` e `telemetry`, com nome `HeroScript-McpSandbox-<id>`. Não reutiliza binários da engine real nem saves existentes; não encerra processos na porta. O marcador `/api/v1/diagnostics/mcp-sandbox` verifica ambiente `McpSandbox`, configuração de persistência e capacidades; o servidor MCP o exige antes de qualquer simulação. A instância é local, com perfil de ferramentas `dev_modder` somente para QA e sem habilitar o admin HTTP. Ao terminar, é possível encerrar o PID mostrado pelo launcher. Os resultados ficam na persistência de teste e nunca devem ser importados para a campanha real.

## Ferramentas registradas

| Ferramenta | Entrada principal | Finalidade |
|---|---|---|
| `get_engine_status` | nenhuma | prontidão, contrato/capacidades e settings |
| `list_settings` | `settingId`, `offset`, `limit` opcionais | revisões e settings selecionáveis |
| `inspect_run` | `runId` | estado e acesso autorizado |
| `inspect_combat` | `combatId`, `actorId` opcional | combate autoritativo e ações legais |
| `inspect_entity` | `combatId`, `actorId` | recursos, status e atributos calculados |
| `inspect_card` | `combatId`, `cardInstanceId`, alvos opcionais | avaliação da carta e custos |
| `inspect_gambits` | `runId`, `combatId`, `actorId`, `sequence` opcional | trace canônico da decisão e revisão fixada |
| `validate_gambits` | `settingId`, `revision`, `budget`, `runDefinitionIds` opcionais | matriz de cobertura sintética e falhas |
| `inspect_save` | `runId` | revisão atual vs fixada, sequência, hash e journal quando autorizado |
| `validate_content` | `settingId`, `revision`, `candidate` opcional | validação do bundle, inclusive erros estruturados HTTP 422 |
| `verify_replay` | `runId` ou `combatId`; cursor e limite | replay integral e página da timeline sem alteração de estado |
| `simulate_encounter` | `settingId`, `revision`, `seed`, `scenario`, limites | cenário de QA real, heurística de cartas e fim de turno |
| `simulate_branch` | `runId`, `sequence`, `commands` | branch de QA com comparação do hash da fonte |
| `analyze_cards` | `settingId`, `revision`, `scenario`, `seeds`, `cardDefinitionIds` | amostra reproduzível de custos, HP, efeitos e resultados |

Restrições: loopback, sem redirects, sem shell/SQL/rotas arbitrárias, limite de 8 MiB por resposta REST, timeout por requisição, paginação, cancelamento. Simulações de até 100 comandos e 100 rodadas; análise de até 20 seeds distintas e limite de cinco minutos por lote. A heurística determinística prioriza cartas legais, depois ações de encerramento/prioridade; não estima a estratégia ótima. HP é perda líquida, não dano bruto, e aplicações do candidato são observações da prévia. Os conflitos de versão, permissões e decisões sem candidato não são mascarados.

### Exemplo de validação Alisyum

Obter a revisão com `list_settings`, depois chamar `validate_gambits`:

```json
{
  "settingId": "alisyum",
  "revision": "<revisão retornada por list_settings>",
  "budget": 3000,
  "runDefinitionIds": ["alisyum_chapter_1", "alisyum_servo_run", "alisyum_gambler_run", "alisyum_renegado_run"]
}
```

A lista explícita evita incluir runs herdadas de outros pacotes. O validador usa os poderes possuídos e o reducer real, exercita 12 rodadas × 3 valores de HP × 2 conjuntos (poderes e encerramento). Bindings sem decisões explícitas são contabilizados como não cobertos, não como falha da engine. O trace de uma partida usa o resolvedor de ações legais e regras de sua revisão fixada, inclusive fallback da configuração do modo quando não há binding explícito. A inspeção histórica respeita `IToolAccessPolicy`: campanhas que proíbem `timeline.inspect_state` retornam 403 em vez de elevar a permissão. Inspeção de save básica continua disponível e explica quando o journal está bloqueado.

## Testes reproduzíveis

```powershell
dotnet test tests/HeroScript.Mcp.Tests
dotnet test tests/Core.Tests --filter FullyQualifiedName~GambitDecisionReducerTests
dotnet test tests/API.Tests --filter 'FullyQualifiedName~GambitDiagnosticsTests|FullyQualifiedName~McpSandboxTests'
node tools/test-heroscript-mcp.mjs (Resolve-Path tools/HeroScript.Mcp/bin/Debug/net10.0/HeroScript.Mcp.dll).Path http://127.0.0.1:5280
```

`test-heroscript-mcp.mjs` recusa uma API comum, faz handshake real, descobre e chama todas as 14 ferramentas. `HEROSCRIPT_MCP_REQUIRE_CLEAN_GAMBITS=1` torna falhas de conteúdo um erro do smoke test; por padrão, o teste aceita que um validador encontre falhas legítimas em conteúdo antigo. Nenhum assert usa a partida real. Em modo de campanha normal, operações de timeline e simulação podem ser negadas por política de jogo; isso é comportamento esperado.

## Integração com Godot MCP

O addon LetsAgents 1.0.0 está no showcase (ver [setup](../../examples/godot-engine-showcase/GODOT_MCP_SETUP.md)). Para cruzar uma partida de QA, use uma cópia isolada do showcase com `config/custom_user_dir_name="HeroScript-mcp-qa-<id>"` e editor aberto com estas variáveis:

- `GODOT_MCP_BRIDGE_PORT=6511` (distinto do editor principal em 6510).
- `HEROSCRIPT_MCP_QA_API=http://127.0.0.1:5280`.
- `HEROSCRIPT_MCP_QA_RUN=<ID de run de teste com combate ativo>`.

Copie apenas arquivos do projeto, sem `.godot` e `.runtime`, e deixe o Godot importar o projeto. A cena `res://tests/mcp_sandbox_preview.tscn` recusa o diretório do jogador e API não isolada; pode utilizar `sandbox_run_override` configurado exclusivamente no clone de QA. Ela hidrata a projeção real por REST e abre a tela de combate, sem enviar comandos de gameplay.

```powershell
$env:GODOT_MCP_BRIDGE_PORT='6511'
$server = Join-Path (npm.cmd root -g) '@letsagents/godot-mcp/server.js'
node tools/test-godot-mcp.mjs $server <novo-arquivo.jpg> http://127.0.0.1:5280 <runId> (Resolve-Path tools/HeroScript.Mcp/bin/Debug/net10.0/HeroScript.Mcp.dll).Path
```

O teste abre a cena de QA via Godot MCP, inspeciona a árvore e `qa_evidence`; chama `inspect_combat` via HeroScript MCP independente e compara combate, revisão e hash. Faz screenshot JPEG do viewport e confirma que o hash da run fonte permaneceu igual. O teste encerra apenas sua própria execução de Play.

## Resultado da validação local — 2026-11-20

- 14/14 ferramentas invocadas via MCP stdio real no sandbox. `simulate_encounter`, `simulate_branch` e `analyze_cards` retornaram resultados da engine; a fonte da branch permaneceu inalterada. `verify_replay` retornou válido e hash inalterado. API offline retornou erro explícito de disponibilidade.
- Testes xUnit MCP 11/11; reducer de gambits 7/7; testes de integração de sandbox, gambits e contratos REST passaram. O teste do Furnace Archon em rodada 2 reconstrói os quatro poderes legais da mensagem de erro, registra condições falsas e verifica que adicionar o fallback seleciona candidato sem escrever a combat state.
- Pacote Alisyum **do checkout atual**, revisão `ffc7570aa0439941f6cfc004444bfca71a374259782df26c7f13c0ec107daba0`: 28 bindings das quatro campanhas, 2016 casos sintéticos, 0 falhas. Não é uma prova de todos os estados possíveis, cooldowns, efeitos ou ausência de alvo, e as correções de conteúdo que já estavam pendentes no checkout não fazem parte dos commits MCP.
- Godot 4.7.2 + LetsAgents v1.0.0: 376 nós no combate sandbox default e 580 nós no combate real de Alisyum preparado até o primeiro encontro; ambos com 0 erros de runtime reportados, screenshots válidos, hashes idênticos à engine e testes Play encerrados. Capturas locais em `.runtime/mcp-godot-qa/7b8299342f0b4c6b9bf226f836a6429d/` (`combat-crosscheck.jpg`, `alisyum-crosscheck.jpg`).
- A comparação antiga/nova do Archon foi feita em fixture do reducer, não na partida antiga do usuário. O modo sandbox default não aceita o Archon como ator; não houve bypass da regra de modo. O modo de campanha Alisyum restringe a inspeção histórica, portanto não foi elevado. Não foi produzido screenshot de uma partida antiga travada nem certificado o gameplay completo de cada campanha.

Plano original: [PLANO_HEROSCRIPT_MCP.md](../../docs/plans/PLANO_HEROSCRIPT_MCP.md).
