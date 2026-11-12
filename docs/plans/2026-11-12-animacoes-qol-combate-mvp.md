# Plano — animações e usabilidade do combate MVP

Data: 2026-11-12. Estado: aprovado; implementação em andamento. Referência examinada por leitura do código do Slay the Robot no commit `4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d`; não foi executada uma comparação visual interativa.

## O que a referência faz e o que já temos

- [Mão da referência](https://github.com/DesirePathGames/Slay-The-Robot/blob/4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d/scripts/ui/Hand.gd): anima o rearranjo das cartas, levanta a carta sob o ponteiro e solicita alvo quando necessário. O MVP já tem leque, elevação, arraste, escolha de alvo e movimentos de cartas; a proposta é aperfeiçoá-los, não reimplementá-los.
- [Cartas da referência](https://github.com/DesirePathGames/Slay-The-Robot/blob/4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d/scripts/ui/card/Card.gd): indicam condições para uso, mostram explicações de palavras-chave após uma pausa e calculam prévias a partir das ações. O MVP já possui seleção, inspeção e dados de apresentação; deve-se conferir a disponibilidade de dados autoritativos antes de mostrar novas prévias numéricas.
- [Feedback dos combatentes](https://github.com/DesirePathGames/Slay-The-Robot/blob/4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d/scripts/combatants/BaseCombatant.gd) e [barra de vida em camadas](https://github.com/DesirePathGames/Slay-The-Robot/blob/4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d/scripts/combatants/LayeredHealthBar.gd): apresentam dano/bloqueio e a variação de vida. O MVP já possui feedback, reações de impacto e transições de recursos, a serem avaliados em jogo.
- [Inimigos](https://github.com/DesirePathGames/Slay-The-Robot/blob/4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d/scripts/combatants/Enemy.gd): calculam a prévia de intenção usando a lógica das ações. O MVP já exibe intenções e dicas, mas é preciso auditar quais valores e explicações chegam do servidor.

## Etapas propostas

1. **Auditoria e cenas de teste.** Montar cenários reproduzíveis para dano, defesa, cura, estados, múltiplos alvos e mudanças de pilha; registrar capturas ou gravações do MVP. Catalogar o que já funciona, as durações e os dados publicados, incluindo estados indisponíveis. Não alterar regras ou simular números no cliente.
2. **Leitura e uso das cartas.** Ajustar a transição de elevação e redistribuição existente, sem aumentar a mão ou encobrir custo e texto. Distinguir cartas jogáveis e indisponíveis por contorno e explicação acessível, usando ações legais publicadas. Melhorar dicas de termos e inspeção por mouse, teclado e controle, evitando informações que não estejam disponíveis.
3. **Escolha de alvo e prévias.** Evidenciar alvos válidos, alvo em foco e cancelamento de forma consistente em mouse, teclado e controle; atualizar o feedback ao mudar o alvo. Mostrar prévias quantitativas de efeitos e intenções somente quando fornecidas por dados autoritativos correspondentes; quando não houver dados, apresentar apenas o que for conhecido, sem inferir dano. Consultar eventual extensão de API separadamente antes de planejá-la como requisito.
4. **Resposta às ações.** Refinar os movimentos já existentes de compra, jogo, descarte e exílio para esclarecer origem e destino. Melhorar a apresentação de dano, bloqueio, cura e alterações de status sobre o combatente certo, com texto/ícone legível e diferenciação além da cor; avaliar uma camada residual curta na vida sem atrasar o valor real. Evitar repetição de animações já existentes e respeitar a ordem dos eventos publicados.
5. **Ritmo e acessibilidade.** Sincronizar efeitos sem bloquear decisões desnecessariamente; preservar pausa, cancelamento e estado após atualização. Respeitar movimento reduzido, escala de texto, foco e leitura em português, e usar somente ícones autorizados da coleção game-icons.net com proporção 1:1 e crédito aos artistas efetivamente usados.
6. **Validação e commits.** Testar ações legais, troca de alvo, múltiplos efeitos, mão vazia, animação interrompida, pausa, movimento reduzido e resoluções suportadas; revisar o resultado visual em jogo. Fazer commits por etapa lógica após aprovação.

## Critérios de aceite

- A interface nunca inventa valores nem revela informação não publicada; valores de vida e escudo permanecem corretos.
- O jogador identifica carta, alvo e resultado sem depender apenas de cor, e consegue jogar e cancelar com teclado.
- Animações respeitam movimento reduzido, não deslocam a interface quando a mão fica vazia e não travam o combate.
- O posicionamento aprovado do MVP e as proporções dos ícones são preservados.

## Limites

A escolha e o ajuste final das animações dependem da auditoria visual da etapa 1; a referência foi inspecionada pelo código, não por execução. A implementação deve seguir as etapas aprovadas.
