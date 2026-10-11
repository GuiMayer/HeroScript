# Base visual anterior à repaginação Marfim

Data: 2026-10-11. Etapa 0 do [plano de repaginação](../../../plans/GODOT_IVORY_VISUAL_REDESIGN_PLAN.md).

## Escopo e procedência

Estas são capturas **renderizadas pela Godot com os componentes de produção**. Os snapshots, ações e registro de replay são fixtures sintéticas. Não são imagens geradas, capturas de uma run real ou comprovação da integração REST.

Base funcional preservada: commit `d0d42cb`, que separa descrição genérica da carta e previsão contextual da ação. As alterações anteriores de código, traduções, diretrizes e regressões foram revisadas e salvas nesse commit, separadamente da preparação do redesign.

O tema permanece o anterior. Nenhuma regra, conteúdo de setting, preferência do jogador, run ou camada de comunicação foi alterada para produzir a base.

Referências distintas:

- [Marfim escolhido](../../interface-concepts/2026-10-11/02-ivory.png): objetivo artístico ilustrativo, não screenshot funcional.
- [Métricas](metrics.json): capturas, driver, versão, contexto e amostras de atualização visual.
- [Validação e inventário](validation.json): resultados dos testes e triagem dos arquivos de UI.

## Capturas

Todos os dez estados abaixo são registrados em 1280×720 e 2560×1080. A captura força o tamanho exato do canvas, preservando o stretch/expand de produção com referência 1440×900. A janela normal do jogo pode reduzir a resolução solicitada para caber na área útil do desktop; essa proteção não é alterada no cliente.

Idioma das capturas: inglês. Escala de texto: 1,0. Movimento reduzido: habilitado, para não comparar frames de animações diferentes. Os testes de layout complementam as imagens com português e escala 1,2.

| Estado | 1280×720 | 2560×1080 | O que preservar |
| --- | --- | --- | --- |
| Principal | [imagem](menu-1280x720.png) | [imagem](menu-2560x1080.png) | Setting e ações; offline não inventa continuidade. |
| Combate, mão cheia | [imagem](combat-hand-1280x720.png) | [imagem](combat-hand-2560x1080.png) | Atores, intenções, mão compartilhada e comandos. |
| Preview separado | [imagem](combat-preview-1280x720.png) | [imagem](combat-preview-2560x1080.png) | Nome do alvo e delta fora da carta. |
| Sidebar aberta | [imagem](combat-sidebar-1280x720.png) | [imagem](combat-sidebar-2560x1080.png) | Painel flutuante sem reduzir largura do campo. |
| Pausa | [imagem](pause-1280x720.png) | [imagem](pause-2560x1080.png) | Modal acima das cartas e sidebar. |
| Mão vazia | [imagem](combat-empty-1280x720.png) | [imagem](combat-empty-2560x1080.png) | Composição estável e mensagem legível. |
| Jornada | [imagem](journey-1280x720.png) | [imagem](journey-2560x1080.png) | Mapa e ação publicada; sem execução automática inventada. |
| Oferta de cartas | [imagem](offer-1280x720.png) | [imagem](offer-2560x1080.png) | Mesmo componente da carta, aquisição distinta do custo de uso. |
| Configurações | [imagem](settings-1280x720.png) | [imagem](settings-2560x1080.png) | Navegação, categorias e preferências locais. |
| Replay | [imagem](replay-1280x720.png) | [imagem](replay-2560x1080.png) | Consulta passiva do registro sintético, sem comandos de jogo. |

O personagem e os inimigos recebem valores deliberadamente simples. As cartas usam suas apresentações e slots existentes; o gateway sintético não fornece inspeções completas da engine. Portanto, essas capturas não servem para avaliar balanceamento, fórmulas ou conteúdo completo de cartas.

## Testes registrados

`validation.json` é a evidência executável: lista códigos de saída, quantidade de checks, duração de processo e avisos. Duração total de um processo de teste não é latência de gameplay.

