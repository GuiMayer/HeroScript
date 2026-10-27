# Implementação do loadout unificado

Guia: [conceito aprovado](../design/UNIFIED_LOADOUT_CONCEPT_AND_RESEARCH.md). Todas as guias editáveis salvam automaticamente por operação completa; a engine continua a validar e aplicar comandos. Não há transação global fictícia nem mudança no determinismo.

## Etapas concluídas

1. `b1d930e`: conceito, decisões e baseline. Inscrições já usavam autosave; compartimentos usavam Apply. Alterações locais não relacionadas foram preservadas fora dos commits.
2. `0b3f8b6`: moldura, registro de módulos e laboratório isolado.
3. `6d2afad`: inscrições reais e atributos compartilhados. Incluídos os componentes editoriais de inscrições/atributos e seu catálogo visual que já existiam localmente e são dependências diretas da página.
4. `5188c54`: baralho, compartimentos com autosave e modificadores autorizados.
5. `458a0f7`: visão geral, relíquias e origens dos atributos como leituras canônicas.
6. `ec4964f`: companions planejado, laboratório com componentes reais e navegação unificada.
7. `e45dba8`: localização, foco, drawer e validação responsiva.
8. Commit que atualiza este relatório: regressões de recuperação/versão/nulos, API real, retenção dos componentes de carta e documentação final.

## Verificação prevista

- Navegar/inspecionar não envia mutações; contextos de run/personagem/setting não se misturam.
- Preview/comando/releitura usam o gateway e os workspaces existentes; valores não são calculados na interface.
- Uma alteração completa produz um comando, inclusive uma troca de posições.
- Trocar guia não abandona gravação; voltar aguarda confirmação ou recuperação.
- Falhas e conflitos preservam o último estado confirmado; retry incerto usa o mesmo comando da sessão.
- EN/PT-BR, mouse/teclado, 1280×720, 1920×1080 e 2560×1080; laboratório separado da run.
- API real: persistência, read-only, retry e reentrada; testes existentes de loadout/compartimentos preservados.

## Evidências

- 159 verificações Godot passaram em dez suítes: shell e foco (9), autosave (9), recuperação (9), layout (48), contrato de leitura (6), run_loadout anterior (24), compartimentos anteriores (20), input nativo de inscrições (6), fluxo de mapa (7) e reconciliação de atividades (21). Incluem retenção dos componentes de carta, navegação direcional/accept, restauração de foco e respostas nulas rejeitadas sem erro de script.
- 23 verificações na Godot com API real isolada passaram. Em Ascendant, uma inscrição foi equipada enquanto outra guia estava aberta; em Deck Workshop, um compartimento foi salvo sem Apply. Navegação não alterou sequência; ambas as runs foram reabertas e verificadas por replay semântico. O teste também abriu a página pelo router real do jogo e voltou à run nos dois settings.
- 5 testes C# da API passaram, incluindo controllers de inscrições/compartimentos e jornada de compartimentos repetida em três hosts, com persistência e replay.
- Capturas renderizadas em 18 combinações de guia/idioma/resolução foram produzidas e as telas de inscrições EN/1920, PT-BR/1280 e baralho EN/1920 foram inspecionadas.
- Cópia do índice do Git: importação e cinco suítes novas passaram após uma repetição do import. A primeira importação encerrou com falha nativa do Godot ao importar SourceSans3.ttf; a repetição terminou sem erro de script. Nenhum workaround foi introduzido no jogo.

Os testes reais usaram uma API própria na porta 61989 e diretórios em `.runtime/unified-loadout-api`, sem editar saves ou interromper as APIs do jogador. A aplicação de modificadores teve regressão de coordenação/preview/opção anunciada em fixture; as duas jornadas Godot reais cobriram inscrições e compartimentos, não uma nova jornada de crafting de modifiers.

O Core não foi alterado e a versão determinística permanece 34. Companions continua não implementado. Relíquias são de inspeção; modificar cartas continua restrito às opções anunciadas pela engine. Guia operacional: [loadout unificado](../guides/unified-loadout-godot.md).
