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
