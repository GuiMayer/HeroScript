# Cliente Godot para o sandbox de combate

Este pacote transforma uma Godot vazia em um cliente fino da engine. Todas as
regras permanecem no HeroScript; a Godot envia comandos, reproduz os frames do
receipt e renderiza snapshots.

## Instalação

1. Copie `addons/heroscript/HeroScriptClient.gd` para o mesmo caminho no projeto.
2. Adicione o script como Autoload chamado `HeroScriptClient`.
3. Inicie a API em `http://127.0.0.1:5260`, ou defina `HEROSCRIPT_BASE_URL`.
4. Conecte `snapshot_changed`, `frame_ready`, `input_changed` e
   `request_failed` na sua cena.
5. Chame `await HeroScriptClient.launch_sandbox(seed)`.

O estado de UI deve vir exclusivamente de `snapshot.combat.actors` e
`snapshot.hand`. Descubra o ator local por `controllerBinding.kind == "Player"`;
não suponha herói, inimigo ou recurso `health`.

## Loop canônico

1. Mostre as ações retornadas por `legal_actions()`.
2. Para uma carta, chame `play_card(cardInstanceId, targetIds)`.
3. Trate o receipt inteiro como a confirmação atômica do comando.
4. Anime cada `frame_ready` na ordem de `index`. Se a UI precisa aguardar uma
   animação, ative `pause_between_frames` e chame `acknowledge_frame()` ao fim.
5. Só aceite novo input quando `input_changed` retornar `true`.

Conflitos `409` fazem o cliente recarregar o snapshot antes de permitir outra
tentativa. Repetir o mesmo `commandId` na API retorna o mesmo receipt com
`duplicate: true`.

## Timeline e branches

`load_timeline()` retorna commits. A seleção visual é a coordenada
`(runSequence, frameIndex)`: a sequência escolhe o estado autoritativo, e o
índice escolhe um frame dentro daquele comando. Use `select_timeline_frame`,
`load_historical_state`, `create_and_switch_branch` e `load_branch_tree` para
navegação e theory crafting sem mutar a linha original.

O contrato completo está em `openapi/heroscript-v1.json` e o fluxo de integração
em `docs/api/combat-sandbox.md`.
