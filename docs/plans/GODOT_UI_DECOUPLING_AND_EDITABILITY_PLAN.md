# Plano — Desacoplamento e editabilidade da interface Godot

## Objetivo e escopo

Permitir que layout, composição e estilo sejam modificados no editor sem refazer lógica de sessão ou integração. Preservar o visual, as posições e proporções, comportamento responsivo, input, foco, acessibilidade, áudio, animações e suas durações, comunicação REST e comportamento observável atual. A engine continua autoritativa sobre regras, transações, determinismo e dados; a Godot apresenta estado e envia intenções.

Escopo: `examples/godot-engine-showcase`. O trabalho é uma refatoração incremental, não um redesign nem uma nova engine de UI. Instanciar subcenas em coleções variáveis, calcular disposição responsiva e animar propriedades são comportamentos legítimos; o problema é a propriedade ter várias autoridades ou depender de detalhes internos difíceis de editar.

## Ponto de partida

Os planos anteriores de centralização já entregaram cenas reais, registry, shell, componentes, presenters, `VisualContext` e testes. Reutilizar as cenas atuais, sem criar uma segunda carta ou outro sistema de telas. Nesta análise, `combat_view.gd` e `map_view.gd` ainda escrevem dimensões/posições e estilos; `card_view.gd` aplica métricas e overrides; `combat_screen.gd`, `card_hand_view.gd`, `main.gd` e `game_session.gd` concentram coordenação. As telas também dependem de singletons, do router e de nomes/árvore de nós. O gateway REST já é isolado e deve ser preservado.

O checkout principal possui alterações preexistentes não commitadas. Antes de implementar, inventariar por arquivo e conferir o conteúdo realmente executado; preservar índice, arquivos novos e alterações do usuário. Não copiar versões antigas da branch de documentação sobre o checkout principal. Usar checkout isolado a partir da revisão correta quando apropriado; trazer mudanças locais necessárias de modo explícito, sem incluir mudanças alheias em commits. A baseline deve registrar também o que não está commitado, sem publicá-lo automaticamente.

## Princípios de arquitetura

1. **Uma autoridade por propriedade:** cenas e recursos controlam composição, cores, fontes, tamanhos-base, margens e hierarquia. Recursos exportados configuram métricas responsivas e animações. Um controlador de layout pode calcular posições derivadas a partir da área disponível; animações podem escrever propriedades durante sua execução e devem restaurar o estado correto. A tabela de propriedade da etapa 1 documenta todas as exceções.
2. **Visão passiva e intenção semântica:** cenas renderizam modelos e emitem sinais tipados. Não escolhem resultados de jogo, resolvem custos, consultam HTTP ou navegam por uma árvore de outra tela. Interação local, foco e cálculo geométrico são responsabilidades legítimas da visão.
3. **Coordenador por tela:** traduz intenções em chamadas aos serviços de aplicação, acompanha estado assíncrono e entrega modelos; não depende de subnós internos. Router central navega por uma interface reduzida.
4. **Fronteiras tipadas:** manter `Dictionary`/JSON dentro do gateway e adaptadores de entrada; converter nas fronteiras para modelos tipados de apresentação. Preservar dados desconhecidos e extensões por setting quando necessários. Nenhuma conversão altera o estado autoritativo.
5. **Injeção explícita:** composição cria os serviços e passa interfaces/adaptadores estreitos. Autoload pode permanecer como ponto de composição durante a migração; componentes visuais não devem assumir sua existência. Não criar serviços globais duplicados.
6. **Uma implementação canônica:** laboratório, combate e inspeções compartilham as mesmas cenas, layout e executor de movimento; previews usam dados fictícios sem IO ou efeitos persistentes.
7. **Sem redesenho silencioso:** cada extração compara geometria, estados e animações antes e depois. Não normalizar valores, mudar a ordem das ações, nem inventar alternativas em caso de dados ausentes.

## Estratégia de validação

