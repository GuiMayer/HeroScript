# Correção da espera antes das animações de cartas

Início: 2026-10-13. Conclusão: 2026-10-14. Base: `c70adcd`.

## Diagnóstico

Na partida Ascendant consultada, ações legais levaram 0,89–1,12 s e a inspeção com alvo 0,52–0,64 s. São leituras do mesmo estado, não medidas da duração das animações nem percentis de todos os combates. O cliente aguardava o refresh completo antes do voo; apenas a preparação reversível acontecia antes disso.

## Engine: previews imutáveis e limitados

- `LegalActionResolver` reutiliza avaliações e conjuntos completos de opções com a mesma entrada. As chaves incluem hashes canônicos completos da run e do combate, origem e comando/ator. Sequência sozinha não seria suficiente para alterações de recursos no mesmo step, outros settings ou branches.
- `CardInspectionService` reutiliza inspeções pela mesma versão e solicitação completa, incluindo alvos, opção de custo e nível máximo de detalhes. A política efetiva permanece no snapshot da run. A representação dos alvos espelha a consulta canônica de legalidade, sem inventar um alvo padrão.
- O lote de ações/inspeções usa o mecanismo existente de memoização de hashes para evitar serializar repetidamente o mesmo agregado imutável durante a consulta.
- Os caches usam o LRU thread-safe existente: 128 avaliações, 16 conjuntos e 128 inspeções. São limites operacionais de memória, não limites da mão ou regras de jogo. Falhas de infraestrutura não são guardadas. Avaliações de ilegalidade determinísticas podem ser reutilizadas para a mesma entrada.
- Os dois serviços são registrados no coordenador de caches como derivados, com estatísticas, limpeza e capacidade limitada. Uma nova versão/input produz outra chave; não há alteração de RNG, cálculo, regras ou commits.

## Godot: aceitar, mover, sincronizar e continuar

Antes da confirmação, a carta recebe apenas a preparação reversível. Ao receber o recibo aceito da sequência/combate esperados, a mesma face é emprestada ao registro de trânsito e começa a ir para a área de uso. Não há publicação antecipada de custos, impactos, novas descrições ou desbloqueio de comandos.

O voo e o refresh acontecem em paralelo. A fila continua ligada ao envelope sincronizado. Se o voo já terminou, sua etapa é concluída sem repetir a animação ou adicionar um timer vazio. Se ainda está em movimento, a fila acompanha o mesmo handle. Retenção/descarte seguem exclusivamente os fatos publicados, quando disponíveis.

Pausa congela a apresentação. Rejeição não inicia voo. Refresh falho mantém a carta aceita em espera, com input bloqueado; a reconexão somente lê e reaproveita a apresentação, sem reenviar o comando. Destruir a tela limpa handles e trânsito. Movimento reduzido/preset instantâneo não criam essa espera visual.

Se a sincronização termina durante a pausa, o cue da carta é retomado sem exigir confirmação manual adicional ao voltar. Se outra revisão já substituiu o recibo, o handle antecipado é cancelado e a nova projeção prevalece; a próxima carta não herda um trânsito órfão.

## Verificação e limites

Teste REST em host e persistência isolados, com conteúdo real do mundo 1 Ascendant, em build Debug:

| Consulta | Primeiro cálculo | Mesma consulta reutilizada |
| --- | ---: | ---: |
| Ações legais | 665,3 ms | 32,4 ms |
| Inspeção da mão | 87,1 ms | 17,4 ms |

O teste compara os JSONs canônicos, o hash da run antes/depois, o reaproveitamento entre legalidade e inspeção, a capacidade dos caches e a equivalência após limpar/recalcular. Esses números não são uma garantia universal de latência inferior a um segundo. Após um comando mudar o estado, a primeira consulta dessa versão continua exigindo cálculo.

Uma segunda rodada junto de testes paralelos registrou ações legais em 1.094,7/38,5 ms e inspeção em 127,6/48,2 ms (primeiro cálculo/reuso). São amostras, não percentis, e reforçam que ainda há custo no cálculo de uma versão inédita.

`animation_live` controla separadamente a resposta do comando e a inspeção. Verifica início do despacho do voo em menos de 50 ms após o recibo no fixture, ausência de impactos antes da sincronização, face única, pausa, fila manual, aproveitamento do voo concluído, rejeição, falha de refresh/reconexão, retorno retido, modo sem animação e encerramento da tela. Esse limite do fixture não é uma medida end-to-end do clique no servidor real.

Também foram executados testes de movimentos compartilhados, seleção/centralização da mão, áudio, envelopes e recuperação. A suíte Core passou com 1.669 testes. As verificações da API usam seus próprios saves; a partida do jogador não foi modificada. Para ativar os novos caches no serviço já em execução, é necessário reiniciar a API, além da janela da Godot.

Os cinco testes focados de API passaram em uma rodada completa: cache/previews com conteúdo real, dois testes de protótipo determinístico/reconexão e dois testes das mecânicas do mundo 1. A suíte geral da API foi abortada pelo limite de 300 segundos, após 207 testes sem falhas. O ensaio de dez jornadas completas Volatile também não terminou nesse limite; essas duas execuções não são certificações completas. A rodada visual final de `animation_live` passou duas vezes, com 29 verificações por execução, incluindo quatro capturas. O teste sem renderização passou com 23 verificações antes da adição dos dois casos de supersessão; as verificações desses casos estão nas duas execuções visuais finais.
