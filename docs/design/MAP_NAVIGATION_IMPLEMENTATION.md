# Navegação do mapa — implementação e certificação

## Etapa 1: baseline e contrato

Baseline estático do mundo 1: 50 nós, 25 camadas, seis alternativas na camada mais larga; perfil anterior aproximadamente 1710 × 2696 unidades. A coluna permanente retirava mais 300 unidades da viewport. Os testes anteriores exigiam scroll horizontal mesmo para um diamante simples.

MapNavigationModel separa seleção, hover, foco e posição autoritativa. Somente `travel` publicado torna um destino uma alternativa; arestas não concedem permissões. O modelo copia os dados e não possui acesso à sessão, transporte ou RNG.

Não foram abertas nem modificadas partidas pessoais. Capturas históricas não são apresentadas como playtest atual. Certificação visual e de integração será registrada nas etapas seguintes.

## Etapa 2: geometria compacta

Símbolos de 42, hitboxes de 72 × 64 e células de 112 × 104. Seis ramos ocupam 800 unidades incluindo margens, contra 1710 anteriormente. Camadas centralizadas; quatro passagens baricêntricas com desempate por ID. Rótulos ficam abaixo dos símbolos e as conexões saem após a área reservada ao texto. SCC, ciclos, desconexões e cache por topologia foram preservados. Testes iniciais: 35 checks aprovados nos quatro scripts de mapa, antes das duas novas asserções de seis ramos/sobreposição.

## Etapa 3: câmera

Modo decisão enquadra o conjunto publicado mais a posição atual, sem selecionar a primeira alternativa. Ato inteiro usa escala real e oculta rótulos ilegíveis; seus símbolos não são botões de viagem: clicar escolhe uma região e retorna ao detalhe. Estado local v2 guarda âncora, offset, escala, seleção e filtro. Estados antigos/âncoras ausentes voltam ao contexto atual, não ao pixel zero. Resize reaplica o contexto; refresh comum não enquadra novamente.

## Etapa 4: gestos e dispositivos

Coordenador intercepta drag iniciado também sobre nós. Movimento acima de sete unidades suprime ativação na soltura; botão central também funciona. Pause, perda de foco e subjanela modal cancelam o gesto. Vizinhos direcionais explícitos com desempate estável; foco fora da viewport é revelado sem alterar a escala. Setas/D-pad e Tab usam as ações UI já reservadas; confirmação utiliza `confirm_action`, existente e remapeável. Não foram criados atalhos paralelos. Hover tem estado próprio e não muda a seleção fixada.

## Etapa 5: Marfim e comparação

Catálogo de ícones existente, estados com símbolos além de cor, conexões realçadas na seleção/hover e filtro que destaca sem esconder nós. Faixa comparativa contém os mesmos IDs e somente emite seleção. Não interpreta descrições para inventar recompensas. Inspector é uma sobreposição lateral (inferior em área estreita), com uma única aba quando recolhido. O canvas mantém sua largura; câmera desconta a área encoberta. Ações continuam explícitas nos detalhes; foco de ação é recuperado quando os dados atualizam. Legenda e filtro são ferramentas secundárias.

## Etapa 6: densidade

Fixture reproduz a topologia e metadados de apresentação dos 50 nós reais do mundo 1, sem instanciar saves. Recompensas/upgrades usam símbolos menores, mantendo hitbox e ID; distinção pode ser desativada no perfil. Metadado opcional `reward` publicado aparece nos detalhes e tooltip comparativo; não é extraído da descrição. Sem metadado, nada é inventado.

Decisão: não agrupar cadeias. A largura caiu para 800 unidades e o modo ato resolve a visão global; colapsar etapas agora esconderia serviços e acrescentaria estado de expansão sem necessidade demonstrada. Todas as 25 camadas e suas transações continuam individualmente inspecionáveis, inclusive em settings desconhecidos/cíclicos.

## Etapa 7: integração

