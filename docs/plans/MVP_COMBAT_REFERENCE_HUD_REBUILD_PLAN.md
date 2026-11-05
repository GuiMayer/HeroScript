# Reconstrução da composição de combate MVP — validação visual por etapa

Atualizado em: 2026-11-05. Estado: etapa 1 aprovada; etapa 2 implementada, aguardando aprovação visual; etapas 3–7 não iniciadas.

## Direção aprovada e substituição do plano anterior

Este documento substitui a versão anterior deste plano. A referência será usada **apenas para a composição da cena**: posições, proporções, agrupamento, separação das artes e informações e hierarquia espacial.

**Manter integralmente o tema atual:** paleta, fontes, fundos, ícones, bordas, materiais e estados visuais dos componentes. Não criar tema escuro, molduras góticas, texturas metálicas, correntes, novo catálogo de ícones ou estilos de botão inspirados na imagem. Botões e elementos da HUD serão reorganizados usando o visual que o projeto já tem.

**Não manter compatibilidade estrutural com a cena antiga do MVP.** A nova árvore será a autoridade; não conservar caminhos antigos, nós vazios, aliases, flags de compatibilidade ou reparenting apenas para satisfazer a montagem anterior. Os bindings devem apontar para a estrutura nova. Reutilizar comportamento existente não significa preservar sua disposição antiga.

O escopo continua sendo a cena exclusiva do setting `prototype-test`. Isso não autoriza apagar componentes compartilhados ou alterar a apresentação dos demais settings. Preservar outros settings é isolamento de escopo, não compatibilidade com a cena MVP substituída.

## Referências e diagnóstico

- [Mapeamento de componentes da interface de combate](Mapeamento%20de%20componentes%20da%20interface%20de%20combate.md), mantido intacto. Suas coordenadas são estimativas iniciais.
- Imagem anexada: `codex-clipboard-e54fda37-c636-4e70-88b5-26abd5cf8ab5.png`, 1024 × 572.
- Cena atual: `examples/godot-engine-showcase/scenes/ui/screens/combat_mvp.tscn`.
- View atual: `scenes/ui/components/combat_mvp_view.tscn` e `scripts/ui/combat_mvp_view.gd`.

Hoje o cabeçalho é uma única linha, pilhas e mão compartilham o footer lateral e a energia é um Label dentro da mão. `combat_view.gd` depende de caminhos e containers da composição anterior, enquanto a view MVP compensa o alinhamento com `Balance`. Essa montagem deve ser substituída na variante MVP, sem esconder a árvore nova atrás de outra montagem em runtime.

Os sistemas existentes de alvo, recursos, status, leitura de coleções, cenário e apresentação já fornecem comportamentos reutilizáveis. A refatoração deve preservar esses serviços e atualizar seus vínculos, sem copiar um segundo controlador de combate.

## Restrições permanentes

1. Preservar a engine C#, JSONs de gameplay, REST, regras, determinismo, imutabilidade e saves.
2. Preservar cartas e tooltips: cenas, conteúdo, arte, custo, fontes, dimensões e estilos atuais.
3. Preservar a mão: fan, hover, hit testing, seleção, drag, reordenação, pagamento inline e animações/durações. Só seu host muda de posição.
4. Reutilizar o controlador, presenters, playback e executores atuais. Não criar outra fila de animações ou lógica local de regra.
5. Montagem estática em `.tscn`, editável na Godot. Scripts apenas vinculam nós, apresentam dados e calculam o layout genuinamente dinâmico.
6. Quantidades de atores e zonas continuam variáveis. A imagem não autoriza inventar um aliado, uma zona, um limite de energia ou um contador de ativações.
7. Não reintroduzir Pass, Hand Overview, Action Preview permanente ou timeline na HUD MVP.
8. Preservar alterações locais alheias, inclusive as de tema e idiomas. A baseline visual deve considerar o tema efetivamente presente no início da implementação.

## Composição final pretendida

Área lógica inicial de 1920 × 1080, com ajuste uniforme ao espaço útil da tela. Não mudar as preferências globais de vídeo. Para outras proporções, centralizar a composição sem distorcer os componentes; a adaptação final permanece trabalho posterior.

