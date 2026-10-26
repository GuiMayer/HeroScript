# Reorganização manual da mão por arraste

Data: 26/10/2026. Estado: plano, sem implementação nesta etapa.

## 1. Comportamento desejado

Permitir mover uma carta para outra posição da mão usando o mesmo gesto de arraste já existente. Enquanto ela passa pelos slots, as vizinhas abrem espaço suavemente. Ao soltar dentro da mão, a carta encaixa na nova posição e o leque continua centralizado.

O movimento deve ser de **inserção**, não uma troca entre duas cartas. Exemplo: `[A, B, C, D, E]`, mover B para depois de D resulta em `[A, C, D, B, E]`. As demais preservam sua ordem relativa.

Fora da mão, o gesto continua servindo para jogar uma carta conforme os candidatos publicados pela engine. Soltura inválida retorna à posição anterior. Escape ou botão direito cancelam a reorganização em prévia.

Não alterar o layout da tela, as proporções do leque ou as durações de uso, compra, descarte e queima. O encaixe e a movimentação das vizinhas usam a duração configurada de reorganização da mão e os componentes compartilhados pelo laboratório e combate.

## 2. Diagnóstico do código atual

| Ponto | Estado atual | Consequência |
|---|---|---|
| `card_hand_view.gd`, `sync_models` | Constrói `incoming` pelos modelos recebidos e atribui `order = incoming` | Uma troca manual de `order` seria desfeita pelo próximo refresh |
| `begin_presentation`, `prepare_zone_beat`, `destination_pose` | Coordenam reservas e posições com ordens derivadas dos fatos publicados | Reorganização local precisa participar da projeção visual sem reescrever esses fatos |
| `pointer_input` | Na soltura de um arraste emite `drop_requested` e tenta retornar a carta | Não distingue uma soltura de reorganização de uma tentativa de jogar |
| `_drag_visual` e `_return_ghost` | Já preservam ponto de pega, pose global e retorno animado da própria carta | São a base adequada; não criar um segundo proxy/executor |
| `hand_layout.gd` | Calcula posições, ângulos e regiões de hit a partir da quantidade e do espaço | Reutilizar a geometria; acrescentar resolução de posição de inserção |
| `hand_retarget_motion.gd` | Reorienta vizinhas a partir da pose atual | Reutilizar para abrir/fechar o espaço durante a prévia |
| `pick_global` | Resolve carta pelo contorno transformado e pela profundidade | Deve continuar servindo ao clique; não deve decidir inserção a partir das cartas que estão se movendo |
| `combat_view.gd` | Encaminha drop para combate e realça alvos publicados | Reorganização deve ser consumida pela mão antes desse encaminhamento e limpar o realce de combate |

O `context_key()` do presenter inclui sequência e passo. Esse identificador representa uma versão da projeção, não é uma boa chave para manter a organização da mão entre ações. A identidade estável da coleção precisa ser separada dessa versão.

## 3. Decisões de arquitetura

### 3.1. Ordem visual separada da ordem autoritativa

Nesta implementação, reorganizar é uma conveniência visual. Não altera uma zona de cartas, o deck, a prioridade de resolução, a seleção de alvos ou a ordem que uma regra da engine percorre.

Manter três listas separadas, sempre por `cardInstanceId`:

- **Ordem canônica:** recebida da engine, preservada integralmente.
- **Ordem visual confirmada:** permutação local das instâncias atualmente presentes.
- **Ordem visual de prévia:** candidata enquanto o arraste acontece; não substitui a confirmada antes da soltura.

Uma organização visual nunca pode tornar-se a entrada de um cálculo, simulação ou comando baseado em índice. Jogar continua identificando a instância e o candidato publicados, independentemente da posição na tela.

Se futuramente um setting usar a posição da carta na mão como regra de jogo, reorganizar essa posição exigirá uma operação autoritativa própria. Este recurso cosmético não implementará essa mudança de regra implicitamente. Nenhum novo campo obrigatório será exigido dos settings atuais.

### 3.2. Estado local pequeno e regras puras

