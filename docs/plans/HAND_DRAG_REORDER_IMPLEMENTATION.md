# Implementação da reorganização visual da mão

Guia: `HAND_DRAG_REORDER_ARCHITECTURE_AND_PLAN.md`.

| Etapa | Resultado | Commit |
|---|---|---|
| 1 | Contrato, inserção e reconciliação por instância, preservando entradas | `1f66497` |
| 2 | Ordem canônica separada da visual; memória de UI por coleção/setting/run/branch/ator/conexão | `9624b0a` |
| 3 | Slot de prévia, fronteiras estáveis e histerese | `49baae7` |
| 4 | Soltura local consumida pela mão e encaixe pelo executor compartilhado | `aa22db5` |
| 5 | Compra, uso, retenção e settlement projetados sobre a preferência local | `c4e0411` |
| 6 | Rolagem nas bordas durante gesto e foco seguindo os vizinhos visuais | `64dc4b5` |
| 7 | Cenários no seletor existente do laboratório, dirigindo o gesto de produção | `525b671` |
| 8 | API real, simulação/hash, replay, interrupções, capturas e árvore preparada para commit | Commit de certificação deste documento |

As oito etapas estão implementadas. O relatório está em `HAND_DRAG_REORDER_VALIDATION.md`.

Não há comando REST de reorganização, mudança de regra ou mutação dos modelos recebidos. Alterações não relacionadas do workspace são preservadas.

## Uso

- Arrastar uma carta sobre a mão abre espaço entre as vizinhas; soltar confirma a inserção.
- Soltar fora da mão continua usando o fluxo de jogar por alvo/candidato publicado.
- Escape e botão direito cancelam a prévia. Toolbar, pilhas, preview, painel do personagem e outros controles sobrepostos não são slots.
- A organização sobrevive a refresh e à remontagem da tela do mesmo combate. Cartas novas entram à direita; sobreviventes e retidas mantêm sua ordem relativa.
- A ordem é mantida em memória nesta sessão da interface. Não é gravada no save, na timeline ou no disco; reiniciar a aplicação restaura a ordem canônica.
- Mouse faz a reorganização nesta versão. A navegação e seleção existentes por teclado/controle seguem a ordem visual, sem novos atalhos ou mudanças de remapeamento.

## Laboratório

No seletor de animações, abrir **Card piles → Hand arrangement**:

- **Move first to last**;
- **Move last to first**;
- **Cancel insertion**.

Os cenários usam o próprio `CardHandView`, sem mão ou executor duplicado. O campo de segundos controla o tempo de reorganização na prévia; **Apply animation to combat** salva explicitamente esse tempo em `hand_transition`, pelo mecanismo de perfil visual existente. Os padrões continuam iguais enquanto o usuário não aplicar outro valor.

## Configuração e ciclo de vida

`HandVisualStyle.reorder_edge_band` e `reorder_scroll_speed` controlam somente o auto-scroll durante o arraste. `reorder_enabled` e `reorder_drop_filter` permitem ao componente desabilitar a reorganização ou excluir áreas sobrepostas, sem exigir novos campos dos settings.

O cache de arranjos é limitado a 16 contextos por `VisualContext`; o menu/roteador o limpa quando não há run ou a run terminou. Contextos derivados de inspeção/histórico recebem armazenamento separado. Não há persistência global estática.

A conclusão dos cenários observa `reorder_settled`, que acompanha a mão e os movimentos atuais. Não espera tweens antigos substituídos pelo reflow, e não depende de fallback do director. Uma soltura já na pose final conclui sem timer vazio.