| Região | Localização inicial | Composição |
| --- | --- | --- |
| Identidade | superior esquerdo | Retrato, nome e informação adicional disponível |
| Rodada | centro superior | Número da rodada e ator ativo abaixo, independentes dos textos laterais |
| Ações secundárias | superior direito | Log, inspeção e pause com os estilos/ícones atuais |
| Pilhas | coluna esquerda, aproximadamente y 38–99% | Draw, Exile, Discard e Owned Deck, com contadores existentes |
| Aliados | lado esquerdo do campo | Arte e placa de informações separadas; aliado adicional só se existir |
| Oponentes | lado direito do campo | Arte e placa de informações separadas, suportando múltiplos atores |
| Energia | centro, acima da mão | Ícone e valor atuais do recurso, sem teto inventado |
| Mão | centro inferior | Mesmo componente e mesmas cartas, sem redesenho |
| Encerrar turno | canto inferior direito | Botão atual reposicionado, fora do host da mão |
| Overlays | flutuantes | Drawer, inspeção, coleções, seleção, recuperação e pause sem deslocar a cena |

Não copiar o corte das cartas pela borda da imagem. Garantir a área atual de hover e interação antes de aproximar os grupos vizinhos. Medir o espaço útil após as margens do shell para não introduzir dupla margem.

## Organização da árvore

```text
CombatScreen                         # mesmo papel de adaptação da sessão
└── CombatView                       # nova composição exclusiva MVP
    ├── CombatFeedback               # integração existente
    ├── ReferenceCanvas              # área lógica; nós posicionados no editor
    │   ├── Header
    │   │   ├── ActiveIdentity        # retrato e textos como nós locais
    │   │   ├── RoundAndTurn          # rodada e ator ativo
    │   │   └── Actions               # botões existentes
    │   ├── Battlefield
    │   │   ├── AlliesHost
    │   │   └── OpponentsHost
    │   │       └── ActorView × N     # comportamento existente; composição local
    │   ├── PileRail
    │   │   ├── PileButton × N        # componentes atuais
    │   │   └── OwnedDeckButton
    │   ├── EnergyIndicator           # nós locais
    │   ├── HandHost
    │   │   └── CardHandView          # componente atual inalterado
    │   ├── EndTurnAndContextActions
    │   └── HandTools                # toolbar externa já suportada
    ├── FloatingCombatHUD            # overlays existentes
    └── CombatLog                    # janela atual
```

A árvore final pode ajustar nomes para os componentes reais, sem preservar a hierarquia anterior. Uma seção estática não deve virar cena só para encapsular um Label. Componentes reutilizados ou com comportamento próprio continuam cenas.

O adaptador atual contém referências tipadas e contratos que precisam ser conferidos antes de trocar a view. Ajustar apenas os vínculos necessários para usar a nova árvore; não manter ancestrais fictícios nem duplicar regras/input para contornar esse trabalho. Qualquer alteração compartilhada deve manter os consumidores reais dos outros settings funcionando.

## Processo de validação obrigatório

Cada etapa abaixo entrega **uma mudança visual concreta**, visível na cena real do MVP, e termina com um commit próprio.

Ao concluir uma etapa:

1. Executar os testes afetados e abrir a cena na Godot.
2. Produzir uma captura em 1920 × 1080 com fixture identificada e descrever o que mudou.
3. Registrar o commit e eventuais limitações reais de dados.
4. Apresentar o resultado ao usuário e **aguardar aprovação antes de implementar a próxima etapa**. Pedido de ajuste mantém a etapa atual aberta.

Preparação, bindings e testes fazem parte da etapa visual correspondente. Não haverá uma etapa técnica separada sem resultado visível, nem avanço automático por tempo decorrido. O commit registra a entrega revisável; a aprovação visual libera o próximo bloco.

## Etapa 1 — Nova base da cena e mão no centro inferior

**Mudança que será vista:** o MVP passa a usar uma nova composição em canvas, com a mão atual ancorada no centro inferior e espaço livre para o campo. O tema e as cartas permanecem iguais.

- Registrar capturas e métricas atuais das cartas/mão antes de alterar a montagem.
- Reconstruir a raiz/hosts da cena MVP; remover a dependência de `Balance` e do footer antigo nessa variante.
- Posicionar apenas o host da mão. Reutilizar o componente atual, sem editar sua geometria interna.
- Conectar a nova árvore ao adaptador de sessão e garantir que a rota real `combat_mvp` a utiliza, não apenas uma fixture ou laboratório.
- Manter os demais controles funcionais em hosts provisórios explícitos enquanto aguardam suas respectivas etapas; isso não constitui fallback para a cena antiga.
- Aplicar escala uniforme e respeitar o retângulo útil do shell.

**Validação visual solicitada:** posição e altura da mão, respiro até a borda, espaço do campo e confirmação de que o tema está intacto. Conferir 0/2/5/12 cartas e hover sem corte.