Criar um colaborador de apresentação, por exemplo `hand_arrangement.gd`, com operações puras ou que retornem novas listas:

- reconciliar uma ordem visual com os IDs presentes;
- produzir uma inserção sem duplicar/remover instâncias;
- iniciar uma prévia a partir da ordem confirmada;
- confirmar ou cancelar a prévia;
- resolver o destino de inserção a partir da geometria estável dos slots.

Ele não acessa REST, recursos do personagem, RNG, descrições ou efeitos. O `CardHandView` continua responsável pelo gesto e pela renderização. O registro de trânsito e `card_motion_pose.gd` continuam responsáveis pela posse visual e pela pose.

Não substituir todos os usos de `order` indiscriminadamente: distinguir iteração visual, identidade das cartas e planejamento derivado dos fatos. Os métodos de apresentação devem receber/projetar a ordem adequada explicitamente.

### 3.3. Persistência local e identidade da coleção

Manter a organização em memória na sessão de UI, para sobreviver a refresh e remontagem da tela do mesmo combate. Usar uma chave estável que inclua conexão, setting, run, branch, combate, proprietário e identidade da coleção visual. Não incluir sequência, passo, quantidade ou índice do nó.

O estado do laboratório tem seu próprio escopo. Inspeções de pilhas, recompensas, outras runs, personagens e branches não compartilham a organização da mão.

Não salvar essa preferência em snapshots, journal ou conteúdo da engine. Persistência em disco entre reinícios fica fora da primeira versão. Limpar o estado ao encerrar a sessão/run e limitar o cache aos contextos ativos para não acumular entradas indefinidamente.

### 3.4. Reconciliação com mudanças reais

- Refresh com as mesmas instâncias: conservar a ordem visual confirmada.
- Uma carta sai: removê-la e preservar a ordem relativa das sobreviventes.
- Novas instâncias entram: acrescentá-las à direita, na ordem canônica relativa de entrada.
- Carta retida: conservar a posição visual, pois a instância continua presente.
- Instância que saiu e retorna em outro comando: tratar como chegada e inseri-la à direita; não manter posições de cartas ausentes indefinidamente.
- Mudança de branch/run/combate/proprietário: usar o escopo correspondente; não transportar uma prévia em andamento.
- Se uma atualização altera a coleção durante o gesto: cancelar a prévia, reconciliar os fatos novos e liberar o visual com segurança. Não confirmar um slot calculado sobre uma mão antiga.

Reservas de compra e `targetOrderAfter` permanecem fatos canônicos intactos. A apresentação projeta suas instâncias sobre a ordem visual reconciliada antes de calcular a pose de chegada. Quando não houver preferência local, preservar o comportamento canônico atual.

## 4. Gesto, slots e animação

### 4.1. Áreas de soltura

Resolver a intenção nesta ordem:

1. Ponteiro dentro da área útil da mão: reorganização local.
2. Ponteiro fora da mão: tentativa de jogo pelo fluxo atual de candidatos/alvos.
3. Nenhuma opção válida: retorno à posição confirmada de origem.

A área útil exclui toolbar, botões de pilha, barras de rolagem, tooltips, painel de preview e modais. Ela pode conter uma pequena margem configurável acima do leque, sem invadir botões ou tornar a área de combate inteira um alvo de reorganização.

Usar o ponteiro para decidir se está na área da mão e o centro da carta arrastada para decidir o índice horizontal. Isso mantém o ponto de pega e evita que clicar perto de uma borda desloque a posição pretendida.

### 4.2. Espaço reservado e estabilidade

Durante o arraste dentro da mão, manter uma reserva lógica para a carta: mesma quantidade, largura e centralização. As vizinhas passam às posições da lista de prévia; a carta arrastada continua flutuante e não recebe o tween do slot.

Calcular fronteiras de inserção a partir dos centros dos slots-alvo, antes da animação, em coordenadas locais da superfície. Não usar a posição momentânea de uma vizinha, porque ela se move justamente para abrir espaço.

