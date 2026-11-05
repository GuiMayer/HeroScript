# Peças táticas no jogo

As peças aprovadas foram integradas como SVGs transparentes em
`examples/godot-engine-showcase/assets/tactical-pieces/`. Todos os arquivos têm
240×320, proporção preservada pelo `ArtSlot`, cores chapadas e nenhuma articulação.
As pranchas de conceito continuam separadas e intactas.

## Heróis

`hero-wanderer.svg`, `hero-regent.svg` e `hero-shard.svg` são variantes cosméticas
do mesmo slot `actor_adept`. O catálogo escolhe uma das três na primeira resolução
do slot e mantém essa escolha durante o processo do jogo. Reiniciar o aplicativo
permite outro sorteio; não há seletor de aparência por enquanto.

Reconciliar a tela, atualizar dados, abrir painéis e invalidar o cache do manifesto
não sorteiam novamente uma variante ainda válida. A escolha usa um
`RandomNumberGenerator` privado da apresentação, não o RNG global da Godot nem o
RNG autoritativo da engine. Não é gravada em comandos, snapshots ou saves.
Portanto, o replay pode ter outra aparência em uma nova sessão, mas os mesmos
resultados de gameplay. Overrides visuais de outros settings continuam soberanos.

## Inimigos

| Peça | Associações visuais |
| --- | --- |
| Atacante | Scout, Charger, Drone, Wisp, Goblin e Spark |
| Defensor | Sentinel, Bulwark, Prime, Orc Warrior |
| Canalizador | Spitter, Binder, Artillery, Corrosion/Prism Elite, Hexer e Echo |
| Enxame | Mite, Swarm e Venomling |

Os IDs são mapeados explicitamente nos manifestos; nenhum papel de combate é
inferido pela arte. Entidades desconhecidas recebem a peça atacante como fallback.
Esses mapeamentos não modificam ataques, atributos ou comportamento dos inimigos.

## Integração e validação

Os manifestos base, marfim de cenário e programmer art compartilham os arquivos.
O gerador de arte das cartas preserva esse conteúdo herdado do manifesto marfim.
`ArtSlot` e `CombatSceneryView` continuam sendo os componentes de produção;
nenhuma animação, disposição de HUD ou área de seleção foi substituída.

`tests/tactical_pieces.gd` cobre importação dos sete SVGs, todas as associações,
três variantes alcançáveis, estabilidade da escolha durante reconciliação e
invalidação, consistência entre painel e cenário, fallback e preservação dos
snapshots e do RNG global. O teste faz parte da suíte padrão de UI.
