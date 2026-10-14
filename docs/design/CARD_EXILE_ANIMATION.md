# Queima pós-uso e ritmo das cartas

2026-10-14.

## Apresentação

O manifesto visual `examples/godot-engine-showcase/data/motion_manifest.json` associa o fluxo publicado `card.played.to-exhaust` à animação `burn`, direção `center`. A engine continua decidindo efeitos e destino exclusivamente pelos componentes e fluxos JSON; Godot não verifica nomes/tags de cartas nem atribui significado às pilhas.

O executor de transferências usa o mesmo `CardBurnMotion`/shader do laboratório, com captura GPU do componente completo, duração normal de **0,5 s** e sem voo/cauda de descarte adicional. O prefixo de uso e o voo antecipado são omitidos somente quando o recibo confirma o fluxo configurado. Queimas não são antecipadas a comandos ainda não aceitos. O fluxo de cartas etéreas não recebe esse tratamento automaticamente.

Tempos normais configurados:

- Uso com descarte: 0,5 s de ida + 0,5 s de descarte; ciclo de 1 s.
- Uso retido: 0,5 s de ida + 0,5 s de retorno; ciclo de 1 s.
- Compra, descarte direto, criação, destruição, transferência, entrada da carta e reação de impacto: 1 s.
- Compra/descarte em lote: cartas concorrentes, sem stagger acrescentando tempo ao ciclo.
- Queima pós-uso: 0,5 s, pelo centro.
- Hover e reorganização da mão mantêm seus tempos rápidos; são feedback de input, não passos cinematográficos.

Velocidade/perfil rápido do usuário continuam escalando esses valores. Movimento reduzido e perfil instantâneo concluem sem animação ou espera artificial. Pausa congela os nodes; skip/cancelamento encerram os handles filhos e liberam capturas/cartas emprestadas. O laboratório inicia o campo com o tempo de produção da animação escolhida (queima: 0,5 s; uso: 1 s), mas editar esse campo não modifica o jogo automaticamente.

## Paridade com o laboratório

`card_choreography.gd` centraliza o ponto de passagem relativo à mão e o orçamento de ciclo. As cenas reutilizam os mesmos executores, curvas, escalas, shader e perfil capturado. O combate não acrescenta a antiga preparação/lift. Pilhas fornecem seus pontos de origem/destino conforme a composição; paridade não significa coordenadas absolutas idênticas em cenários diferentes.

Durante comandos de cartas, reações de impacto/barras são cosméticas paralelas: os fatos publicados continuam processados na ordem canônica, mas esses Tweens não acrescentam um segundo por impacto antes do descarte/queima. Eles possuem limpeza no feedback e respeitam pause, cancelamento, fechamento e substituição. Turnos/ações não originados por carta e replay de efeitos conservam passos bloqueantes de leitura. Transferências reais de outras cartas continuam sendo animações próprias.

Escolha a variante, configure os segundos, reproduza e clique em **Aplicar animação ao combate**. O perfil visual local é salvo em `user://card_animation_profile.json`, sem modificar regras, runs ou `motion_manifest.json`. Vale para a próxima captura, inclusive em outra cena/processo; não muda um ciclo em andamento. Uso com descarte e uso retido compartilham a duração da família de uso (ida + saída/retorno). Compra em lote compartilha a duração da compra; a contagem não é alterada. Cenários compostos/instantâneos sem equivalente único de produção não habilitam o botão. Sem esse clique, o laboratório não salva o perfil.

`card_animation_parity.gd` compara uso, retenção e queima no laboratório e no combate, incluindo vários impactos, ponto de passagem, aplicação do perfil, políticas capturadas e limpeza. Rodada renderizada: 22 verificações aprovadas. Tempos (lab/combate): uso 1,003/1,057 s; retenção 1,004/1,029 s; queima 0,523/0,551 s. Diferenças de poucos frames incluem despacho/sincronização; uma API lenta ainda pode acrescentar espera externa, sem alterar a coreografia ou antecipar fatos não confirmados.

Certificação transversal desta revisão: 471 verificações aprovadas em 17 suites de UI (headless e renderizadas), incluindo laboratório, política, seleção, transferência, lifecycle, pause, apresentação histórica e direção do playback. O teste antigo de lifecycle foi atualizado para abrir o histórico no menu de pausa, seu proprietário atual; o teste temporal agora distingue um executor que reivindica um handle de fatos sem executor. Ambos passaram, sem reintroduzir espera artificial ou autoridade de diálogo no painel passivo.

## Conteúdo Ascendant / mundo 1

| Carta | Custo | Efeito base | Disponibilidade |
| --- | --- | --- | --- |
| Last Guard / Última Guarda | 1 energia | 14 Block | Uma no deck inicial, substituindo uma Guarda Cinética |
| Second Wind / Novo Fôlego | 2 energia | 10 Health + 8 Block | Recompensas |
| Solar Burst / Explosão Solar | 2 energia | 12 de dano + queimadura com potência 4 | Recompensas |

Todas usam o componente de disposição `card.played.to-exhaust` e efeitos/custos/targeting existentes. Solar Burst conserva o cálculo e os críticos por tiers do setting; a UI não calcula seu dano. O deck inicial continua com dez cartas e o catálogo do mundo 1 passa de 30 para 33. Nomes EN/PT e artes do catálogo Marfim foram vinculados aos slots existentes.

O exílio é o fluxo existente do setting, não remoção permanente da carta da run. Runs salvas continuam vinculadas à revisão anterior de conteúdo. Atualize/recarregue a configuração da API e inicie uma nova run Ascendant para receber o deck novo.

## Validação

`AscendantWorldOneContentTests`: componentes, compilação/grafo, deck e disposição (4 testes). `ExileCardPresentationFactsTests`: run real via REST, uso da carta inicial, recibo com fluxo de exílio e remoção da mão. `AscendantWorldOneMechanicsTests` e `AscendantWorldOneJourneyTests`: execução determinística dos efeitos e jornada completa dos nove encontros até o chefe/replay (4 testes de API no conjunto, aprovados).

`animation_live.gd` exercita a apresentação de combate com gateway isolado: início sem voo duplicado, shader central, duração de aproximadamente 0,5 s e limpeza/desbloqueio. Certificação renderizada: 33 verificações aprovadas; última duração medida 0,532 s. Os testes de política, transferências, limites, seleção e laboratório cobrem os demais caminhos, incluindo instantâneo e ausência de animação.