Aplicar uma pequena histerese nas fronteiras, limitada em relação ao espaçamento entre slots. Só atualizar o layout quando o índice candidato mudar. Isso evita oscilações e reconstruções por evento de mouse.

Ao sair da área da mão, recolher a prévia para a ordem confirmada e retomar os realces de combate. Ao reentrar, reconstruir a prévia a partir da mesma ordem confirmada, sem acumular trocas intermediárias.

### 4.3. Soltura e cancelamento

- Soltura em nova posição: confirmar a lista visual, emitir um evento local de reorganização e usar `_return_ghost`/pose compartilhada para encaixar na nova pose final.
- Soltura na mesma posição: apenas encaixar; não emitir mudança de ordem.
- Escape, botão direito ou soltura inválida: descartar a prévia e retornar à posição confirmada anterior.
- Soltura na mão nunca emite `drop_requested` de gameplay, clique sintetizado ou ativação da carta.
- Reorganizar não seleciona uma carta automaticamente, não abre tooltip por conta própria e não dispara áudio de uso/compra. Se houver som, reutilizar um evento curto de manipulação já existente, uma única vez.
- Enquanto arrasta, suspender o hover de outras cartas para evitar que uma vizinha elevada dispute a prévia dos slots.

### 4.4. Duração e propriedade

Usar `hand_transition` para deslocar as vizinhas e encaixar a carta. A reorganização é um gesto de interface e não consome o ciclo de uso de 1 s nem o de queima de 0,5 s.

Aplicar a curva/retargeting existentes a partir da pose atual. Um único proprietário escreve a transformação de cada carta. Movimento reduzido e modo instantâneo aplicam a lista e a pose final sem timers vazios.

Não recriar `CardView`, reconfigurar o conteúdo das cartas ou recalcular efeitos ao passar por um slot. O feedback de reorganização não depende de nova chamada à API.

## 5. Etapas sequenciais

### Etapa 1 — Contrato e reconciliação da ordem visual

Implementar `hand_arrangement.gd` e testes puros de inserção, reconciliação e cancelamento. Definir a chave estável da coleção e a política de cartas novas/retidas/removidas.

**Aceite:** a lista continua sendo uma permutação válida dos IDs presentes; fatos/modelos de entrada permanecem intactos. Cartas da mesma definição são independentes por instância.

### Etapa 2 — Integrar ordem visual à mão e à sessão de UI

Separar a ordem recebida da ordem de exibição em `sync_models`, foco, leitura e geometria. Guardar/restaurar a organização em memória por escopo, incluindo a remontagem da mesma tela. Preservar comportamento canônico quando não houver organização local.

**Aceite:** um refresh não desfaz a organização; outra run/branch/personagem não a herda. Os IDs enviados ao jogar são os mesmos candidatos legais publicados.

### Etapa 3 — Resolver slots e produzir a prévia

Acrescentar cálculo de índice de inserção em `hand_layout.gd` ou auxiliar puro dedicado. Definir área útil, exclusões, espaço reservado e histerese. Integrar a prévia ao arraste sem alterar a ordem confirmada.

**Aceite:** arrastar lentamente sobre uma fronteira não faz vizinhas oscilarem; mão com 1/2 cartas continua centralizada; não há reflow de containers externos.

### Etapa 4 — Confirmar soltura e animar encaixe

Consumir reorganização antes de encaminhar o drop ao combate. Reutilizar `hand_retarget_motion.gd`, `card_motion_pose.gd` e o retorno da própria carta. Restaurar prévia e realces corretamente ao cancelar ou sair da mão.

**Aceite:** a carta entra no slot escolhido e desloca as intermediárias; nenhuma troca de identidade, carta duplicada, seleção automática ou chamada REST ocorre.

### Etapa 5 — Compatibilizar compra, uso e apresentação

Integrar a projeção visual com `begin_presentation`, `prepare_zone_beat`, `destination_pose` e settlement. Reservar slots de chegada de acordo com a organização local e reconciliar remoções/retenções. Bloquear/cancelar reorganização durante apresentação autoritativa, envio, modal e replay histórico.

