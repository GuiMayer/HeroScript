# Laboratório de inscrições — referências e interação

Revisão: 2026-10-15. Escopo: cena isolada de validação visual, não implementação das regras de inscrições na engine.

## Referências consultadas

- [Warframe — Mods Guide (oficial)](https://www.warframe.com/en/news/mod-ja): separa coleção, posições equipadas e orçamento; cada mod ocupa uma posição e informa seu custo de Drain. Aproveitamos a leitura de custo individual junto do orçamento total. Não copiamos polaridades, progressão ou regras de propriedade.
- [Duet Night Abyss — How to Get and Equip Demon Wedges (GameWith)](https://gamewith.net/duetnightabyss/71207): o guia mostra a tela de equipamento e descreve arrastar até uma posição vazia ou usar Quick Equip, com Tolerance visível. É uma referência editorial com imagens, não documentação oficial; a página informa que deixou de ser atualizada. Usamos apenas o padrão de interação, não seus números/regras como especificação atual do jogo.
- [Godot — Control (oficial)](https://docs.godotengine.org/en/stable/classes/class_control.html): arrasto nativo com `_get_drag_data`, preview descartado automaticamente, consulta sem mutação em `_can_drop_data` e entrega em `_drop_data`.

## Aplicação no laboratório

1. Coleção à esquerda; quadro de posições e detalhe da inscrição no centro; atributos do personagem à direita. Relíquias e modificadores de cartas não são exibidos nem carregados no fixture deste laboratório.
2. Posição vazia: fundo verde-acinzentado, texto explícito, símbolo `+` e instrução “Solte aqui”. Posição ocupada: fundo marfim e identificação do item/custo. A diferença não depende só de cor.
3. Cores de vazio e borda são propriedades do componente de loadout; cor de destino é propriedade do tile. Não estão nas regras ou no conteúdo de combate.
4. Arrastar mostra uma cópia visual junto do ponteiro. Destinos admissíveis recebem contorno verde, reforçado sob o ponteiro. Origem não é removida ao começar o gesto.
5. Coleção → posição: equipa o mesmo item local; se houver outro, ele fica sem posição, disponível na coleção. Um item já equipado é movido da origem, nunca duplicado.
6. Posição → posição ocupada: troca os dois itens. Posição → vazia: move. A alternativa selecionar item + posição + “Equipar no rascunho” usa a mesma transição.
7. Soltar fora cancela, sem perda de item. Payloads de outra cena, tipo desconhecido ou versão anterior do quadro são rejeitados. Nenhum drag inicia uma transação parcialmente aplicada.
8. Orçamento numérico acompanhado de barra, custo individual e indicação de item no rascunho. Exceder orçamento continua permitido neste protótipo, com aviso: a validação real será da engine.
9. Busca localizada, nós estáveis e alternativa por clique/teclado preservados. A coleção tem rolagem própria para alcançar os itens inferiores sem deslocar o quadro. O ornamento de circuito foi compactado para priorizar os encaixes.

## Fronteiras e verificação

`inscription_tile.gd` trata gesto/preview e emite intenção; `loadout_view.gd` apresenta a projeção; o controlador encaminha para `inscription_loadout_draft.gd`, que modifica somente o rascunho em memória. Não há gravação, inventário real ou chamada REST nesta cena. As futuras regras de compatibilidade/capacidade não devem ser implementadas no tile.

### Atributos do personagem e linguagem

Em vez de descrever uma alteração numa carta, a inscrição informa `+20% Bônus de dano direto` ou `+25 p.p. Chance crítica`: são atributos do personagem. A coluna **Base** é imutável; **Equipado** reflete somente as inscrições instaladas no rascunho. Selecionar uma peça apenas inspeciona, sem somá-la. Remover ou trocar recalcula a amostra a partir da base, sem acumular mutações.

O vocabulário de atributo + bônus, com contrapartidas negativas explícitas, adapta a leitura de equipamento e mods das referências. É uma decisão própria de apresentação: não copia fórmulas de Warframe/DNA. Percentuais e pontos percentuais são distinguidos nas descrições. Bônus de dano crítico é o adicional por tier (base ilustrativa 100%); chance crítica pode ultrapassar 100%, sem cap fictício no laboratório.

O fixture oferece vida/energia/mana máximas, armor, dodge base 0%, críticos, bônus de dano direto/periódico/multiacerto, efeitos condensados, ganho de bloqueio, cura e tags fogo/veneno/gelo/raio. As últimas representam bônus de efeitos elegíveis daquela tag, não apenas dano, e não concedem novos efeitos ou ações. Valores e a disponibilidade de armor nas amostras não são balanceamento de jogo.

`loadout_attribute_sample.gd`, exclusivamente em `scripts/labs`, soma contribuições de apresentação aos valores base declarados no JSON; não é uma segunda bucket pipeline, não resolve efeitos e não estima mitigação, cura, críticos ou rolagens. `character_attributes_view.gd` apenas apresenta esses valores e a origem dos bônus. Na integração real, substituir o adapter de amostras pela projeção canônica da engine; não levar a soma local para a UI de combate.

O teste `tests/inscription_loadout_lab.gd` cobre movimento, troca, substituição, origem inválida sem alteração parcial, cancelamento fora, payload estrangeiro/obsoleto, diferenças de aparência, entrega nativa pelo viewport e início real por pressionar/mover o mouse. Matriz de layout: EN/PT-BR; 1280×720, 1920×1080 e 2560×1080; texto PT-BR em 120%. Também verifica ausência de chamadas à engine e de gravação de preferências.

Não foram adicionados autoequip, polaridades, upgrade ou salvamento: seriam decisões de gameplay e implementação de engine além desta correção de interface.