- Capturar a revisão exata, alterações locais relevantes, resolução, DPI, tema, locale, escala de texto e preferências; usar fixtures determinísticos em uma cópia isolada com dados de usuário separados, como `tools/test-godot-ui.ps1` já faz.
- Baseline de menu, configurações, mapa, atividade, diálogo, loadout, combate e replay: 1280×720, 1920×1080 e 2560×1080; comparar também escalas 1,0 e 1,6, quando suportadas. Registrar retângulos e capturas das cenas, com tolerâncias explícitas para rasterização/subpixel; diferenças fora da tolerância exigem inspeção, não atualização automática da baseline.
- Registrar poses em tempos representativos e duração total de compra, uso, descarte, exílio, retorno, hover, fan e transição de mapa, incluindo redução de movimento e cancelamento; manter executor e perfil atuais.
- Validar teclado/mouse/controle, foco, pausa e modais, persistência local e acessibilidade; verificar mãos de 0, 1, 2, 5 e muitas cartas e diferentes números de atores.
- Reutilizar testes existentes de cena, catálogo, mão, input, animação, mapa, sessão, loadout, pause e replay. Novos testes devem cobrir falhas reais de contrato/ciclo de vida e alterações feitas no editor, evitando testes que apenas repitam o código.
- Fazer integração com API real em ambiente isolado: iniciar/retomar run, percorrer mapa, entrar em combate, jogar carta, encerrar turno, retornar à atividade/mapa e conferir replay. Comparar requests, respostas, ordem dos comandos, sequence/step e hashes para mesma seed/conteúdo/comandos; usar runs isoladas e não alterar saves reais.
- Executar teste que abre cada cena registrada sozinha no editor e em runtime, detecta nós ausentes/sinais duplicados e confirma que não há HTTP, escrita de saves ou preferências no preview. Testar remontagem, navegação durante requisição, cancelamento e desconexão.
- Testar editabilidade de forma observável: modificar em cópia descartável cor, margem, posição-base, fonte e tamanho de um componente no editor/asset; instanciar normalmente e confirmar que permanece, exceto propriedades declaradas derivadas na tabela de autoridade.

## Etapas sequenciais

### 0. Inventário e baseline

Listar cenas registradas, subcenas, nós dinâmicos, autoloads, dependências de router, overrides e referências a caminhos internos; levantar testes existentes e estado Git. Registrar cobertura de cada cena e lacunas, sem presumir que arquivos grandes sejam defeitos por si só. Gerar baseline funcional e visual pela estratégia acima.

**Aceite:** reprodução confiável, fixtures e ambiente isolados, diferenças preexistentes registradas e nenhum arquivo do usuário alterado. Commit apenas de documentação/fixtures comprovadamente pertencentes à etapa.

### 1. Contrato de autoridade visual

Para menu, carta, mão, combate, mapa e overlays, mapear propriedade → proprietário (`.tscn`, `Theme`/`Resource`, layout responsivo, animação ou conteúdo de apresentação) → momento de escrita → justificativa → restauração após animação. Identificar `custom_minimum_size`, âncoras, `offset`, posições, cores, fontes, `StyleBox` e `move_child`. Definir valores básicos em cenas/recursos; constantes responsive existentes migram para perfis exportados com os mesmos defaults. Documentar quais controles são naturalmente criados por coleção.

**Aceite:** matriz revisável, sem dupla autoridade, com testes de edição e sem mudança visual. Compatibilidade das propriedades atuais é requisito, não pretexto para fixar todas as posições em pixels.

### 2. Composição e contexto de tela

Definir contratos estreitos para navegação, sessão de leitura/comandos, animação, tradução e feedback. Criar adaptadores em torno dos serviços atuais e composition root; evitar acessar `router.shell`, membros privados ou autoload diretamente de componentes. Migrar uma tela de baixo risco como piloto mantendo a API antiga no adaptador durante a transição e remover a ponte quando os consumidores forem migrados. `ScreenView` continua contrato comum, sem classe-base que absorva o projeto inteiro.

**Aceite:** cena piloto abre em isolamento com serviços falsos e em runtime com serviços reais; navegação, foco e cancelamento preservados; nenhum segundo serviço de sessão.