**Aceite:** comprar ou usar uma carta não restaura a ordem antiga nem cria um encaixe corretivo no último frame. Rejeição mantém a organização confirmada anterior. Replay histórico começa pela ordem canônica, sem herdar preferências da mão ao vivo.

### Etapa 6 — Mãos grandes e navegação

Garantir que os índices sejam globais na coleção, mesmo com scroll/paginação. Acrescentar auto-scroll suave nas bordas apenas durante reorganização dentro da mão, sem mudar o comportamento de rolagem em repouso. Atualizar foco e navegação esquerda/direita pela ordem visual.

Preservar clique + alvo e navegação atual por teclado/controle. Reordenação por atalho/controle pode reutilizar o mesmo comando local de inserção, sem gerar um segundo algoritmo; caso adicionada, usar modificador explícito e respeitar remapeamento existente.

**Aceite:** mover a primeira carta para o fim de uma mão excedente funciona sem alterar capacidade, tamanho de página ou regras das zonas. O foco continua ligado à instância, não ao índice antigo.

### Etapa 7 — Laboratório compartilhado e regressões

Adicionar cenários ao laboratório existente, usando os mesmos componentes: inserir no início/meio/fim, atravessar vários slots, sair/reentrar, cancelar, receber atualização durante gesto e comprar após reorganizar. Não adicionar outro protótipo de mão.

**Aceite:** laboratório e combate produzem as mesmas ordens/poses para a mesma sequência de gestos e fatos; duração de encaixe segue o mesmo perfil capturado.

### Etapa 8 — Certificação com a API real e documentação

Executar o fluxo reorganizar → jogar → comprar/encerrar turno → voltar à tela, em tentativas de QA separadas dos saves do jogador. Registrar a ausência de tráfego de gameplay durante reorganização e verificar replay após comandos reais.

**Aceite:** mesmo comando aplicado à mesma run-base produz o mesmo resultado autoritativo independentemente da organização visual. Validar usando simulação/branches de QA autorizadas ou fixtures equivalentes; não comparar duas chamadas sequenciais contra um estado já modificado.

Documentar a política local e os limites. Fazer um commit por etapa na implementação.

## 6. Matriz mínima de regressão

| Grupo | Casos |
|---|---|
| Inserção | Origem antes/depois do destino, primeiro/último slot, mesma posição, definições repetidas com instâncias diferentes |
| Quantidade | 0, 1, 2, 5, 10 e mão com overflow |
| Arraste | Ponto de pega na borda, vizinhas sobrepostas, fronteira com histerese, sair/reentrar, Escape, botão direito |
| Combate | Soltar na mão, em alvo válido, em alvo inválido e sobre botão/painel que não é slot |
| Estado | Refresh sem mudança, carta removida, novas compras, retenção, carta que sai e retorna, troca de ator/branch/run |
| Continuidade | Remontar a tela do mesmo combate; apagar contexto ao encerrá-lo; laboratório isolado |
| Interrupção | Modal, pausa, navegação, apresentação e sincronização durante um gesto |
| Acessibilidade | Movimento reduzido, instantâneo, velocidade configurada, mouse e foco por teclado/controle |
| Tela | 1280×720, 1920×1080, 2560×1080, escala de UI, resize e scroll |
| Autoridade | Zero comandos REST de reorganização; snapshots/journal/RNG inalterados; candidato enviado por ID correto |

Critérios visuais adicionais: sem saltos entre mão e overlay; vizinhas animam a partir da pose atual; o leque mantém o mesmo centro e dimensões; a carta arrastada não é recortada durante espera/rejeição; tooltips e outlines não ficam presos após a soltura.

## 7. Primeiro marco

Concluir as etapas 1–4 no laboratório. Esse marco já permite pegar uma carta, abrir espaço entre vizinhas, soltar em outra posição e cancelar o gesto, preservando leque, layout e tempos atuais.

As etapas 5–8 fecham a integração com os fatos da engine e garantem que a organização sobreviva às ações reais sem ganhar autoridade sobre as regras do jogo.
