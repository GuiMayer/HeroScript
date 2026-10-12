# Combate Marfim: proporções da referência, ícones, leque e pilhas visuais

Data: 2026-10-12. Estado: plano para aprovação; nenhuma alteração de gameplay/interface foi implementada nesta entrega.

## 1. Objetivo e decisões

Usar a imagem enviada como referência de **composição**, não apenas de paleta: cenário e personagens dominam a metade superior; mão central em leque ocupa a parte inferior; controles ficam num dock discreto nas bordas. Substituir a aparência de formulário dos botões por um conjunto coerente de ícones e controles impressos.

Diretrizes confirmadas:

- Retirar Hand overview da interface de combate; não substituí-lo por outro botão equivalente.
- Adaptive fan deve inclinar a mão mesmo com duas cartas. Uma carta permanece central e reta.
- Draw, discard, exile e quaisquer outras zonas publicadas devem abrir um container visual de cartas, não uma lista de nomes.
- Reutilizar CardView, detalhes, estilos de carta, controllers, sessão e playback. Não criar uma segunda mão nem uma segunda implementação de regras.
- Manter recursos, tipos de cartas, pilhas, equipes e settings genéricos. Os nomes conhecidos são convenções visuais do setting, não semântica da engine.
- A referência não autoriza inventar previsão, ordem de ativação ou informações ocultas para preencher a composição.

Artes de cenário/personagens continuam placeholders nesta entrega. Corrigir escala/anchors agora; ilustrações finais são uma tarefa separada.

## 2. Diagnóstico do código atual

| Ponto | Evidência | Correção planejada |
| --- | --- | --- |
| Leque desaparece com poucas cartas | HandLayout multiplica rotação e arco por compression. Havendo espaço, compression é zero. | Separar curvatura estética de compressão por densidade. |
| Barra de ferramentas grande | CardHandView cria overview, inspect, paginação e contador; CombatView acrescenta botões de pilhas. | Separar mão e chrome do combate; dock de ícones próprio. |
| Proporções dependem do mínimo dos containers | CombatView calcula a área dos atores pelo mínimo total do HUD inferior; CombatLayoutProfile só parametriza alguns painéis. | Política de composição com faixas proporcionais e mínimos legíveis. |
| Botões genéricos largos | Header e TurnControls usam botões de texto convencionais. | Variantes visuais e iconografia centralizadas. |
| Pilhas viram texto | CombatView._inspect_pile concatena nomes e abre InspectionDialog. | CardCollectionView/CardZoneDialog passivos, com grade de CardView. |
| Inspeção em lote só cobre zonas jogáveis | O gateway chama /cards/evaluations, cujo serviço é InspectPlayableCards. | Integrar a avaliação individual existente para cartas visíveis nas outras zonas, sob demanda. |
| Ordenação visual ignora a política publicada | CombatPresenter.pile_cards sempre ordena por definitionId. | Preservar ordem somente quando orderVisible autorizar; senão usar ordenação neutra estável. |
| Referência de zona insuficiente para cenários genéricos | A seleção atual usa apenas zoneId. O read model também publica ownerId/scopeId. | Identificar a zona por endereço completo e contexto da run. |

Contrato existente relevante: CardZoneView publica contentsVisible, orderVisible, count, cards e presentation. A avaliação individual já retorna baseContainer, effectiveBase, upgrades e disponibilidade, inclusive verificando visibilidade. Não é necessário alterar as regras de pilhas ou a bucket pipeline para esta mudança.

## 3. Proporções-alvo

A referência é aproximadamente 16:9. As faixas abaixo são estimativas visuais iniciais, não medidas exatas ou limites de gameplay. Usar coordenadas normalizadas da área útil da tela; mão e cenário podem se sobrepor visualmente.

| Região | Alvo inicial | Tratamento |
| --- | --- | --- |
| Cabeçalho | 0–8% da altura | Round à esquerda, contexto/controle central quando publicado, pause à direita. |
| Cenário e personagens | 8–49% | Artes muito maiores, silhuetas dominantes e hitboxes independentes. |
| Vida e estados essenciais | 47–55% | Barras baixas sob os personagens; números e símbolos compactos. |
| Mão em leque | 54–90% | Centro inferior; cerca de 70–78% da largura para cinco cartas. |
| Dock inferior | 89–100% | Pilhas nas bordas, preview no centro e End turn à direita. |