| Teste | Fronteira protegida |
| --- | --- |
| `layers.gd` | Comunicação, sessão, projeções e playback. |
| `card_preview_separation.gd` | Quantidade de origem, alvo genérico e resultado contextual separado; snapshots não mutados. |
| `card_presentation.gd` | Anatomia, comparações numéricas, raridade, tooltip, inspeção e localização. |
| `card_hand_lab.gd` | Laboratório compartilhado e ausência de alterações nas preferências/sessão. |
| `hand_ux.gd` | Layout e interação da mão, inclusive grandes quantidades. |
| `pause_layers.gd` | Camadas, foco e bloqueio de input do modal. |
| `combat_stage_layout.gd` | Resoluções, idiomas, escala, preview, sidebar e estabilidade de composição. |
| `card_play_selection.gd` | Uso da carta não seleciona outra automaticamente. |
| `resolutions.gd` | Opções e persistência de resolução, usando preferências isoladas. |
| `visual_baseline.gd` | Capturas, geometria, canvas, replay sintético, leituras passivas e ausência de HTTP. |

Aviso preexistente: alguns processos reportam **duas instâncias ObjectDB não liberadas ao encerrar**. Registrar separadamente de falhas; esta etapa não introduz uma correção de ciclo de vida nem declara o aviso resolvido. A emissão varia entre execuções; os diagnósticos da execução registrada estão no JSON.

## Métricas e limites

O roteiro mede `refresh_state()` de combate e jornada, em dois cenários:

1. Mesma sequência, sem mudança de estado.
2. Sequência incrementada, mantendo o conteúdo visual simples da fixture.

São 20 amostras por cenário e resolução. O JSON contém mediana e p95 em milissegundos, leituras do gateway sintético e número de requisições HTTP reais observadas. Entre amostras, há espera por frames para permitir processamento das operações diferidas.

**O cronômetro cobre apenas a chamada síncrona de atualização.** Não inclui espera pelos frames, desenho da GPU, layout diferido, rede, persistência ou execução da engine. Não representa tempo clique→resultado nem garante uma resposta REST inferior a um segundo.

Comportamento observado na fixture:

- Atualização de combate sem mudar a sequência não solicita nova inspeção.
- Mudança de sequência solicita uma leitura sintética de mão por atualização.
- Jornada, nesta fixture, atualiza apenas apresentação; não solicita uma leitura do gateway.
- Captura do replay solicita timeline e commit sintéticos; nenhuma dessas leituras é HTTP real.
- Todos os fluxos usam zero requisições HTTP reais. Essa é uma garantia de isolamento, não o orçamento esperado para gameplay online.

Ao comparar futuras etapas, repetir o roteiro no mesmo ambiente e considerar a versão da Godot, cache aquecido, resolução, escala e driver. Não comparar diretamente timings headless com timings renderizados.

### Validação online não executada

Durante esta etapa, não havia listener nas portas locais 5260/5271 usadas pelo projeto. Assim, testes como `gameplay_polish.gd`, `hand_online.gd`, fluxo real de progressão e replay persistido não foram executados.

Antes de liberar a repaginação completa, complementar a base com uma API ativa e os testes online indicados no plano. As fixtures não substituem essa verificação.

## Inventário e ordem de migração

O JSON inventaria os arquivos `.gd` de `scripts/ui/`, com referências a `AppTheme`, construções `Color(...)`, cores embutidas em texto e overrides de tema. São contagens de ocorrências para triagem, não quantidade de bugs ou prova de que toda cor deve ser removida.

Nesta base há **83 construções `Color(...)` e 148 chamadas de override** nesse diretório. Overrides de espaçamento, fonte e cores semânticas podem ser legítimos. Também existem cores dentro de BBCode no histórico/replay, que não aparecem ao procurar somente `Color(...)`.

