# Godot MCP Bridge (LetsAgents)

Integração de desenvolvimento local; não é parte das regras do HeroScript.

## Instalação usada

- Godot 4.7.2 Steam e Node.js 24.14.0.
- Addon LetsAgents [v1.0.0](https://github.com/letsagents/godot-mcp/tree/v1.0.0), commit `b96b855cf211e5aa46698fba57c04d95f89ce1f2`, instalado em `addons/godot-mcp/` com licença MIT.
- Servidor npm `@letsagents/godot-mcp@1.0.0`, instalado globalmente. O addon está habilitado em `project.godot` e gerencia seu autoload `McpRuntime`.
- A ponte usa apenas `127.0.0.1:6510`. Não expor a porta externamente.

Para registrar em outro ambiente Windows, com Node.js >= 20 e Codex CLI disponíveis:

```powershell
npm.cmd install -g @letsagents/godot-mcp@1.0.0
$npmRoot = npm.cmd root -g
$serverPath = Join-Path $npmRoot '@letsagents/godot-mcp/server.js'
codex mcp add godot-mcp --env GODOT_MCP_BRIDGE_PORT=6510 -- node $serverPath
codex mcp get godot-mcp
```

Abra este projeto no editor (não somente o executável standalone). O Codex pode exigir nova sessão/reinicialização para reconhecer um servidor adicionado depois da inicialização. O MCP pode lançar a cena a partir do editor ou observar uma sessão Play iniciada no editor. Não reinicie uma partida que o usuário esteja utilizando.

## Validação repetível

No diretório raiz HeroScript, com o editor aberto e jogo parado:

```powershell
$serverPath = Join-Path (npm.cmd root -g) '@letsagents/godot-mcp/server.js'
$shotPath = Join-Path (Resolve-Path 'examples/godot-engine-showcase/.runtime') ('godot-mcp-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.jpg')
node tools/test-godot-mcp.mjs $serverPath $shotPath
```

Crie `.runtime` se ainda não existir. O teste usa JSON-RPC MCP real via stdio, verifica que nenhuma sessão já está em Play, inicia a cena principal, testa ping, árvore, inspeção, erros e screenshot, e encerra apenas a própria execução. Não abre ou avança uma campanha. Captura reduzida a 1024 px de largura conforme o addon.

Resultado local (2026-11-20): servidor 1.0.0, 41 ferramentas, 84 nós na tela principal, inspeção `HeroScriptShowcase` bem-sucedida, zero erros de runtime reportados, JPEG de 39.215 bytes, execução encerrada. O screenshot mostra o menu no estado de preparação de dados; não certifica conectividade da API nem gameplay completo.

## Uso e limites

- `get_scene_tree`, `inspect_node`, `screenshot` e `get_runtime_errors`: primeira escolha para diagnóstico visual.
- `play_scene`/`stop_scene`, `simulate_input`, `set_node_property` e `call_node_method`: apenas em execução de teste ou com autorização, pois podem alterar a sessão. Para gameplay, preferir fixtures/sandbox.
- Antes de distribuição/exportação, desabilitar o addon em Project Settings > Plugins e confirmar que `McpRuntime` foi removido. Não habilitar ponte de depuração na build pública.
- O servidor MCP não altera a arquitetura REST da engine. Ver [plano do MCP próprio](../../docs/plans/PLANO_HEROSCRIPT_MCP.md).

Para o teste cruzado real dos dois MCPs e uso da cena de QA, ver [HeroScript MCP](../../tools/HeroScript.Mcp/README.md). O editor padrão permanece na porta 6510; cópias isoladas usam porta diferente.