### 3. Modelos de apresentação e validação de fronteiras

Definir modelos tipados pequenos para identidade/versão da sessão, ator, carta, ação permitida, zona e estado local de interação; escolher classes `RefCounted` ou recursos adequados por caso. Adaptadores convertem DTOs em modelos e verificam campos obrigatórios, tipos, UUIDs e contexto de revisão. Dados opcionais e conteúdo desconhecido continuam disponíveis de forma explícita. Não inserir defaults que possam produzir ação válida inexistente; falhas ficam visíveis e preservam o último estado válido onde o fluxo atual exige.

**Aceite:** testes com campos ausentes, nulos, tipos inesperados e resposta atrasada; estado apresentado não mistura run/sequence/revisão. Comparar payloads e respostas anteriores/depois.

### 4. Componentes visuais e editabilidade

Auditar carta, ator, recursos, status, atributos, botões, modais e pile view. Extrair cenas apenas onde há componente real reutilizável; manter cena canônica. Trocar caminhos profundos entre componentes por sinais e API pública; usar `%UniqueName` ou `@export` tipado onde fizer sentido, validando bindings ao iniciar. Migrar cores/fontes/margens estáticas para Theme/Resource sem suprimir acessibilidade ou estados de foco. O preview `@tool` usa fixtures isolados, sem rede.

**Aceite:** testes de edição das propriedades previstas, catálogo e laboratório; mesmas fontes, dimensões e cores; sem novas variantes legadas de carta.

### 5. Layout responsivo do mapa e combate

Extrair geometria derivada para componentes de layout com contratos documentados. Manter âncoras e tamanhos-base no editor e parametrizar breakpoints, limites e gaps nos perfis existentes ou recursos dedicados; preservar valores e fórmulas atuais. `map_view` usa API pública de geometria em vez de `route._geometry`; inspeção flutuante continua sem redimensionar mapa. `combat_view` delega cálculo sem mudar safe areas, camadas, ordem de desenho ou distribuição da cena.

**Aceite:** matrizes de resolução/escala, centro e transição do mapa, overlays e hitboxes equivalentes; trocar um parâmetro exportado na cópia de teste tem efeito previsível sem alterar outro componente.

### 6. Mão: geometria, input e apresentação

Dividir `card_hand_view` em colaboradores pequenos: cálculo puro de fan/retarget, coordenação de mouse/drag/teclado e renderização/reconciliação das cenas de cartas. Reutilizar `hand_arrangement_store`, perfis e executor existentes; manter identidade das instâncias e slots visuais locais, sem mudar o baralho da engine. Um único caminho para combate e laboratório. Manter hit-testing conforme transformação, z-order, hover, seleção, reorder, retorno e cancelamento.

**Aceite:** mãos 0/1/2/5/cheia e overflow, arraste/seleção, reorganização, reversão e layout após resize; poses e durações comparáveis; laboratório e combate idênticos com mesmos dados e perfil.

### 7. Orquestração de combate e playback

Dividir o adaptador de combate em coordenadores com responsabilidade única: interação/comandos, consulta/inspeção, estado de apresentação e adaptador de animação. `CombatView` recebe modelos e emite intenções; não alcança estado global nem manipula serviços diretamente. Introduzir contrato explícito de propriedade de executor e assinatura/desconexão de sinais; preservar geração, handles, watchdog, pausa, descarte e garantia de não esperar por animação inexistente. Sem substituição do protocolo de eventos ou das animações existentes.

**Aceite:** scripts de combate, playback, seleção, interrupção, travamento, latência e lifecycle passam; desconectar/remontar ou mudar de tela durante resposta não deixa callbacks ativos nem cartas bloqueadas; API real preserva comportamento.

### 8. Sessão e comunicação

Separar progressivamente, por composição, armazenamento de snapshot, sincronização/leitura, despacho de comando com idempotência/versão, consultas de inspeção e replay. `GameSession` permanece fachada durante migração e depois expõe apenas o contrato necessário; `EngineGateway`/transporte continuam sendo autoridade de HTTP, serialização e erros. Preservar retry, geração, validação de revisão e cópias defensivas; não introduzir polling ou mais requests no caminho crítico. Medir antes/depois a latência de comando até feedback e requests por ação.