Para cinco cartas, experimentar altura individual de 32–38% da tela e proporção próxima à referência, preservando anatomia e legibilidade. A forma final deve ser aprovada em captura antes de fixar os valores. Não modificar a fonte durante hover, play ou inspeção.

Em 2560×1080, preservar as proporções verticais e limitar a expansão horizontal da mão: ultrawide amplia cenário/espaço lateral, não estica cartas. Em 1280×720 ou fonte em 120%, compactar decoração/arte e controles secundários antes de sacrificar texto ou esconder informações essenciais. Conteúdo excepcional pode usar scroll explícito; não cortar opções.

O cenário preenche a tela, inclusive atrás da mão. As informações clicáveis dos atores ficam acima da faixa das cartas elevadas. Sidebar continua flutuante e, fechada, mostra apenas sua alça.

## 4. Ícones e botões

Criar um catálogo SVG próprio da UI, separado de artes de cartas/personagens. Traços consistentes, tinta/marfim/vermelhão, sem emojis ou glifos dependentes da fonte. Circuitos paralelos com curvas de 45° pertencem à moldura/chrome; não atravessam o conteúdo legível dos ícones.

| Controle/informação | Símbolo proposto | Apresentação |
| --- | --- | --- |
| Pause | Duas barras | Símbolo + rótulo curto, integrado ao canto superior. |
| Timeline | Nós conectados | Controle compacto com tooltip e atalho. |
| Draw | Cartas empilhadas + indicação de entrada | Ícone, nome curto e contador destacado. |
| Discard | Cartas empilhadas + indicação de saída | Mesma família, silhueta distinta. |
| Exile | Carta deslocada/isolada | Não usar uma lixeira que sugira destruição permanente. |
| Zona customizada | Pilha neutra | Ícone configurável em apresentação; fallback genérico. |
| End turn | Seta para a direita | Ação primária vermelhão, moldura angular e rótulo visível. |
| Inspect | Lupa | Contextual à carta em foco, sem grande botão textual. |
| Cancelar seleção | X | Só enquanto houver seleção, em slot estável. |
| Próximo frame | Avançar | Só no playback manual; não confundir com fim do turno. |
| Passar prioridade | Transferência/seta | Somente se a engine publicar essa ação. |
| Intenções | Ataque, guarda, status, recurso, desconhecido | Mapping de apresentação sobre fatos publicados; tooltip com detalhes. |

O preview central substitui a área atual de texto solto por um bloco compacto de ação/alvo/custo/resultados publicados. Não chamá-lo de “próxima ação” se não houver uma ação selecionada ou um frame real. Não exibir uma fila de turnos fictícia para imitar os marcadores da referência.

Cada controle mantém Button nativo, foco, nome acessível, tooltip localizado e atalho remapeável. Ícone é o visual, não a identidade do comando. Hover/foco/disabled/processing não mudam seu tamanho. A ação principal e as pilhas mantêm texto curto; controles secundários podem usar apenas símbolo, com alternativa de rótulos visíveis.

## 5. Adaptive fan independente da quantidade

Continuar usando HandLayout puro e a superfície estável de hit testing existente. A curvatura terá um mínimo próprio; a compressão adapta o espaçamento, sem ser a condição para existir leque.

- Zero cartas: estado vazio legível, sem seleção e sem texto vertical. A região reservada não causa saltos no dock.
- Uma carta: centralizada e reta.
- Duas cartas: rotação oposta, inicialmente ±4–6°, com separação/leve sobreposição proporcional.
- Três a cinco: arco progressivo, extremos inicialmente ±7–10°, centro mais alto.
- Muitas cartas: comprimir até uma faixa mínima selecionável; depois permitir scroll/paginação sem diminuir indefinidamente cartas/fontes.
- Hover/foco: elevar e endireitar apenas a carta ativa, mantendo slots e hitboxes estáveis. Sair restaura o leque.
- Depois de jogar: nenhuma carta selecionada automaticamente; as restantes se reposicionam, sem reconstrução ou alteração temporária de fonte.

Adicionar parâmetros em HandVisualStyle para ângulo mínimo do leque, curva por quantidade, arco, passo preferido proporcional à carta e limites de sobreposição. Manter o modo reading como alternativa acessível explícita; adaptive continua o padrão, inclusive com duas cartas. Movimento reduzido mantém a geometria bonita, mas elimina transições.

## 6. Browser visual das pilhas

Ao clicar na pilha, abrir um modal amplo com nome/contador, grade rolável de cartas e fechamento claro. CardView apresenta a mesma anatomia da mão; clicar uma carta abre o painel detalhado existente. Não joga, seleciona alvo, compra ou move cartas.

