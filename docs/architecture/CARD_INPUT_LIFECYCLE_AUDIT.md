# Auditoria: bloqueios de uso de cartas na Godot

Data: 2026-10-16. Escopo: cliente Godot, REST e apresentação; nenhuma alteração nas regras ou nos snapshots da engine.

## Resultado e evidências

O erro de `resolution: null`, corrigido anteriormente, não era o único caminho de bloqueio. Foram reproduzidos dois defeitos locais com testes que falharam antes da correção e uma falha de montagem com a API real. Não é adequado atribuir todos os relatos à latência REST ou afirmar que reiniciar o menu corrige a causa.

| Causa | Reprodução | Correção |
| --- | --- | --- |
| Recibo chega antes da entrada da cena | Iniciar encontro via REST, depois montar a tela: fila com frames, `dispatch_ready=false`, nenhum executor a ativava. | Retomar o envelope em `_ready`/callback deferido após entrar na árvore, com guarda para não ativar duas vezes. |
| Rebind de contexto durante envio | Controller inicia submit; troca escopo; conclusão antiga é ignorada e deixava `submitting=true`. | Reiniciar somente o latch da view no rebind. O lock de escrita da sessão continua impedindo concorrência; conclusão obsoleta não modifica a nova seleção e preserva o resultado de aceite. |
| Animação termina enquanto sessão está ocupada | `_on_settled` agenda uma única tentativa; naquele instante `busy=true`; nada voltava a reconciliar depois. | Guardar geração pendente e tentar reconciliar nos frames seguintes, apenas quando sessão sincronizada/ociosa, apresentação concluída e árvore não pausada. Consumir a geração antes de navegar. |
| Recovery encontra um head posterior ao recibo | Leitura falha após aceite; outro avanço torna o recibo antigo; recovery sincroniza mas não havia notificação para devolver a face emprestada. | Sinal explícito de apresentação descartada cancela apenas cosméticos, devolve transits e reconcilia o head válido antes de liberar input, sem reenviar a jogada. |
| Tween cancelado | Executor assume handle; tween é morto sem conclusão normal. | Detectar perda dos tweens possuídos e terminar o handle com fallback, sem esperar o watchdog inteiro. |
| JSON de apresentação/sessão incompleto | `frames:null`, coleções opcionais nulas e bundles/recibos com forma incorreta. | Normalizar coleções exclusivamente visuais; validar estrutura da sessão antes de publicá-la. Recibo 2xx malformado é resposta incerta: preservar o mesmo comando para recuperação idempotente, nunca autorizar outro envio. |
| Modal oculto ou sobreposto | Fechar uma janela enquanto outra existe, ou ocultar janela sem destruí-la. | Rastrear cada janela por referência fraca e observar visibilidade/saída, em vez de manter um booleano liberado só por destruição. |

## Contrato de liberação

Escrita pendente pertence à sessão, não à animação. Uma pausa, espera manual de animação ou perda de sincronização é um bloqueio legítimo; não deve ser removido por um timeout que libere novas jogadas cegamente.

Uma conclusão visual só reconcilia o estado final se sua geração/contexto ainda for atual. A recuperação de falha de leitura não reenvia uma escrita já confirmada. Uma escrita de resultado incerto reutiliza sua identidade original. Nenhuma dessas correções altera RNG, custo, seleção legal, efeitos, ordem canônica ou replay.

`CombatController.blocking_reasons()` expõe motivos distintos (`session_busy`, `submitting`, `unsynchronized`, `presentation_pending`, `animation`, `paused`, `disposed`) para diagnóstico. O teste online registra esses motivos e a posição da fila se uma ação não ficar pronta no prazo, sem forçar o desbloqueio para passar.

## Pesquisa em fontes primárias

- [Node e `_ready`](https://docs.godotengine.org/en/stable/classes/class_node.html#class-node-private-method-ready): a entrada e preparação na árvore são eventos diferentes de construir/configurar um objeto. A retomada de uma fila que usa nós vivos deve respeitar esse ciclo.
- [GDScript: await, sinais e corrotinas](https://docs.godotengine.org/en/stable/tutorials/scripting/gdscript/gdscript_basics.html#awaiting-signals-or-coroutines): await devolve o controle e retoma quando o sinal/corrotina completa. Logo, estados de contexto podem mudar entre início e retomada; não presumir que a configuração inicial da view ainda é atual.
- [Tween](https://docs.godotengine.org/en/stable/classes/class_tween.html): `kill()` aborta/invalida a operação; `finished` representa conclusão normal. O handle da aplicação precisa tratar cancelamento/perda de animação, não depender exclusivamente do sinal de término. O teste de tween morto confirma esse caso no runtime utilizado.

Essas fontes explicam os mecanismos da Godot; as causas específicas acima foram verificadas no código e nos testes do HeroScript, não inferidas apenas dos artigos.

## Certificação

- `combat_controller`: rebind durante comando, aceite/rejeição, seleção, bloqueios e descarte.
- `animation_live`: mesma tela real, recibo antes de montagem, settlement antes de idle, pausa, cancelamento, supersessão, resposta nula, recovery e saída terminal.
- `combat_input_soak`: 40 ciclos sem recriar o combate, com retenção, troca de zona, nulos, leituras atrasadas, rejeição, falha de refresh/recuperação, animação normal/reduzida e avanço manual. Exige exatamente uma escrita por tentativa.
- `combat_input_online`: REST real contra Kestrel/armazenamento isolados, settings `default` e `ascendant`; 16 comandos de cartas/turnos aceitos na execução de referência e duas verificações semânticas de replay válidas. Exige incremento de sequência, sessão sincronizada e liberação dos gates após cada ação.
- `compact_session`, `presentation_envelope` e `presentation_director`: dados malformados, recuperação com mesma identidade, coleções nulas e tween cancelado.
- `turn_hand_cycle`: ciclo de zonas e modais sobrepostos/ocultos.

O teste online não pertence à suíte offline padrão: requer API isolada explicitamente indicada por `-ApiUrl`. Não deve apontar para a API/saves do jogador. A suíte padrão inclui o soak e o controller para prevenir regressão recorrente.

Resultado final: 50 scripts da suíte offline, 1.357 checks aprovados; mais 42 checks online com duas verificações de replay válidas. Os avisos intencionais do teste de watchdog e da rejeição de composição gráfica inválida não são erros de execução. Foram corrigidos também dois problemas do harness encontrados durante a certificação: fixture de replay sem leitura de bookmarks e encerramento do processo antes de drenarem leituras de menu/voz Ogg. A execução final não apresentou os vazamentos de teardown que esses testes inicialmente mostraram.

## Aplicação e limites

O log disponível do jogo apontava repetidamente para a atribuição antiga de Dictionary em `presentation_envelope.gd:13`; essa linha não corresponde à versão corrigida em disco. Scripts já carregados por um processo aberto não são atualizados apenas pelo commit/push. Feche e inicie novamente o jogo para executar esta versão; não é necessário apagar saves ou reiniciar a API.

Não se afirma ausência universal de bugs, balanceamento ou cobertura de todo mod possível. Casos inválidos continuam bloqueados com recuperação explícita; espera manual configurada continua manual. A validação cobre os caminhos reproduzidos e o fluxo REST publicado, sem relaxar a autoridade da engine.