| Grupo | Arquivos principais em `scripts/ui/` | Destino |
| --- | --- | --- |
| Tema global | `app_theme.gd` | Perfil Marfim, papéis semânticos, controles e alto contraste. |
| Fundo | `ember_background.gd` | Papel discreto e ornamento estático, sem redesenho contínuo desnecessário. |
| Cartas | `card_view.gd`, `card_visual_style.gd`, `card_effect_text.gd`, `card_details_panel.gd` | Anatomia marfim, efeitos invertidos, cores numéricas e raridade compartilhadas. |
| Interação da mão | `card_hand_view.gd`, `hand_layout.gd`, `hand_visual_style.gd`, `hand_interaction.gd`, `hand_hit_surface.gd` | Preservar autoridade/algoritmos; ajustar estilo sem nova implementação concorrente. |
| Campo e recursos | `combat_screen.gd`, `actor_panel.gd`, `actor_portrait.gd`, `resource_view.gd` | Silhuetas, intenções, barras e comandos com recursos genéricos. |
| Overlays | `floating_hud.gd`, pausa em `main.gd` | Painel flutuante, alça recolhida, camada e input preservados. |
| Jornada | `activity_screen.gd`, `route_view.gd`, `dialogue_panel.gd` | Mapa impresso, estados publicados e decisões legíveis. |
| Menus | `main.gd`, `settings_screen.gd` | Hierarquia editorial, navegação existente e controles nativos completos. |
| Histórico | `run_history_screen.gd`, `run_replay_screen.gd`, `timeline_screen.gd`, `historical_combat_view.gd` | Mesma linguagem e indicação de contexto histórico; migrar cores BBCode. |
| Conteúdo/ferramentas | `codex_screen.gd`, `sandbox_screen.gd`, `card_zone_tool_dialog.gd` | Componentes compartilhados e informação técnica acessível. |
| Inspeção | `inspection_dialog.gd`, `hand_overview_dialog.gd` | Detalhes persistentes/roláveis; tooltip permanece passivo. |
| Artes | `art_slot.gd`, catálogo e manifesto existentes | Trocar placeholders por slot; nomes, números e regras continuam na UI. |

### Controles e estados que não podem ficar de fora

- Botões: normal, hover, pressionado, desabilitado, foco e seleção, sem mudança de tamanho entre estados.
- Inputs: LineEdit/TextEdit, placeholder, texto selecionado e erro.
- Opções: OptionButton/PopupMenu, toggles, sliders e controles de bindings.
- Leitura: Label/RichTextLabel, barras, listas, scrollbars e texto extenso.
- Modais: pausa, confirmação de abandono/compra/transformação, inspeção e visão geral.
- Aplicação: carregando, desconectado, erro, oferta vazia, mão vazia e run encerrada.

## Como repetir

Na raiz do repositório:

```powershell
./tools/capture-ivory-baseline.ps1
```

Opcionalmente, indicar outro executável em `-Godot` e uma pasta de resultados diferente em `-OutputDirectory`. `-Headless` executa os checks e métricas sem produzir imagens; não deve substituir uma captura renderizada.

O roteiro:

1. Copia a demo para uma pasta única em `.runtime/ivory-baseline/`.
2. Configura um diretório `user://` exclusivo nessa cópia.
3. Importa os recursos e executa os testes offline.
4. Renderiza os componentes originais com fixtures, sem conexão automática.
5. Grava capturas, métricas e inventário, mantendo logs na cópia isolada.

As cópias e preferências de teste ficam fora dos arquivos versionados. Não há limpeza recursiva automática, alteração da configuração original, inicialização de servidor ou escrita em saves do jogador.

## Próximo marco

Etapa 1: especificação visual e recursos — perfil Marfim, fontes locais licenciadas e extensão mínima do estilo de cartas. A base deste diretório deve continuar como referência do tema anterior; futuras capturas do redesign devem usar outro diretório.