Estados obrigatórios:

1. **Visível:** uma carta por instância, com transformações/upgrades próprios; duplicatas continuam distinguíveis.
2. **Vazio:** mensagem e ilustração neutra, sem grade artificial.
3. **Conteúdo oculto:** contador/back placeholder e explicação, sem consultar instâncias proibidas. Não confundir com vazio.
4. **Ordem oculta:** catálogo ordenado de modo neutro; sem numeração que sugira próxima compra.
5. **Ordem visível:** preservar ordem publicada, com indicação explícita quando representar sequência.
6. **Dados incompletos/conexão indisponível:** renderizar informações já publicadas, identificar detalhes indisponíveis e oferecer retry; não inventar custo zero ou scaling.

Busca por nome/tag e ordenação local são úteis em pilhas grandes. Quando a ordem do jogo estiver visível, ordenar por nome deve ser uma visualização alternativa claramente rotulada, não sobrescrever silenciosamente a sequência. Foco/scroll permanecem por endereço de zona; ao fechar, voltam ao botão que a abriu.

Inspeção REST: usar GET /api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation para a carta visível selecionada; o endpoint em lote atual continua dedicado à mão. Não fazer uma request por carta ao abrir uma pilha grande. Cache por setting/run/branch/revisão/sequence/combate/step/instância/contexto; coalescência, concorrência limitada e descarte de respostas atrasadas. O grid não depende de uma avaliação completa para aparecer.

Se a avaliação atual não fornecer um fato de custo/efeito para carta fora da zona jogável, usar sua definição/instância publicada sem calcular upgrades ou buffs no cliente; mostrar indisponibilidade explícita. Uma extensão de leitura só será necessária se os testes demonstrarem falta de dados canônicos para a face. Não criar um endpoint que revele zonas ocultas nem liberar inspeção proibida pelo modo.

## 7. Arquitetura proposta

- **CombatLayoutProfile + cálculo puro de métricas:** faixas proporcionais, limites de largura, área segura, tamanhos mínimos e adaptação por resolução/fonte. CombatView aplica os rects; não determina regras.
- **CombatChromeStyle / catálogo de ícones:** contornos, stroke, cores, tamanhos, padding e variantes IconAction/PileControl/PrimaryTurnAction.
- **Header/Dock/Actor HUD:** componentes passivos que recebem modelos e emitem as intenções atuais. Não acessam gateway.
- **HandLayout/HandVisualStyle/CardHandView:** leque e interação compartilhados entre combate e laboratório; toolbar configurável por consumidor, sem depender de nomes de setting.
- **CardCollectionView:** grade de leitura reutilizável, independente da mão jogável.
- **CardZoneDialog + presenter de coleção:** endereço completo da zona, visibilidade, ordenação, instâncias e estado local.
- **Coordinator de inspeção de coleção:** adaptação de leituras existentes, cache/lifecycle; nunca constrói nodes ou executa comandos.

Remover overview da UI/atalho de combate e limpar traduções, foco e configuração de controles correspondentes. Valores antigos salvos devem ser ignorados sem resetar os demais remaps. Verificar consumidores do laboratório/testes antes de excluir o antigo HandOverviewDialog; só mantê-lo se houver um uso real, não como rota escondida duplicada.

## 8. Etapas de implementação, em ordem

### 1 — baseline e contratos

Capturar o combate atual com 0/1/2/5/10/20 cartas; marcar proporções da referência e preservar screenshots anteriores. Fixar invariantes de seleção, fonte, foco, scroll, requests e playback. Criar fixtures de zonas vazias/ocultas/customizadas e de ordem visível/oculta.

**Aceite:** critérios mensuráveis antes da mudança; nenhuma run pessoal usada em QA.

### 2 — política proporcional e protótipo de composição

Ampliar CombatLayoutProfile e criar cálculo de métricas. Montar os slots header/battlefield/actor HUD/hand/dock na composição existente; usar os mesmos componentes vivos. Primeiro aprovar a geometria com placeholders, antes dos detalhes ornamentais.

**Aceite:** captura aproxima a referência; cenário/atores dominam a metade superior; cinco cartas ocupam a faixa inferior sem cobrir vida/intenções ou controles.

### 3 — adaptive fan real

Separar fanStrength de compression, parametrizar ângulos/arco/espaçamento e atualizar hit testing/reserva de bounds. Aplicar ao laboratório e combate através do mesmo código.

