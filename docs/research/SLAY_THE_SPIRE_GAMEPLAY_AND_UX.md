# Slay the Spire: sistemas, experiência e aplicação à demo

Pesquisa iniciada em 12/09/2026. Escopo: Slay the Spire original, não sua sequência. O objetivo é aprender com a estrutura das decisões, sem copiar personagens, artes, textos ou todo o conteúdo do jogo.

## Síntese

O diferencial a perseguir não é uma interface cheia de cartas: é permitir que o jogador **entenda uma ameaça, compare respostas, execute uma decisão e reconheça sua consequência**. A demo já possui regras, previews, recursos, transações, progressão e histórico. Sua apresentação anterior expunha muito do formato desses dados, mas pouco da importância de cada informação.

A prioridade desta entrega é a interface de gameplay. Os menus existentes são preservados. Profundidade de conteúdo e balanceamento continuam sendo trabalho de game design; esta entrega não torna quatro definições de cartas equivalentes a um deckbuilder comercial.

## O que a pesquisa sustenta

### Decisões táticas com informação

O relato dos desenvolvedores sobre os protótipos descreve jogadores confusos quando não conseguiam antecipar os adversários. A evolução das intenções foi central para tornar o combate compreensível. Consequência para a demo: intenção próxima do inimigo, alvo inequívoco e previsão acessível antes do comando. Uma previsão incerta deve continuar marcada como incerta. [Entrevista da Ars Technica](https://arstechnica.com/gaming/2019/05/video-slay-the-spire-is-a-friendly-game-of-death-but-it-was-hard-to-get-it-right/).

### Construção de deck é adaptação

Anthony Giovannetti, designer da Mega Crit, descreve recompensas variáveis que estimulam adaptação, estilos ofensivos e defensivos, consistência das compras e combinações entre cartas, upgrades e recursos consumíveis. Não basta aumentar números: novas escolhas precisam interagir com a estratégia existente. A recomendação de deck pequeno no artigo está contextualizada; não é uma lei para todo arquétipo. [Artigo do próprio designer](https://blog.playstation.com/2019/05/13/how-to-come-out-on-top-in-slay-the-spire-out-may-21-on-ps4/).

### Variedade de conteúdo sustenta a repetição

O material oficial apresenta personagens com conjuntos próprios de cartas, numerosos encontros e itens e níveis procedurais. Nossa campanha atual é uma sequência curta de sete atividades com poucos oponentes. Ela demonstra os subsistemas, mas ainda não oferece essa amplitude de decisões. [Press kit da Mega Crit](https://www.megacrit.com/press-kits/slay-the-spire/).

### Balanceamento exige observação

A descrição da palestra de Giovannetti na GDC destaca métricas e feedback de jogadores como ferramentas de balanceamento, preservando dificuldade e sensação de jogo. Aplicação proposta: medir resultados por seed, turno, carta, encontro e escolha; juntar números com sessões de observação. Foi consultada a descrição da palestra, não feita uma análise integral do vídeo. [GDC: Metrics Driven Design and Balance](https://www.gdcvault.com/play/1025731/-Slay-the-Spire-Metrics%EF%BB%BF).

## Comparação com o projeto

As decisões abaixo são recomendações para HeroScript, não afirmações de que a implementação interna de Slay the Spire usa esta arquitetura.

| Sistema | Base atual | Direção de produto |
|---|---|---|
| Orçamento por turno | Modos por recurso, ações e prioridade | O jogador deve ver o orçamento e a ação de encerrar turno; modos diferentes não podem herdar regras visuais de energia |
| Intenções da IA | Previews publicados pela engine | Destacar ameaça sobre cada oponente; conservar indicação de incerteza |
| Alvos e legalidade | Candidatos canônicos com custos e alvos | Seleção em duas etapas, retratos clicáveis, cancelamento e inspeção de indisponibilidade |
| Cartas e efeitos | Containers de componentes e pipeline | Nome, custo, arte e efeito separados; custo não repetido como efeito; descrição detalhada no inspector |
| Defesa e recursos | Recursos genéricos e mitigação configurada | Exibir valores e deltas publicados; nenhuma regra especial de vida/bloqueio no renderer |
| Mão e zonas | Instâncias, compra, descarte, exílio | Mão central; contadores acionáveis; nunca revelar ordem embaralhada na inspeção |
| Status, relíquias e upgrades | Autoridades e avaliações separadas | Diferenciar identidade da carta, upgrade e contexto temporário; ampliar explicações contextuais em uma próxima iteração |
| Recompensas | Seleções e decomposição pela API | Escolhas visuais comparáveis, alternativas secundárias separadas |
| Loja | Ofertas com preços calculados | Mostrar a carta real da oferta e seu preço, inclusive custos em recursos diferentes |
| Preparação e forge | Comandos configurados | Apresentar opções como decisões; confirmar alterações importantes |
| Mapa | Nós, conexões e comandos de viagem | Mostrar conexões reais e etapas resolvidas; apenas destinos anunciados podem ser acionados |
| Histórico e replay | Journal, snapshots, branches e simulação | Manter ferramenta de inspeção separada da mão e dos botões principais |
| Ritmo e animação | Recibos + cursor de apresentação | Animação não envia comandos e não recalcula valores; manter pausa e movimento reduzido |
| Metaprogressão e conteúdo | Não demonstrados em profundidade nesta campanha | Definir identidade do jogo, arquétipos, progressão e variedade antes de ampliar a campanha |

## Arquitetura da apresentação

```text
REST → gateway → GameSession (snapshot e comandos sem UI)
                        ↓
              presenters passivos
                        ↓
          telas + CardView + ActorPanel

art_manifest.json → ArtCatalog → ArtSlot
recibo da engine  → Playback   → feedback visual
```

- O presenter recebe cópias dos dados, relaciona IDs e formata conteúdo. Não calcula dano, não decide legalidade e não determina preços.
- A tela encaminha uma escolha já anunciada. Não monta uma autoridade alternativa de combate.
- O catálogo de arte associa conteúdo a slots estáveis. Um mod sem ilustração continua jogável com o fallback da categoria.
- Os containers controlam o layout; animações de cartas usam escala ou uma cópia visual fora do container. Essa separação segue o modelo de layout documentado pela Godot. [Containers na Godot](https://docs.godotengine.org/en/stable/tutorials/ui/gui_containers.html).
- Inglês é o idioma e a chave de fallback. Português permanece um catálogo separado.

## Mudanças desta entrega

1. Arena com lados visualmente separados, cenário próprio e retratos substituíveis.
2. Intenções sobre os inimigos, recursos agrupados e status próximos ao personagem.
3. Carta reutilizável com custo, título, placeholder e resumo dos efeitos.
4. Custos identificados pelos componentes do candidato, não por nomes de recursos.
5. Ação principal do turno em posição estável; inspeção/cancelamento contextual.
6. Escape primeiro cancela seleção; pausa continua disponível quando não há seleção.
7. Progressão em rota lateral com arestas reais e destaque da etapa atual.
8. Recompensas, compras e upgrades apresentados como cartas; preços vêm do snapshot.
9. Confirmação para compras, decomposição, upgrades, rerolls e abandono.
10. Testes de projeção, campanhas reais e layout em ambos os idiomas.
11. Mão inicial da campanha com dois ataques, duas defesas e uma Fire Orb. A composição total do deck permanece igual; a ordem inicial é uma escolha didática no JSON, não uma regra no cliente.
12. Limpeza de bloqueio passa pela pipeline configurada `signed_resource_delta`: uma redução assinada não pode usar o mínimo zero da pipeline de magnitude de dano. Os testes reproduziram saldo indevido e cobrem proteção durante a ativação inimiga, limpeza no próximo início e replay.
13. A verificação de uma jornada completa recebe prazo de comunicação maior que comandos interativos. Uma execução longa de replay não deve ser confundida com divergência determinística.

### Migração de conteúdo

Essas mudanças de regras valem para revisões novas. Runs já criadas mantêm sua revisão imutável; iniciar uma nova jornada após reiniciar a API é o caminho mais simples para testar o novo deck e a limpeza de bloqueio. Não se deve modificar saves antigos para aplicar esse ajuste.

## Próximos passos para um jogo comercial

1. **Identidade de combate:** definir 2–3 arquétipos com oportunidades e fragilidades. Não adicionar cartas que só repetem números.
2. **Encontros com personalidade:** padrões e combinações de inimigos que cobrem respostas distintas; o chefe atual é principalmente uma variação de recursos.
3. **Escolha de caminho:** criar ramificações com riscos/recompensas diferentes nos JSONs. A interface suporta conexões, mas a campanha continua deliberadamente curta e linear.
4. **Economia:** testar custo de oportunidade entre compra, melhoria e recuperação; definir opções de recusa no modo, quando desejadas, sem inventá-las no cliente.
5. **Leitura avançada:** comparar upgrade antes/depois e exibir tooltips de status/relíquias a partir de descrições e avaliações publicadas.
6. **Feedback:** áudio específico por categoria de evento, transições de turno e animações por frame com estados intermediários explicitamente fornecidos. Não reconstruir simulação no cliente.
7. **Validação humana:** observar jogadores que nunca viram o projeto. Perguntar quem atacará, quanto custa uma carta, por que está indisponível e o que mudou após a ação.

Critérios sugeridos para playtest: entender uma intenção sem abrir logs; localizar custo e alvo sem tentativa/erro; cancelar sem perder o turno; comparar duas ofertas antes de comprar; completar uma jornada sem ajuda. Testes automatizados não substituem essas observações.
