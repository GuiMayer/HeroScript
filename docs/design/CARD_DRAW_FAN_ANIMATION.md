# Compra de cartas alinhada ao leque

## Contrato visual

Laboratório e combate executam transferências pelo mesmo `CardZoneMotion.execute`.
O executor prepara a mão antes de iniciar o movimento e assenta a etapa quando o
handle termina. Nenhum dos consumidores mantém uma implementação paralela.

- As posições de destino respeitam a ordem publicada das cartas.
- As cartas que já estavam na mão abrem espaço durante a compra.
- Cada carta comprada nasce visualmente com a inclinação de seu destino no leque.
- O centro percorre uma curva Bézier cúbica, com tangente final alinhada à carta.
- A chegada e a carta real compartilham centro, rotação, escala e tamanho. Não há
  uma segunda animação corretiva de encaixe.
- A pose é convertida entre os espaços locais da mão e do overlay, incluindo
  deslocamento, escala e rotação dos pais.
- Os destinos são consultados durante o movimento para acompanhar redimensionamento.

`CardMotionPose` reúne as operações de pose e trajetória. `CardHandView` publica
`destination_pose`; `CardTransitRegistry` preserva a pose ao emprestar a carta.

## Duração e autoridade

Os valores de duração, escalonamento e stagger continuam vindo da política de
movimento capturada. A curva não acrescenta timers, filas ou espera de encaixe.
O reposicionamento das cartas existentes é limitado à duração de chegada quando
ela for menor que a transição normal da mão. Movimento reduzido, modo instantâneo,
pausa, skip e cancelamento continuam usando o lifecycle existente.

Etapas sem transferências não preparam nem recalculam a mão desnecessariamente.
Regras, API, RNG, ordem dos comandos e snapshots da engine não são alterados.

## Regressão

`tests/card_draw_pose.gd`, incluído na suíte padrão de UI, verifica:

- mãos 0→1, 0→2, 2→5 e 5→10, nos modos adaptive e reading;
- ordem de inserção publicada, inclusive antes de cartas existentes;
- trajetória intermediária e igualdade da pose no frame final;
- pais transformados e duração curta de 0,04 segundo;
- laboratório real e tela de combate com o mesmo ciclo de 0,3 segundo;
- pausa, skip, instantâneo, movimento reduzido e limpeza dos visuais;
- preservação dos dados publicados, sem emissão de comandos de gameplay.

O combate desse teste usa gateway de fixture, não uma campanha na API real.
Os demais testes de coreografia, ciclo de turno e seleção complementam a cobertura.
O hash de `card_hand_view.gd` no baseline atual de programmer art foi atualizado
deliberadamente para esta mudança de movimento autorizada; as medidas e o baseline
original de geometria não foram regravados.
