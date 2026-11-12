# Plano: melhorar a usabilidade do Pattern Workshop

Estado: aprovado; implementação em andamento. Data: 2026-11-12.

## Escopo e método

Revisão heurística da tela de ofertas, estado após aquisição, seleção de item e carta, comparação e confirmação do Pattern Workshop do MVP. Base: inspeção das cenas e do código atuais, capturas gráficas com dados simulados em 1280×720 e 160% de texto, e consulta às referências abaixo. Não houve teste com jogadores nem partida completa conectada; as conclusões são hipóteses de design, não resultados de pesquisa com usuários.

## Comparação com os princípios

1. **Decisão próxima da informação.** As ofertas mostram nome, efeito e custo simultaneamente, o que favorece comparação. Porém cada oferta tem “Selecionar”, enquanto “Adquirir” fica distante no rodapé, e o texto “Selecione um padrão para ver seu custo” contradiz o custo já visível. A confirmação exige lembrar qual oferta foi escolhida. Princípios: reconhecimento em vez de memorização e correspondência entre controle e consequência.
2. **Estado e próximo passo.** Após comprar, a tela fica majoritariamente vazia: comunica a conclusão, mas não destaca qual padrão foi adquirido nem o que aconteceu com o saldo. “Aplicar agora” e “Aplicar depois e continuar” existem, mas a relação entre compra, inventário e carta não está suficientemente visível. Princípios: visibilidade do estado e feedback da ação.
3. **Escolha de carta e compatibilidade.** A aplicação usa duas listas de texto e uma carta de prévia. Nomes iguais podem representar instâncias diferentes, e cartas indisponíveis aparecem desabilitadas, com razões dependentes de dica. Não se vê de imediato qual carta está ativa ou na reserva quando essa informação não está presente no rótulo. Princípios: reconhecimento, prevenção de erro e informação acessível sem depender de cor ou hover.
4. **Comparação e confirmação.** A comparação reutiliza duas cartas reais e consulta a prévia do motor, evitando inventar valores. Ainda é preciso verificar visualmente se ela evidencia o padrão e as mudanças relevantes, se cabe com texto ampliado e se erros de prévia explicam o que fazer. O layout é uma hipótese a validar com dados reais.
5. **Hierarquia e legibilidade.** A tela de ofertas tem área central vazia, enquanto o rodapé concentra três ações próximas; o foco visual não distingue suficientemente ação principal, alternativa e saída. Os tamanhos e contraste devem ser medidos na aplicação real, não inferidos a partir das capturas simuladas.
6. **Navegação e recuperação.** O mapa e o fluxo usam comandos publicados e não compram ao retornar. São necessários testes explícitos de foco ao abrir/fechar diálogos, retorno sem perda de seleção, teclado/controle, erro de rede, estado pendente e sessão retomada.

## Plano proposto

### Etapa 1 — referências visuais e critérios

- Capturar a tela real em estados inicial, padrão selecionado, saldo insuficiente, após compra, seleção de carta, comparação e falha; usar campanha isolada, sem interferir na campanha aberta do jogador.
- Registrar as dimensões e os estados existentes, incluindo leitura em português, inglês, 1280×720, 1920×1080 e texto ampliado.
- Definir critérios para hierarquia, foco, contraste e legibilidade antes de alterar a composição. Não transformar capturas simuladas em evidência de uma partida real.

### Etapa 2 — escolher e adquirir um padrão

- Reorganizar as três ofertas mantendo nome, efeito e custo juntos e com dimensões comparáveis. Destacar a seleção com contorno e texto, sem depender apenas de cor.
- Substituir a instrução contraditória por orientação sobre a decisão. Aproximar seleção e aquisição, preservando um único botão de confirmação de compra com padrão, custo e saldo conhecidos.
- Mostrar saldo insuficiente antes de tentar comprar quando saldo e preço forem fornecidos; quando não forem conhecidos, indicá-lo sem fabricar números. Conservar cancelamento e validação final no motor.
- Diferenciar com clareza ação principal, gerenciamento de padrões e saída. Preservar o comportamento opcional e pedir confirmação ao sair sem comprar.

### Etapa 3 — feedback após compra

- Apresentar o padrão efetivamente adquirido, seu efeito e o saldo atualizado quando publicados, com a mensagem explícita de que nenhuma carta foi alterada ainda.
- Destacar **Aplicar agora** e oferecer **Aplicar depois e continuar** como alternativa; manter acesso ao mapa sem resolver silenciosamente a visita.
- Testar atualização/reentrada e estados em que os dados ou a conexão ainda não estejam disponíveis, evitando afirmar que a compra falhou ou foi concluída antes da resposta autoritativa.