**Aceite:** testes de transporte, sessão, versão, reconexão, falha e resposta fora de ordem; mesma sequência de requests e resultados, com medição sem regressão relevante.

### 9. Router, atividades e demais telas

Extrair do `main` serviços de navegação, composição de shell/modal e ciclo de vida; telas chamam navegação por interface, não métodos privados de outra tela. Migrar atividade, diálogo, menu, configurações, loadout, timeline, histórico e replay em lotes pequenos. Substituir dispatch baseado em strings/arrays por sinais ou objetos de intenção tipados nos pontos críticos, preservando comandos e payloads da engine. Não reimplementar regras em componentes de UI.

**Aceite:** cada rota do registry abre isoladamente; entrar/sair de atividade, voltar ao mapa, pause/configurações e replay sem sinais duplicados, navegação incorreta ou perda de estado local.

### 10. Temas, acessibilidade e ferramentas de autoria

Consolidar valores visuais estáticos em `Theme` e perfis existentes, mantendo overrides dinâmicos que comunicam estado ou acessibilidade. Evitar que `view_style_factory` crie componentes de produção paralelos quando uma cena canônica já existe; funções de construção usadas em laboratórios ou conteúdo dinâmico devem ser classificadas antes de remover. Workbench exibe componentes reais e fixtures explícitos, incluindo estados vazio, erro e texto longo. Preview não modifica recursos compartilhados.

**Aceite:** troca de estilo em uma cópia muda o que a matriz autoriza sem sobrescrita inesperada; português/inglês, contraste, escala, movimento reduzido, foco e navegação acessível preservados.

### 11. Regressão de ponta a ponta e desempenho

Executar suite afetada e fluxo com API real definido acima. Comparar screenshots/retângulos/poses, requests, hashes e logs. Certificar pause no meio de animação, falha/retry de rede, retorno de diálogo, retomada de run por setting, muitos atores/cartas e replay. Verificar consumo de frame e quantidade de nós/atualizações; documentar diferenças e investigar regressões antes de alterar limites.

**Aceite:** equivalência demonstrada, sem duplicação de efeitos ou chamadas, nenhuma divergência de determinismo e sem dados persistentes reais alterados. Resultados devem identificar ambiente, versões, fixtures e limitações.

### 12. Limpeza e documentação

Eliminar pontes temporárias somente após todos os consumidores migrarem; remover cenas duplicadas apenas com prova de não uso e respeitando arquivos do usuário. Atualizar guia para edição na Godot, matriz de autoridade, contratos e como executar testes/previews. Listar exceções deliberadas de layout dinâmico. Relatório final separa o que foi verificado automaticamente, manualmente e o que permaneceu pendente.

**Aceite:** nenhuma autoridade visual duplicada conhecida, documentação reflete o estado real, checkout e Git conferidos e mudanças independentes preservadas.

## Entregas e commits

Uma etapa por commit, com commits menores por componente quando necessário; nenhuma etapa é marcada concluída só porque o código foi movido. Primeiro preservar e revisar alterações locais existentes, depois isolar as novas alterações e verificar o diff antes de cada commit. Se uma etapa detectar divergência, corrigir e revalidar antes da próxima. Não incluir mudanças de gameplay, conteúdos ou engine como parte da refatoração visual.

## Limites e decisões explícitas

- A interface não será totalmente sem código: coleções variáveis, hit-testing, animação e responsividade precisam de lógica. O critério é uma fronteira clara e autorável.
- Não forçar tipagem completa de todo JSON: preservar extensibilidade nos pontos que recebem conteúdo de settings e tratar validação nas fronteiras.
- Não exigir paridade de screenshots bit a bit entre renderizadores diferentes; fixar ambiente e usar tolerâncias justificadas junto a métricas geométricas e verificação humana.
- Não remover migração de saves/preferências ou mecanismos de compatibilidade sem comprovar que deixaram de ser necessários para dados existentes.
