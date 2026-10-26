# Deck Workshop e compartimentos

Mecânica opcional: nenhum setting antigo passa a conceder componentes por instalar esta atualização.

## Laboratório

1. Reinicie a API atualizada para publicar o setting `compartment-lab` (Deck Workshop).
2. Abra `examples/godot-engine-showcase/scenes/deck_compartment_lab.tscn` na Godot e execute essa cena (F6).
3. O laboratório cria sua própria run de teste pela API. Não reutiliza nem grava o bookmark da run que você estava jogando.
4. Arraste uma carta para um compartimento, ou selecione a carta, escolha o destino e use Move Selected.
5. A engine valida a distribuição e devolve as cartas efetivas. Mudanças continuam pendentes até Apply.
6. Clear Draft deixa a distribuição vazia; Restore volta ao estado confirmado. Back pede confirmação para descartar mudanças.

O laboratório usa `CardView` e `CardFactsPresenter`, os mesmos componentes de apresentação do combate. Não calcula efeitos ou custos na Godot. A coluna Neutral não é uma zona de compra: indica ausência de contribuição do compartimento.

Conteúdo demonstrativo: Ward acrescenta um efeito de block; Recovery acrescenta custo, condição e efeito de recuperação de health. O outro personagem de conteúdo possui capacidade/estrutura distintas; não há seletor de personagem dentro da run normal.

Conexão indisponível não é substituída por exemplos offline. Em falha de resposta de um commit, Retry Same Command reutiliza a identidade para evitar duplicar operações. Em conflito de versão, reabra a tela para revisar o estado real antes de reenviar.

Contrato REST: [deck-compartments](../api/deck-compartments.md). Arquitetura: [plano](../plans/DECK_COMPARTMENTS_ARCHITECTURE_AND_PLAN.md).

## Dentro da run

Selecione Deck Workshop no menu de settings e crie uma run. No mapa ou na tela da atividade, o botão **Deck Build** aparece somente quando a engine anuncia a janela de alocação. A tela é o mesmo editor do laboratório, mas lê e altera somente o personagem da run atual através de GameSession. Fora dessa janela, não existe comando executável anunciado e a API também rejeita a alteração.
