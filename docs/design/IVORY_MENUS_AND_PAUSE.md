# Marfim — menus e pausa

2026-10-11. Expansão do [menu principal](IVORY_MAIN_MENU.md), sem repaginar a run.

## Escopo

| Tela/contexto | Aparência |
| --- | --- |
| Menu principal, configurações, histórico, laboratório de regras e códice | Marfim. |
| Replay aberto pelo histórico | Marfim; permanece leitura de registros. |
| Pausa e confirmação de abandono | Modal Marfim sobre a run escurecida. |
| Configurações abertas pela pausa | Marfim; retorno restaura o gameplay anterior. |
| Combate, jornada, timeline e replay aberto pelo resultado da run | Perfil anterior. |

Cartas, regras, recursos, áudio, remapeamento, progressão e callbacks REST continuam com as mesmas autoridades. Iniciar um cenário pelo laboratório muda para a composição de gameplay, não mantém a skin do editor sobre o combate.

## Implementação visual

Os menus reutilizam `ivory_theme.tres`: papel, tinta escura, Cinzel/Source Sans 3, cantos pequenos e acento vermelhão. A paleta agora cobre também painéis/categorias/linhas de configurações. `AppTheme` padroniza listas, seleção/foco, editores JSON, divisores, sliders, tooltips e dialogs nativos. O slider tem marcador de tinta legível sobre papel.

Não existe uma segunda implementação de configurações, histórico, replay ou laboratório. Os componentes e controllers existentes recebem o perfil apropriado por rota. Corrigidos o rótulo Seed que podia virar coluna vertical e o painel do editor de cenário que não expandia com a janela.

A pausa mantém o `CanvasLayer` modal, bloqueio de input, confirmação de abandono e restauração de foco. O circuito do painel é estático e removido em alto contraste; retomar é a ação principal.

## Pausa isolada

O perfil global do gameplay não é trocado ao abrir a pausa. O shell deriva um `VisualContext` de apresentação com o perfil Marfim e materializa um `Theme` nativo independente.

`AppTheme.scoped_profile()` é uma fronteira **síncrona** para construir/reaplicar os estilos de um overlay: empresta o perfil aos helpers e restaura o original antes de retornar, sem incrementar a geração global. Não usar `await`, enviar comandos ou disparar ações da aplicação dentro desse callback. Foco, ações e confirmação acontecem fora da construção; a confirmação herda o Theme do modal.

Alterar escala/alto contraste reaplica a pausa pelo contexto derivado. Não troca o tema da árvore de gameplay nem cria outro cursor de playback. Isso não transforma a infraestrutura em suporte geral a skins independentes simultâneas.

## Validação

### Interação com configurações

Salvar uma preferência não representa uma troca de idioma. `I18n.set_locale()` normaliza o idioma solicitado e só emite `locale_changed` quando o idioma efetivo muda; a inicialização do tradutor nativo permanece garantida. Assim, sliders e toggles não provocam a reconstrução da tela, perda de foco ou retorno indevido para Geral.

`settings_interaction.gd`, incluído na seleção padrão do runner, testa cliques reais e alterações consecutivas nos seis sliders, nas três categorias que os possuem, em inglês/português e com entrada pelo menu/pausa. Confere identidade da tela e do slider, categoria, foco, persistência local, volume geral aplicado e atualização de tradução quando o idioma realmente muda. Executa apenas em um diretório de preferências isolado, sem comandos REST.

A auditoria das demais opções resultou em correções de recuperação de controles, normalização de preferências/endereço, silêncio real, estado de conexão e velocidade do replay. O [contrato de configurações](GODOT_SETTINGS_BEHAVIOR.md) descreve comportamento, limitações e regressões específicas. O menu agora distingue conexão em andamento de servidor desconectado e permite reconectar sem criar uma run.

```powershell
./tools/test-godot-ui.ps1 -Tests pause_layers,ivory_menus,settings_contracts,settings_interaction,resolutions,setting_progression,view_replacement,theme_contracts,ivory_menu -VisualTest ivory_menus_visual -VisualOutputDirectory .runtime/ivory-menus
```

O runner cria projeto/user directory isolados. A nova suite percorre os menus e todas as seis categorias de configurações em EN/PT, 1280×720 e 2560×1080; PT usa texto a 120%. A suite de resoluções existente cobre as opções adicionais. Confere pausa sobre combate com componentes reais, foco/input, tema e identidade da carta, contagem de configuração, estado imutável, leituras e retorno de configurações/replay. Não submete comandos de gameplay nem HTTP real.

Durante a verificação, houve encerramentos nativos `0xc0000005` do executável Godot 4.7.2 no importador/teardown, sem erros de script. O runner ganhou `-ReuseProject`: aceita apenas uma cópia isolada previamente importada, atualiza os fontes e reutiliza seu cache. A execução final usou a cópia que havia importado/encerrado normalmente; todas as suites finais encerraram com código 0. Não foi suprimido o erro nem atribuído seu motivo a uma causa não demonstrada.

[Capturas e relatório final](ivory-menus/2026-10-11/README.md). A repaginação da run continua pendente.