MapScreen revalida a referência opaca nas escolhas mais recentes antes de enviar; bloqueia estado não sincronizado. Controller, ingresso único de atividades, avanço forçado e barreira de retorno de diálogo permanecem canônicos. Estado de leitura continua segregado por setting/run/branch (limite de 16 contextos). Dispose cancela gesto e invalida respostas tardias. Testes cobrem payload/versão preservados, opção removida, busy, falha, troca de branch e fechamento durante requisição.

## Etapa 8: certificação

Concluída em cópia Godot com dados de usuário isolados e fábrica REST temporária. Nenhum save pessoal foi aberto, migrado ou sobrescrito.

- Core: **1874 testes aprovados**.
- REST: **PublishedAct_CanTraverseAllNineEncountersServicesAndReplay aprovado**, incluindo campanha completa, replay e retomada persistida (102 s nesta máquina; não é medição de uma ação).
- Matriz MapView: 4 resoluções × 2 idiomas × 2 escalas de fonte = **16 combinações**; contraste ampliado e movimento reduzido nos contextos de teste. Resoluções: 1280×720, 1920×1080, 2560×1080 e 2560×1440.
- Validados enquadramento das seis alternativas e posição atual, ausência de pan horizontal nesse caso, 20 refreshes idênticos, restauração no meio do ato, resize e visão geral sem microalvos.
- Eventos de mouse, teclado e D-pad injetados na viewport validam drag sobre nó, vizinhos explícitos e confirmação separada da seleção. Isso não equivale a playtest físico com controle.
- Grafos: 50 nós reais, linear de 100 nós, diamante, seis ramos, ciclo/self-loop, desconexão, tipo desconhecido e fallback horizontal com 20 ramos. Nenhum nó ou transação é agrupado/ocultado.
- Suítes adjacentes: run_graphics_updates, activity_reconciliation, dialogue_contracts, dialogue_coordination, dialogue_reading, activity_ingress e run_graphics_architecture aprovadas (119 checks).
- Oito scripts de mapa: **153 checks funcionais**; execução renderizada acrescenta oito verificações de gravação de captura. A aprovação é de comportamento automatizado, não de conforto humano.
- Zero intenções de comando e projeções de entrada inalteradas durante toda a matriz de exploração. O componente não tem transporte nem acesso ao RNG; não se trata de benchmark REST de 30 segundos.

Capturas reproduzíveis em [map-navigation](map-navigation/), usando MapView de produção e topologia real, mas snapshot/opções sintéticos de teste — não uma partida pessoal. Revisão visual resultou em símbolos nativos legíveis para os placeholders; artes não-placeholder do catálogo continuam tendo precedência. Visão geral informa explicitamente que clicar escolhe uma região, sem viajar. Retorno após progresso real enquadra a nova decisão, enquanto reabrir sem progresso preserva a leitura manual.

Reproduzir:

```powershell
./tools/test-godot-ui.ps1 -Tests @('map_navigation','map_camera','map_geometry','map_graph_view','map_selection','map_world_one','map_flow','map_certification')
./tools/test-godot-ui.ps1 -Tests @('map_geometry') -VisualTest map_certification -VisualOutputDirectory 'docs/design/map-navigation'
dotnet test tests/Core.Tests --no-restore --verbosity quiet
dotnet test tests/API.Tests --no-restore --filter 'FullyQualifiedName~AscendantWorldOneJourneyTests.PublishedAct_CanTraverseAllNineEncountersServicesAndReplay' --verbosity quiet
```

### Limites e aprovação humana

Pendente: avaliar conforto com mouse/controle físicos, localizar posição em até 2 s e comparar caminhos em até 5 s, especialmente em telas reais pequenas. Não houve sessão humana nem medição desses tempos. Capturas/casos de interface não substituem esse aceite. Não foi necessário alterar engine, geração, conteúdo, balanceamento, persistência, journal, RNG ou contratos REST.

## Uso (interface simplificada)

Esta revisão substitui as decisões anteriores de toolbar, comparação permanente, overview e restauração de câmera manual. As seções de etapas anteriores descrevem o histórico da implementação.

