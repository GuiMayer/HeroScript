# Aquisição de inscrições

`LoadoutPolicyDefinition.startingInscriptionIds` concede instâncias com proveniência `run-start` ao registrar os personagens. Posse não equipa automaticamente. `stations` descreve recompensas, lojas e crafting dirigido sobre o mesmo inventário independente de cartas.

Cada estação declara `stationId`, `nodeIds`, `selection` (`Random` ou `Directed`), `pool` (ID → peso inteiro positivo), `offerCount`, `maximumPurchases` e `costs`. Sem custos, a oferta é uma recompensa; com custos usa a carteira da run. `Directed` oferece o catálogo elegível sem sorteio, e o limite de compras torna o crafting finito. Não há reroll implícito.

`OPEN_INSCRIPTION_OFFER {actorId, stationId}` captura opções, preços, destinatário, revisão e fingerprint de elegibilidade. A ordem do pool é canônica antes do sorteio. Uma estação abre no máximo uma vez por nó, mesmo com inventário por personagem. Não é possível multiplicar recompensas trocando o personagem.

`CHOOSE_INSCRIPTION_OFFER {offerId, inscriptionId}` paga e concede atomicamente uma das opções capturadas. `inscriptionId: null` recusa/fecha sem pagar. Uma mesma opção não pode ser comprada duas vezes; compras anteriores permanecem quando o restante é recusado. Replay usa o snapshot de elegibilidade da run, nunca o perfil atual.

Ambos são comandos do gateway normal, com versão/idempotência/journal. Não existe endpoint público de grant livre. Fora dos nós autorizados, durante combate, depois de resolver o nó ou fechar a oferta, as operações são rejeitadas. O ator destinatário é capturado ao abrir, não enviado novamente na compra.

Um nó `InscriptionStation` com `definitionId` igual ao `stationId` exige fechar a oferta antes de concluir o estágio; recusar é válido. Estações adicionais em lojas/descansos são opcionais. A interface usa as opções/preços/apresentação publicados nos comandos, sem selecionar conteúdo ou calcular regras localmente.

## Ascendant: configuração inicial

- Cinco definições: chance crítica, bônus crítico, geração de Block, potência residual e dano de ataques.
- Seis posições, capacidade 12 e inventário `Actor`; foco e defesa iniciais possuídos, não equipados.
- Cinco antigos nós de relíquias numéricas agora oferecem inscrições aleatórias. As definições históricas de relíquias permanecem no catálogo, mas não são recompensas desses nós; saves existentes não são convertidos silenciosamente.
- Loja em `s08_market`: até duas compras, 30 Gold por peça; crafting nos dois descansos: uma compra dirigida, 40 Gold.
- As relíquias excepcionais Iron Vow e Blood Covenant substituem duas recompensas antigas. Armor não foi incluído no catálogo comum de inscrições.
- Precision trace é uma opção desbloqueada ao concluir uma run, sem aumentar permanentemente atributos. O setting usa as regras de elegibilidade já existentes; o tipo `Inscription` foi acrescentado ao final do enum, sem renumerar os anteriores.

**Mapa/atividade → INSCRIPTIONS** abre o workspace conectado. É possível inspecionar e reorganizar fora de combate. O laboratório do menu principal mantém exemplos offline como referência visual, além da opção explícita de conectar a run atual.