**Aceite:** duas cartas inclinadas simetricamente em tela larga; uma reta; muitas selecionáveis; hover não provoca oscilação e jogar não seleciona outra.

### 4 — catálogo de ícones e chrome

Criar SVGs e variantes de controles. Aplicar ao header, pilhas, dock, intenção e alça. Consolidar foco/tooltip/acessibilidade/localização. Preservar estados e slots estáveis.

**Aceite:** nenhum controle parece um botão genérico largo; símbolos distinguíveis sem depender de cor; End turn continua explícito e sem ambiguidades com playback.

### 5 — retirar Hand overview e integrar o dock

Eliminar botão/atalho público e referências de foco da experiência de combate. Desacoplar chrome das pilhas da toolbar da mão, preservando a inspeção contextual e navegação de mãos grandes. Limpar consumidores obsoletos após substituir os testes.

**Aceite:** não há overview na tela ou configuração de combate; scroll/teclado/controle acessam todas as cartas; nenhuma seta de foco aponta para node removido.

### 6 — browser das pilhas e detalhes canônicos

Implementar CardCollectionView/CardZoneDialog, endereço completo e os seis estados. Integrar avaliação individual sob demanda e cache limitado. Ajustar presenters para não apresentar carta de coleção como jogável, nem custo desconhecido como zero.

**Aceite:** draw/discard/exile mostram faces de cartas; clicar abre detalhes, nunca joga. Pilha oculta não vaza conteúdo/ordem. Instâncias com upgrades distintos não se misturam.

### 7 — feedback e composição final

Integrar preview compacto, custos alternativos, resultados/intenções publicadas e controles contextuais ao novo dock. Ajustar molduras e circuitos sem poluir conteúdo. Testar sidebar/modal/pause sobre mão rotacionada e cenário ampliado.

**Aceite:** estados busy/reconexão/playback são claros; preview não volta à descrição da carta; modais bloqueiam input subjacente e pausa fica acima de tudo.

### 8 — certificação e documentação

Executar regressões offline e REST isoladas nos três settings; capturar EN/PT em 1280×720, 1920×1080 e 2560×1080, fonte 100/120%, alto contraste e movimento reduzido. Atualizar diretrizes/README, registrar medidas e fazer commit por etapa.

**Aceite:** referência e resultado comparáveis lado a lado; nenhuma regra/hash/custo é calculada pela UI; refresh idêntico não recria mão, não muda fonte e não dispara novas inspeções. Escopo de reads é limitado ao que o usuário abriu.

## 9. Matriz adicional de regressões

- Leque com 0/1/2/3/5/10/20 cartas, overflow, carta ativa no extremo, clique em região sobreposta, teclado/controle e drag existente.
- Mesma mão antes/depois de play, novas avaliações REST, redraw, resize e hotreload; preservar instâncias por cardInstanceId.
- Pilhas grandes, duplicatas, transformações diferentes, endereço de zona igual com owners/scopes distintos, modo sem inspeção e conteúdo desconhecido.
- Modal aberto durante refresh, alteração da zona, troca de setting/branch, fechamento com request pendente e retorno de foco.
- OrderVisible=false não revela próxima compra; ContentsVisible=false não é apresentado como Empty.
- Uma carta de pilha não publica SELECT TO PLAY, não altera seleção de combate e não emite comando.
- Remover overview não altera a persistência dos outros controles; configurações não retornam à aba General.
- Nada fica selecionado por padrão após uso; animação não precisa de botão para liberar no modo automático.

## 10. Referências técnicas usadas

Proporções e direção visual: imagem enviada pelo usuário nesta solicitação. Os percentuais são nossa interpretação inicial e devem ser aferidos em captura.

- [Godot Control](https://docs.godotengine.org/en/stable/classes/class_control.html): anchors, offsets, foco, mouse_filter e pivot. Manter wrappers Control na mão; containers não devem disputar seus transforms.
- [Godot GridContainer](https://docs.godotengine.org/en/stable/classes/class_gridcontainer.html): distribuição da coleção por colunas de Controls; colunas adaptadas à largura útil.
- [Xbox Accessibility Guidelines](https://learn.microsoft.com/en-us/xbox/accessibility/guidelines) e [contraste XAG 102](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/102): legibilidade, foco/estados e contraste dos controles. Ícones não eliminam rótulos/accessibility names ou navegação por teclado.

Não incluir nesta entrega geração de artes finais, mudança de balanceamento, nova fila de turnos, novo sistema de cartas ou redefinição das regras de visibilidade das pilhas.