**Testes:** seleção da rota MVP, montagem no editor, `ui_standalone_preview`, `hand_selection`, `hand_hover_motion`, pagamento inline e baseline de dimensões das cartas. Nenhum ancestral antigo exigido para a montagem funcionar.

**Gate:** aprovar a base da nova composição antes de mover os demais grupos.

## Etapa 2 — Cabeçalho com três regiões independentes

**Mudança que será vista:** identidade/retrato à esquerda; rodada central, turno logo abaixo; log, inspeção e pause à direita.

- Usar fontes, ícones, bordas, superfícies e estados de botão atuais, sem redesenho de skin.
- O centro da rodada não depende da largura do nome nem dos botões laterais.
- Alimentar identidade e turno pelos modelos reais, com fallback atual de retrato.
- Não atribuir ao herói o contador do suporte: o campo conhecido `supportCombat.remainingOwnerActivations` deve continuar identificado como dado do suporte. Omitir informação sem contrato correspondente.
- A lupa/inspeção aciona a inspeção existente da carta ativa; tooltip e disponibilidade esclarecem a função.
- Conectar sinais fixos no editor e manter atalhos remapeados.

**Validação visual solicitada:** alinhamento dos três grupos, proporção do retrato, hierarquia da rodada e ausência de cortes em nomes longos.

**Testes:** troca de ator, texto longo, PT/EN, retrato ausente, foco e pause, sinais sem duplicação após refresh.

**Gate:** aprovar cabeçalho antes de reorganizar as pilhas.

## Etapa 3 — Pilhas em coluna à esquerda

**Mudança que será vista:** Draw, Exile, Discard e Owned Deck saem da área direita e passam à lateral esquerda, com os botões/ícones atuais e contadores legíveis.

- Reutilizar botões de pilha e coleção atuais. Alterar posicionamento, alinhamento e agrupamento, sem trocar o tema.
- Manter endereços `zoneId`, `ownerId`, `scopeId`, ordem autorizada e restrições de visibilidade.
- Confirmar a fonte publicada de Owned Deck; não fabricar inventário pela soma das zonas. Se faltar contrato, registrar a lacuna e não apresentar dado fictício.
- Continuar abrindo container com cartas, não lista de nomes.
- Atualizar âncoras de origem/destino para os botões reais já montados.
- Garantir que a área de abertura do drawer pela borda esquerda não intercepte cliques nas pilhas.

**Validação visual solicitada:** largura da coluna, ordem dos botões, contadores, distância da mão e do campo.

**Testes:** coleção vazia/oculta, retorno de foco, `card_collections`, `card_zone_motion`, `card_draw_pose`; compra deve sair do novo ponto correto sem mudar sua duração.

**Gate:** aprovar coluna e interação antes de reorganizar os atores.

## Etapa 4 — Campo de batalha e arte separada das informações

**Mudança que será vista:** aliados à esquerda e inimigos à direita, com artes livres no campo e placas compactas abaixo; deixam de parecer grandes cartões de informação.

- Reutilizar as artes atuais, o contrato de ActorPanel e os componentes atuais de recursos/status.
- Reorganizar os nós da variante MVP para separar a área de arte da placa de nome/recursos/intenção. Retirar fundos que envolvem a unidade inteira usando a composição, sem alterar a paleta ou desenhar uma nova skin.
- Manter barras, contadores, ícones e fontes atuais. A largura da arte pode diferir da largura da placa.
- Preservar lookup de recursos/status, sinais de alvo, highlights, feedback e âncoras do cenário.
- Suportar quantidades variáveis de atores; a imagem de três unidades é somente a fixture principal.
- Não inventar vida de companion ou intenções ainda não reveladas.

**Validação visual solicitada:** ocupação do campo, separação entre equipes, altura das placas, legibilidade e espaço acima da mão.

**Testes:** herói sem aliado e com aliado real, 1/2/4 oponentes, seleção de alvo, dano/cura/status, ator removido, nomes longos e composição em resoluções menores.

**Gate:** aprovar campo e placas antes de finalizar os controles inferiores.

## Etapa 5 — Energia central e Encerrar turno no canto inferior direito

**Mudança que será vista:** energia acima do centro da mão e botão Encerrar turno isolado no canto inferior direito, com espaço próprio.