### Etapa 4 — item, carta e comparação

- Melhorar a organização das listas e da prévia com títulos e instruções que indiquem o passo atual. Diferenciar instâncias com mesmo nome usando apenas informações publicadas, como localização no deck ou reserva e alterações existentes; não inventar identificadores de jogo.
- Mostrar o motivo de incompatibilidade também por teclado/controle e texto visível quando o motor o fornecer. Se faltar apresentação ou motivo, explicar a indisponibilidade com linguagem clara.
- Na comparação antes/depois, identificar o padrão e destacar diferenças fornecidas pela prévia. Manter cartas integrais e oferecer confirmação e cancelamento claros; não calcular alterações localmente. Se a prévia estiver incompleta, impedir a aplicação e oferecer recuperação.
- Manter padrões já adquiridos acessíveis no loadout, sem duplicar regras ou apresentar comandos vencidos.

### Etapa 5 — acessibilidade, validação e commits

- Garantir ordem de foco, operação por mouse, teclado e controle, retorno de foco ao cancelar e nomes acessíveis; preservar suporte a texto ampliado e movimento reduzido. Medir contraste dos textos e estados; comunicar seleção e indisponibilidade por texto/forma além de cor.
- Verificar em 1280×720 e 1920×1080, português e inglês, 100% e 160% de texto; confirmar que ações não são cortadas, que a comparação é navegável e que existe rolagem apenas onde necessária.
- Testar saldo suficiente/insuficiente/desconhecido, compra confirmada/cancelada/rejeitada, itens ativos/reserva, cartas com nomes repetidos, incompatibilidade, prévia indisponível, reconexão e reentrada. Executar um fluxo conectado em ambiente isolado e testes existentes do MVP e outros settings.
- Utilizar nós e componentes declarados nas cenas Godot, sem criar nós pelo código. Se ícones forem necessários, usar apenas game-icons.net, preservar proporção 1:1 e registrar artistas em `CREDITS.md`; não usar emojis.
- Fazer commits por etapa lógica após aprovação, preservando alterações preexistentes fora do escopo.

## Fontes

- [Nielsen Norman Group: 10 heurísticas de usabilidade](https://www.nngroup.com/articles/ten-usability-heuristics/) — estado, prevenção de erros, consistência e reconhecimento.
- [Nielsen Norman Group: visibilidade do estado](https://www.nngroup.com/articles/visibility-system-status/) — retorno compreensível das ações e transições.
- [Microsoft Xbox Accessibility Guideline 103](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/103) — não depender somente de cor para transmitir estados.
- [Game Accessibility Guidelines: contraste](https://gameaccessibilityguidelines.com/provide-high-contrast-between-text-ui-and-background/) — contraste do texto e controles sobre o fundo.
- [Google Playables: boas práticas de design](https://developers.google.com/youtube/gaming/playables/certification/best_practices_design) — legibilidade e navegação por teclado. Seus números de tamanho de toque são direcionados ao contexto do Playables, não uma exigência direta deste jogo para desktop.

## Verificações realizadas

- Capturas gráficas com projeções simuladas: ofertas e seleção em 1280×720 e 1920×1080, estado após compra, lista de cartas e comparação, incluindo texto em 160%. A campanha aberta do jogador não foi modificada.
- Pela paleta compartilhada, o contraste calculado é 16,1:1 para texto principal sobre branco, 5,94:1 para texto secundário sobre branco e 5,59:1 para branco sobre a ação principal. São medições das cores da paleta, não uma auditoria de cada pixel/estado do jogo.
- Passaram os testes de seleção e preço, saldo insuficiente ou preço não publicado, confirmação e cancelamento, foco após cancelamento, estado adquirido, cópias de cartas, incompatibilidade acessível, comparação, navegação do mapa e interfaces relacionadas. Os testes de integração REST do motor dos três padrões também passaram na execução anterior; o motor não foi alterado nesta etapa.
- Limitação: não foi executada uma partida completa da interface conectada a um servidor isolado, nem um estudo com jogadores. Os dados gráficos usados aqui são simulados. A verificação `world_one` permanece com uma expectativa independente desatualizada de 30 cartas para um catálogo com 57.
