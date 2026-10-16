# Redesign da navegação do mapa

Data: 2026-10-16. Status: oito etapas implementadas; certificação automática e capturas em `docs/design/MAP_NAVIGATION_IMPLEMENTATION.md`. Aprovação humana de conforto permanece pendente. Regras de gameplay não foram alteradas.

## 1. Direção recomendada

O mapa deve responder imediatamente: **onde estou, quais são as próximas opções e por que escolher cada uma?** Hoje ele funciona mais como um diagrama de caixas que o jogador precisa deslocar e interpretar.

Proposta: **mapa vertical de símbolos compactos, enquadrado na decisão atual, com alternativas comparáveis e detalhes em painel flutuante**. Manter Marfim/tinta e os circuitos finos de 45° nas margens; conexões funcionais precisam ser simples.

Não alterar geração do mundo, encontros, recompensas, avanço forçado configurado, custos, determinismo ou imutabilidade para resolver navegação. Não transformar o mapa num editor de grafos com mais ferramentas.

## 2. Auditoria do estado atual

Inspeção dos scripts atuais. A imagem `docs/design/ivory-run/2026-10-12/map.png` foi usada apenas como referência visual histórica, não como captura da partida atual do usuário. Nenhuma partida pessoal foi aberta ou modificada.

| Problema | Evidência no código | Efeito |
| --- | --- | --- |
| Caixas largas | MapLayoutProfile: 230×64; passo lateral efetivo 282 | Poucas alternativas cabem lado a lado. |
| Camadas alinhadas à esquerda | MapLayout reinicia cada camada na mesma margem | Vazio em camadas estreitas e ramos desalinhados. |
| Ordenação por ID | Não considera relações com pais/filhos | Conexões longas e cruzamentos evitáveis. |
| Coluna permanente | MapView reserva inspector de largura mínima 300 | Reduz área navegável mesmo sem informação útil. |
| “Próximas escolhas” mostra só uma | _next_choices usa candidates[0] | Não compara nem enquadra todas as alternativas. |
| “Visão geral” não enquadra | Só muda nós para 168×64 e reduz gaps | Grafo pode continuar fora da viewport. |
| Restauração em pixels | Guarda scroll horizontal/vertical | Resize, fonte e topologia podem invalidar a posição. |
| Primeira abertura no meio da run | Seleção atual é restaurada, mas scroll inicial é zero | Pode mostrar o início do ato, não a decisão atual. |
| Pan disputa clique esquerdo | ScrollContainer.gui_input e Buttons dos nós | Risco de disputa de captura; reproduzir por eventos reais antes de afirmar bug. |
| Foco inferido | RouteView não define vizinhos explícitos | Navegação por teclado/controle pode ser imprevisível. |
| Inspector recriado | _inspect remove filhos quando sua chave muda | Pode perder foco; falta continuidade explícita. |
| Filtro pouco visível | Só desenha pequeno arco no marcador | Difícil encontrar um serviço no mapa longo. |

A separação Screen/View/layout puro/câmera/controller já é uma boa base. O mapa dedicado **já existe**: não será reconstruído do zero.

### Dimensão real do mundo 1

O JSON atual tem **50 nós**, incluindo variantes de rotas, serviços e recompensas. São 25 camadas, com até seis nós na mesma camada. Pelas constantes atuais, o grafo ocupa aproximadamente **1.710×2.696 unidades lógicas**, antes de inspector, cabeçalho e margens.

Há 16 nós Encounter e 15 CardSelection no grafo completo, não em uma tentativa. Tratar cada recompensa como uma grande caixa equivalente a uma parada também aumenta o esforço de leitura.

### Testes atuais

Reexecutados nesta auditoria: **31 checks aprovados**, em map_geometry, map_graph_view e map_selection. Eles protegem geometria, identidade, scroll/foco e separação entre inspeção e viagem.

Entretanto, parte da suíte exige largura 230 e scroll horizontal: isso protege a solução anterior, não qualidade de navegação. Faltam testes de enquadramento de todas as escolhas, resize, gestos sobre nós e navegação direcional explícita.

## 3. Pesquisa e aplicação ao projeto

As fontes sustentam princípios; as dimensões e interações propostas abaixo são decisões deste plano, não padrões universais.

| Referência | Evidência | Aplicação proposta |
| --- | --- | --- |
| Slay the Spire 2 | Mega Crit relata aumentar ícones e diferenciar tipos de sala após dificuldade de clique/identificação em telas pequenas | Símbolos distintos, hitboxes confortáveis e estados claros; não copiar a tela inteira. |
| Monster Train | A apresentação oficial relaciona caminhos a benefícios distintos, como upgrades e bônus | Comparar serviços e riscos conhecidos antes de viajar; não copiar número de ramos. |
| Say the Spire | O mod permite alternar escolhas, seguir conexões até bifurcações e retraçar a leitura | Navegação semântica de teclado/controle; não depender só da distância entre botões. |
| Xbox Accessibility Guidelines | Ordem consistente e foco perceptível | Foco visível, retorno previsível e operação completa sem mouse. |
| Godot | Documentação alerta que foco inferido pode falhar em interfaces complexas | Vizinhos explícitos e coordenação de foco por região. |

