# Som de entrada nas cartas

`CardHandView.card_hovered` também sinaliza a retomada do mouse após foco de
teclado/controle ou reconciliação da interface. Isso é necessário para atualizar
preview e cancelar o alvo anterior, mas não significa uma nova entrada física.
Usar esse sinal diretamente para áudio fazia cada retomada repetir o som.

`card_hover_entered` é emitido somente quando muda o ID da carta sob o ponteiro.
O laboratório e o combate usam esse sinal para `GameAudio.card_hover`. O sinal
anterior continua atualizando a interação, sem emitir áudio por conta própria.

- Movimentos dentro da mesma carta não repetem o som.
- Reconciliação de foco sobre a mesma carta não repete o som.
- Trocar de carta ou sair e reentrar emite uma nova entrada.
- Foco programático, modal de leitura e arrasto não disparam o som de hover.
- O evento usa três recortes de papel do Kenney Casino Audio 1.1 (CC0), com 85 ms,
  cooldown de 65 ms e uma voz simultânea para não acumular uma raspagem contínua.
- Compra e uso também usam o pack de cartas; ver `GODOT_AUDIO_DESIGN.md`.

Regressões em `audio_contracts.gd` verificam entradas, seis retomadas consecutivas
de foco sobre a mesma carta, manutenção do sinal de targeting e saída/reentrada.
A geometria e os tempos das animações não mudaram; apenas o hash atual de
`card_hand_view.gd` no baseline de programmer art acompanha o novo sinal.
Regras, RNG e snapshots da engine não são envolvidos.
