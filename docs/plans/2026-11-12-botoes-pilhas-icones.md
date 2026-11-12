# Plano: botões das pilhas no combate MVP

## Esquema proposto

Em cada botão: ícone quadrado à esquerda, com a quantidade inteira sobreposta em uma pequena etiqueta escura de alto contraste; nome da pilha à direita do ícone, alinhado à esquerda e ocupando o espaço restante.

- Compra: `faithtoken/card-draw.svg`.
- Descarte: `faithtoken/card-discard.svg`.
- Exílio: `delapouite/card-burn.svg`.
- Deck: `delapouite/stack.svg`.

## Implementação após aprovação

1. Conferir a correspondência dos botões no componente compartilhado das telas de combate e integrar os ícones da coleção local, mantendo a proporção 1:1.
2. Ajustar a distribuição, o contraste da quantidade sobre o ícone e o texto, preservando as ações e contagens dos botões.
3. Atualizar `CREDITS.md` com os ícones efetivamente usados, seus artistas e atribuição conforme os termos da coleção.
4. Verificar o resultado visual e o comportamento das pilhas; registrar as alterações em um commit separado.

A implementação aguarda aprovação do esquema.