- Mover o indicador atual de energia para o host central e usar o ícone já existente.
- Mostrar o recurso real do ator de input, inclusive valores acima do máximo quando permitidos. Não impor teto de três ou apresentar `/3` fixo.
- Mover o botão atual End Turn sem redesenhá-lo ou alterar disponibilidade/comando.
- Manter avançar/pular/cancelar acessíveis apenas nos estados aplicáveis; não introduzir painel permanente de Action Preview.
- Não alterar o tamanho, estilo ou fan das cartas para acomodar os novos controles.

**Validação visual solicitada:** energia, distância até a mão, destaque espacial de End Turn e ausência de colisão com hover/tooltip.

**Testes:** energia zero/excedente/ausente, end turn bloqueado, teclas remapeadas, pause durante resolução, hover e seleção próxima ao botão.

**Gate:** aprovar a faixa inferior antes de consolidar as janelas flutuantes.

## Etapa 6 — Janelas e overlays alinhados à nova composição

**Mudança que será vista:** drawer, inspeção de cartas, coleções e log abrem sobre a nova tela em posições coerentes, sem deslocar campo ou mão.

- Preservar a aparência e os comportamentos atuais dos overlays; reposicionar e ajustar seus limites ao canvas real.
- Manter drawer pelo canto esquerdo/atalho I, restauração de foco e pause acima de cartas/efeitos.
- Confirmar prioridade de Esc: cancelar uma seleção antes de abrir pause, conforme o comportamento existente.
- Ajustar feedback de reconexão/erro para não cobrir permanentemente elementos de gameplay.
- Continuar usando o log atual. Esta etapa não promete implementar o journal estruturado do backlog.

**Validação visual solicitada:** abrir cada overlay e confirmar que nenhum elemento da composição se move, fica inacessível ou aparece acima do pause indevidamente.

**Testes:** `pause_layers`, `combat_attributes_drawer`, recuperação, reabertura de coleção, seleção de custos inline e cancelamento.

**Gate:** aprovar a integração dos overlays antes da revisão final do conjunto.

## Etapa 7 — Composição completa ajustada e certificada

**Mudança que será vista:** tela final coesa, com ajustes de espaçamento entre grupos aprovados, sem alterar o tema ou os componentes de carta.

- Inspecionar a composição inteira em 1920×1080 e 1280×720; conferir fallback 1280×800 e 2560×1080 sem deformação.
- Ajustar somente espaçamento/posição dos hosts conforme as avaliações das etapas anteriores.
- Exercitar textos longos, escala de texto, PT/EN, mãos de 0/1/2/5/10/12 cartas e múltiplos atores.
- Comparar cartas e tempos de animação com a baseline inicial; investigar diferenças antes de atualizar qualquer expectativa.
- Executar `editor_tool_scenes`, testes de input/apresentação e uma run com API real em store de QA: entrar, comprar, usar, pagar com carta, exilar, encerrar turno, concluir e retomar.
- Verificar replay, integridade dos snapshots retidos e ao menos outro setting fora do MVP.
- Remover somente a montagem antiga do MVP que ficou comprovadamente sem uso. Não deixar fallback/flags de compatibilidade, nem apagar componentes compartilhados utilizados.
- Atualizar registro de implementação e [TODO](../roadmap/TODO.md), com commits, capturas, testes e limitações reais.

**Validação visual solicitada:** aprovação final do conjunto e das resoluções apresentadas.

**Gate:** só declarar concluído após a aprovação final; divergências voltam ao bloco correspondente.

## Registro das aprovações

| Etapa | Implementação | Commit/evidência | Aprovação visual |
| --- | --- | --- | --- |
| 1 — Base e mão | Implementada | [Validação e capturas](../validation/MVP_COMBAT_REFERENCE_STAGE1.md) | Aprovada pelo usuário em 2026-11-05 |
| 2 — Cabeçalho | Implementada | [Validação e capturas](../validation/MVP_COMBAT_REFERENCE_STAGE2.md) | Aguardando aprovação do usuário |
| 3 — Pilhas | Pendente | — | Aguardando etapa anterior |
| 4 — Campo e atores | Pendente | — | Aguardando etapa anterior |
| 5 — Energia e End Turn | Pendente | — | Aguardando etapa anterior |
| 6 — Overlays | Pendente | — | Aguardando etapa anterior |
| 7 — Conjunto final | Pendente | — | Aguardando etapa anterior |

## Fora do escopo

Novo tema/skin, arte de cartas/personagens, redesign da mão, novas animações, mudanças nas durações, mecânicas de companion, conhecimento inimigo novo, log durável completo, alterações da engine e refatoração global das demais telas. A referência organiza a cena; o visual continua sendo o do projeto atual.