Fontes:

- [Mega Crit — Neowsletter, novembro de 2024](https://www.megacrit.com/news/2024-11-07-neowsletter-issue-4/). Referência histórica de design, não declaração sobre toda a versão atual de StS2.
- [Monster Train — apresentação oficial na Steam](https://store.steampowered.com/app/1102190/Monster_Train/).
- [Say the Spire — documentação do autor](https://bradjrenshaw.github.io/say-the-spire/mod/map.html). É um mod de acessibilidade, não a UI padrão.
- [XAG 112 — navegação](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/112) e [XAG 113 — foco](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/113).
- [Godot — navegação/foco](https://docs.godotengine.org/en/stable/tutorials/ui/gui_navigation.html) e [ScrollContainer](https://docs.godotengine.org/en/stable/classes/class_scrollcontainer.html).

## 4. Experiência desejada

### 4.1. Composição

- Cabeçalho compacto: ato/run, recursos relevantes, deck e pausa. Ferramentas de debug/replay não competem com viagem.
- Mapa ocupa a área principal; sem coluna vazia permanente.
- Inspector flutuante para seleção fixada. Recolhido deixa apenas uma pequena aba; abrir/fechar não muda a largura do grafo.
- Em telas menores, ficha inferior compacta; em ultrawide, lateral. O enquadramento respeita a área realmente livre.
- Mapa consultável durante atividades sem abandoná-las nem montar outro navegador.

### 4.2. Nós e conexões

- Símbolos de 40–48 unidades e hitboxes de 64–72 como ponto inicial de protótipo, sujeitos a testes. Não permitir sobreposição.
- Nomes curtos no atual, selecionado e próximas opções; descrições completas no inspector.
- Ícones de combate, loja, descanso, forja, diálogo, inscrição e recompensa; elite/chefe quando publicado. Tipo desconhecido tem fallback.
- Atual, visitado, disponível, apenas visível, selecionado, hover e foco são estados distintos.
- Usar forma/marca/texto além de cor. Percurso concluído discreto; alternativas atuais em destaque.
- Destacar conexões de entrada/saída no hover/foco. Continuidade topológica futura não significa permissão futura garantida.
- Centralizar camadas e ordenar por relações, com desempate estável por ID; linhas não atravessam ícones ou rótulos.
- Circuitos decorativos ficam nas margens; uma ligação funcional não vira um feixe de fios ambíguo.

### 4.3. Enquadramento e movimento

Dois modos:

1. **Decisão:** atual e todas as alternativas publicadas visíveis, com algumas camadas futuras quando couber. Padrão na primeira entrada.
2. **Ato inteiro:** enquadra o grafo completo. Reduz detalhe, não fonte indefinidamente. Se símbolos/hitboxes ficarem pequenos, serve para escolher uma região e voltar ao detalhe, não para viajar em mini-botões.

Roda desloca verticalmente. Arrastar fundo move a câmera; botão central e Espaço+arrastar são alternativas. Não tornar zoom um requisito para usar o mapa nem trocar o scroll comum por zoom sem indicação.

No mundo 1, o perfil compacto deve dispensar pan horizontal nas resoluções suportadas. Grafos arbitrariamente largos mantêm fallback de pan: não impor três ramos ou outra regra à engine.

Restaurar leitura por nó-âncora e offset, não apenas pixels. Não recentralizar a cada refresh nem seguir o ponteiro. Se a âncora desaparecer ou ficar inválida após resize, recuperar o contexto atual.

Ações essenciais: “Minha posição”, “Próximas opções” — o conjunto, não a primeira — e “Ato inteiro”. Legenda/filtros são secundários.

### 4.4. Comparar, selecionar e confirmar

- Faixa contextual compara as próximas opções: ícone, nome, risco e benefício conhecidos. É outra apresentação dos mesmos IDs, não outro fluxo de viagem.
- Hover mostra prévia transitória sem trocar seleção fixada ou mover câmera.
- Clique seleciona; “Viajar” confirma. Sem duplo clique obrigatório.
- Foco/hover em futuro visível não habilita viagem.
- Sem justificativa publicada, dizer “não é uma opção atual”, sem inventar requisito.
- Durante envio, bloquear duplicação; manter tratamento existente de timeout, resultado incerto e referência desatualizada.
- Preservar avanço forçado configurado quando não houver decisão. Nunca escolher um entre vários ramos automaticamente.
- Diálogo continua separado e respeita a barreira existente de retorno ao mapa.

### 4.5. Teclado, controle e acessibilidade

- Foco inicial no atual ou contexto de decisão ao usar teclado/controle; no mouse, não fabricar destaque em outro nó.
- Esquerda/direita alternam alternativas; cima/baixo exploram conexões/camadas; Tab troca regiões.
- Confirmar seleciona/abre detalhes; viagem exige a ação explicitamente focada. Um evento não executa os dois passos.
- Cancelar fecha detalhes e devolve foco. Modais bloqueiam input do mapa.
- Novas ações passam pelo remapeamento existente.
- Alto contraste, EN/PT, fonte 100–120%, movimento reduzido e rótulos acessíveis.
- Foco fora da viewport é revelado. Não depender exclusivamente de cor, hover, áudio ou animação.

## 5. Arquitetura

| Parte | Responsabilidade |
| --- | --- |
| ActivityPresenter.route / projeção | Modelo de leitura a partir do snapshot e escolhas atuais |
| MapLayout | Geometria pura: camadas, ordem, limites, retângulos e arestas |
| MapLayoutProfile | Medidas, densidade, espaçamentos e política de rótulos |
| MapNavigationModel, novo | Seleção, foco, âncora, modo, filtro e navegação entre conexões |
| MapCamera | Conversões mapa/tela, fit bounds, clamp, pan e área útil |
| MapNodeView, se necessário | Componente focável com estados/hitbox explícitos |
| MapView | Grafo, comparação e inspector; emite intenções, sem REST |
| MapScreen + controller existente | Resolve referência publicada e coordena transições |
| Engine | Visibilidade, elegibilidade, custos, consequências e progressão |

Geometria não muda por vida, ouro, busy, hover ou troca de seleção. Layout invalida só por topologia, perfil ou dimensões relevantes. Preservar nodes e sinais por identidade.

Guardar leitura por setting/run/branch/contexto e versão do layout. Fallback seguro se a âncora não existir. Estado visual não entra no journal/snapshot.

selectedNodeId não é currentNodeId. nextNodeIds não concede permissão. Nenhuma regra, recompensa, rota ou RNG é calculada pela Godot.

### Precisa mudar a engine?

**Não para os problemas principais.** Topologia, atual, resolvidos e opções já existem.

Pode ser útil enriquecer o read model com benefício/risco estruturados, motivo de indisponibilidade, etapa/grupo e política de revelação. Auditar antes de acrescentar campos. Se necessários: opcionais, retrocompatíveis e com fallback. Não extrair números/regras de frases em inglês nem revelar encontros/recompensas ocultos.

## 6. Implementação sequencial

### Etapa 1 — baseline real e contrato de navegação

1. Capturar mundo 1 no início, bifurcação de seis opções, retorno de combate/loja/diálogo e próximo do chefe.
2. Registrar viewport, foco, seleção, tamanho do grafo e esforço de rolagem.
3. Fixtures: linear, diamante, seis ramos, 50/100 nós, ciclo, desconectado, tipo desconhecido e opção removida.
4. Separar hover, seleção, foco, atual e disponibilidade no modelo.

**Aceite:** cenários reproduzíveis e zero comandos por inspeção. API QA isolada, sem alterar partidas pessoais.

### Etapa 2 — geometria compacta e continuidade

1. Introduzir símbolos e hitboxes independentes.
2. Centralizar camadas; ordenar por vizinhança com algoritmo estável e limitado.
3. Reduzir cruzamentos e separar rótulos de arestas.
4. Preservar suporte a ciclos e desconexões; não recalcular por mudanças de recursos.

**Aceite:** seis alternativas cabem horizontalmente em 1280×720 no perfil normal; sem alvos sobrepostos. Mesma topologia/perfil produz mesma disposição, independentemente da ordem dos arrays.

### Etapa 3 — câmera e restauração

1. Implementar fit de atual+escolhas e fit do ato.
2. Respeitar safe area, limites de legibilidade e política de nível de detalhe.
3. Guardar âncora/offset e restaurar após resize.
4. Priorizar exploração manual sobre recentralização automática.
5. Verificar conversão de coordenadas/hit testing se houver escala.

**Aceite:** continuar run no meio do ato mostra a decisão atual. Refresh não desloca câmera; resize mantém contexto.

### Etapa 4 — gestos e dispositivos

1. Coordenador de pressionar/arrastar/soltar/ativar.
2. Arrastar iniciado sobre fundo ou nó cancela o clique de soltura após ultrapassar limiar.
3. Cancelar gestos em pause, modal e perda de foco da janela.
4. Configurar vizinhos/foco por região e integrar remapeamento.

**Aceite:** mouse/teclado/controle completam a mesma escolha. Drag nunca viaja; foco sempre visível. Testar eventos reais, não somente pressed.emit().

### Etapa 5 — visual Marfim e comparação

1. Ícones e estados claros, seguindo catálogo/tema existentes.
2. Substituir coluna permanente por inspector flutuante responsivo.
3. Adicionar faixa comparativa ligada aos mesmos destinos.
4. Apresentar só benefícios/riscos/custos publicados.
5. Reduzir toolbar; melhorar filtro sem esconder rotas nem mudar permissões.

**Aceite:** reconhecer atual e próximas escolhas sem legenda; comparar três destinos sem buscar caixas em áreas diferentes da tela.

### Etapa 6 — densidade e etapas auxiliares

1. Diferenciar paradas principais de recompensas/serviços.
2. Avaliar agrupamento visual de segmentos lineares com expansão.
3. Nunca esconder decisão, atual, risco/custo relevante ou ação necessária.
4. Manter IDs e transações; fallback de grafo completo para ciclos/settings desconhecidos.
5. Permitir desligar agrupamento no perfil visual.

**Aceite:** ato legível sem tratar toda recompensa como nova bifurcação. Nenhum comando extra ou novo avanço automático.

Agrupamento não é obrigatório: se o desenho compacto resolver o problema, não acrescentar complexidade sem evidência.

### Etapa 7 — integração com o fluxo

1. Revisar mapa→atividade→mapa, diálogo, pause, continuar e troca de branch.
2. Reutilizar MapScreen/controller; faixa comparativa não executa comandos por conta própria.
3. Testar opção removida, versão alterada, timeout e resposta atrasada.
4. Preservar política de avanço forçado e retorno de diálogo.

**Aceite:** mesma escolha envia mesmo payload/versão. Recursos e consequências idênticos; hover/foco/pan não executam ações.

### Etapa 8 — certificação

1. Resoluções, idiomas, fontes, acessibilidade e dispositivos.
2. Atualizar testes que exigiam scroll horizontal como objetivo; manter fallback para grafos largos.
3. Campanha, replay e retomada em API QA isolada.
4. Sessões humanas: localizar atual, comparar rotas, achar descanso e retomar leitura.
5. Documentar resultados/capturas e fazer commit por etapa funcional na implementação.

**Aceite:** matriz abaixo atendida; testes automáticos e aprovação humana registrados separadamente. Não afirmar playtest sem realizá-lo.

## 7. Matriz de aceite

| Cenário | Resultado exigido |
| --- | --- |
| Primeira abertura / continuar no meio do ato | Atual e alternativas visíveis sem busca manual |
| Seis ramos do mundo 1 | Sem scroll horizontal em 1280×720 no perfil padrão; nomes completos nos detalhes |
| Próximas opções | Enquadra todas, não só a primeira |
| Ato inteiro | Enquadramento real ou seleção explícita de região; sem mini-hitboxes |
| Mouse | Clique inspeciona; Viajar confirma; drag/hover geram zero comandos |
| Teclado/controle | Ordem previsível, foco visível, operação sem mouse |
| Vinte refreshes idênticos | Zero substituições de nós/recálculos de geometria; câmera inalterada |
| Vida/ouro/busy mudam | Só regiões pertinentes atualizam; nós não se movem |
| Resize / pause / sidebar | Contexto e foco preservados; mapa não salta |
| EN/PT e fonte 100/120% | Sem texto vertical/cortado, alvos sobrepostos ou ações inacessíveis |
| 1280×720, 1920×1080, 2560×1080, 2560×1440 | Área útil e proporções corretas |
| Ciclos / desconexões / tipos desconhecidos | Fallback legível; sem inventar conexões/permissões |
| Opção removida ou versão alterada | Referência antiga bloqueada/revalidada; sem redirecionamento silencioso |
| Explorar o mapa por 30 s | Hash/snapshot/RNG inalterados; zero POST e sem leituras repetidas por hover |
| Mesmo seed/revisão/comandos | Mesmo resultado autoritativo e replay válido antes/depois do redesign |

Metas iniciais de avaliação humana, ainda não medidas: localizar posição em até 2 s; compreender duas alternativas em até 5 s; selecionar e confirmar sem arrastar. Ajustar após observar jogadores reais.

## 8. Marco e limites

**Primeiro marco de aprovação: etapas 1–5**, com o grafo real do mundo 1. Resolver leitura, câmera e input antes de agrupamentos, minimapa ou anotações.

Fora do escopo: geração de mapas, balanceamento, novos encontros/recompensas, solver de melhor rota, movimento livre, desenho livre, minimapa permanente por padrão e migração de saves.

O plano histórico GODOT_IVORY_RUN_AND_DIALOGUE_PLAN continua como referência. Este documento refina a navegação sobre sua implementação atual. docs/research/map-generation-systems.md trata geração/topologia, um problema diferente desta auditoria.
