# Otimização dos gargalos restantes — 2026-10-15

Continuação da [validação REST](REST_INTERACTIVE_LATENCY_VALIDATION.md).
Preservar regras, resultados, RNG, hashes, imutabilidade e persistência durável.
Cada etapa será validada e commitada separadamente.

## Sequência

1. **Compartilhamento estrutural no encoding canônico.** Novos snapshots não
   devem serializar novamente cada regra, mapa e encounter anterior quando o
   objeto imutável correspondente continua sendo o mesmo. Usar os metadados
   reais do serializer, manter fallback conservador, limite de profundidade,
   tipos declarados, conversores e ordem canônica. Reuso operacional limitado.
2. **Remover trabalho derivado redundante após o novo profiling.** Investigar
   os trechos que continuarem dominantes, sem legalidade aproximada ou cortar
   fatos. Fragmentos gerados internamente podem evitar validações repetidas se
   testes comprovarem a mesma aceitação/rejeição e bytes.
3. **Certificação ampliada.** Repetir o ato completo e comparar o hash de run e
   combate com o serializer de referência após cada comando. Medir por tipo,
   reportar máximos e testar múltiplas seeds. Validar os testes Core, API,
   determinismo entre processos, recuperação e cliente Godot relevantes.

A referência é o conteúdo publicado, nesta máquina local. Não é possível
garantir tempo absoluto para qualquer hardware, carga do SO ou mods sem limite;
outliers não serão ocultados para declarar a meta cumprida.

## Etapa 1 — implementação

Encoding por propriedades aplicado somente a `RunState`, `CombatState` e
`RunEncounterState`, com metadados padrão do System.Text.Json. Folhas usam o
serializer existente, preservando seus conversores/polimorfismo. Formatos não
auditados, extension data, callbacks e conversores por propriedade seguem o
caminho de referência. O array de encounters mantém ordem e compõe seus elementos
imutáveis, incluindo todo o histórico no hash.

Um LRU operacional de até 128 entradas/8 MiB é compartilhado por escopos
aninhados e descartado ao terminar a operação. Identidade + tipo declarado são
as chaves; rótulo de revisão, sequência e igualdade parcial não são suficientes.
O cache não guarda resultados mutáveis nem altera os snapshots.

Fronteiras, fases e fingerprint da descoberta também compõem os mesmos campos
por esse caminho, em vez de embutir novamente o agregado num objeto anônimo.
Fragmentos são criados somente pela fábrica interna que serializa/valida os
valores; a composição não repete parsing de JSON já gerado. O limite original
de profundidade é checado explicitamente antes de cada composição.

Testes adicionados: bytes exatos em 180 revisões com partes compartilhadas,
escopos aninhados e evicção; profundidade inválida e tipo declarado. O teste do
ato compara também SHA-256 do serializer de referência após cada comando, fora
da região cronometrada.

Validação: 1.740 testes Core passaram. O ato adicional com comparação independente
após cada comando também passou. O benchmark sem trabalho de referência
concluiu os 9 encontros/172 comandos, replay e recuperação, com vida final 50.
Sessão p95 238 ms/máximo 537 ms; comandos p95 agregado 936 ms, mas máximo 1.899 ms.
Fim de turno p95 1.306 ms. Portanto esta etapa não fecha a meta de máximo 1 s.

Uma variante intermediária com parsing redundante dos fragmentos foi rejeitada
por regressão de desempenho. Testes de referência e medições de latência foram
separados: o primeiro valida cada hash; o segundo não adiciona alocações de
certificação entre as ações medidas.

## Etapa 2 — próximo gargalo identificado

`ContentRuntime.GetDefinition<T>` desserializa a definição em toda consulta.
Isso também impede reaproveitar o hash da mesma definição entre cálculos.
Adicionar cache tipado limitado apenas para classes declaradas profundamente
imutáveis; consumidores de tipos mutáveis continuam recebendo novas instâncias.
Chave deve incluir instância do runtime, tipo, kind e ID; hot reload não pode
reusar definições de outra revisão/configuração. Integrar com o coordenador de
caches e testar concorrência, evicção, invalidação e igualdade de bytes/hashes.