Clique num símbolo para abrir os detalhes flutuantes e confirme **Viajar** ali. A barra de ferramentas, filtro, faixa duplicada de destinos e atalhos externos foram removidos. Pausa continua disponível pelo controle configurado. Arraste com botão esquerdo ou central e use a roda para explorar; ao abrir/retornar ao mapa ou redimensionar a tela, o nó atual fica no centro. A aba lateral abre/recolhe os detalhes, inicialmente recolhidos.

Setas/D-pad percorrem nós, Tab sai da região do grafo para ações/detalhes, confirmar seleciona e a ação Viajar precisa ser confirmada separadamente. Cancelar no controle recolhe detalhes e devolve foco ao nó; o atalho configurado de pausa continua abrindo pausa. O primeiro uso direcional sem foco inicia na posição atual; mouse não recebe foco fabricado na abertura.

### Painel e enquadramento

O inspector é uma sobreposição: sua visibilidade não participa de tamanho do canvas, área útil, zoom ou offset. Margens simétricas de câmera permitem centralizar inclusive o primeiro e o último nó. O foco inicial do roteador usa o nó atual. Estado de leitura v3 mantém seleção/visibilidade, mas não restaura pan/zoom antigo ao retornar. Não há deslocamento lateral ao abrir/recolher detalhes. Capturas atuais em map-centered/.

### Conclusão ao retornar

Ao sair de uma atividade pelo botão MAP, ActivityController reconsulta as opções atuais e envia somente o RESOLVE_NODE publicado para o currentNodeId. O mapa abre após aceitação. Sem esse comando, a visita continua pendente; não se escolhe carta, compra ou diálogo automaticamente. Falha/estado não sincronizado não gera badge local de conclusão.

Quando uma ação de atividade leva de volta ao mapa (por exemplo, após escolher recompensa), o mesmo fechamento ocorre antes da navegação. Ao restaurar uma run em que a única opção restante é RESOLVE_NODE, MapScreen também executa esse fechamento canônico. Inspeção de loja/recompensa com outras opções ainda pendentes não dispara essa segunda automação.

A engine continua dona da conclusão, efeitos de saída e novo snapshot. Nenhuma definição JSON, regra de gameplay, RNG ou implementação de persistência foi alterada. Os comandos permanecem registrados e reproduzíveis no journal.

### Regressões desta revisão

- Matriz de 16 combinações de resolução/idioma/fonte: posição atual exatamente centralizada, primeiro/último nó centralizáveis, retorno/resize, ausência de toolbar e estabilidade absoluta de extensão e posição ao alternar o painel.
- activity_map_completion: opção exata, snapshot anterior imutável, escolha obrigatória preservada, outra identidade de nó ignorada, rejeição, indisponibilidade, ambiguidade, clique duplo, resposta tardia e fechamento após recompensa.
- activity_ingress: conclusão única publicada é automática; recompensa/loja pendente não é pulada pela simples entrada no mapa.
- A suíte padrão inclui activity_map_completion. Componentes de mapa e roteamento real continuam exercitados sem tocar saves pessoais.
- Validação final: 430 checks Godot aprovados; 20 testes Core de progressão/transições aprovados; campanha REST publicada com replay e retomada aprovada (92 s). Capturas renderizadas nas quatro resoluções foram revisadas separadamente; não equivalem a playtest humano.
- map_auto_completion cobre o workflow real do mapa, controlando apenas a saída para não enviar rede: opção única, escolhas pendentes, ticket obsoleto, barreira de diálogo, falta de sincronização e identidade diferente do nó atual.
- A rolagem permanece ativa com barras ocultas, usando o modo nativo [SCROLL_MODE_SHOW_NEVER](https://docs.godotengine.org/en/stable/classes/class_scrollcontainer.html).

## Correção de montagem inicial

O router prepara MapScreen/MapView antes de adicionar a tela à árvore. Uma consulta de foco introduzida no redesign pressupunha viewport disponível e interrompia a construção do inspector. A consulta agora é opcional fora da árvore, e input de componente desmontado é ignorado. MapSelection passou a projetar a tela antes de `add_child`, reproduzindo a ordem de produção e verificando que todos os detalhes foram construídos. Essa regressão acrescenta um check à suíte, totalizando 154 checks funcionais de mapa. Sem alteração de regras ou comandos.
