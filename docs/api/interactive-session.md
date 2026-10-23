# Sessão interativa REST

Use `GET /api/v1/runs/{runId}/session?expectedSequence=N` para sincronização.
O cliente não precisa carregar o agregado completo após cada ação.
Capability: `compact-interaction-sessions-v1`. A versão das regras continua 31;
o launcher exige a capability para não reutilizar um host antigo sem `/session`.
Exemplos executáveis: [interactive-session.http](../../examples/http/interactive-session.http).

## Fluxo

1. Envie o comando canônico com commandId e versões esperadas.
2. Aguarde o recibo durável; não é uma previsão otimista.
3. Leia a sessão na sequência confirmada pelo recibo.
4. Valide a versão e publique o bundle inteiro, nunca seção por seção.

A Godot solicita `Prefer: return=minimal` nos POSTs de comando. No gateway de run,
isso evita a cópia completa do agregado no recibo. Sem essa preferência, a
representação existente continua disponível. Não há outro executor conforme a
representação: regras, persistência e resultado são os mesmos.

## Corpo

- `version`: run, setting, sequência, steps, revisão, engineVersion e fingerprint;
- `run`: projeção de interação, sem encounters anteriores, manifesto ou regras completas;
- `combat`: read model do combate atual, ou null;
- `commands`, `capabilities`, `cardZones`, `attributes`;
- `cardPresentations`: base/origem das cartas necessárias à cena;
- `modifierItems`: inventário e compatibilidade;
- `actions`: candidatos canônicos com custos e aplicações de preview, sem traces completos;
- `evaluations`: inspeção detalhada da mão inteira não é requisito do refresh.

Somente enquanto o combate está `ACTIVE`, o mapa compacto contém o nó atual.
Em `VICTORY`, `DEFEAT`, `DRAW` ou `ABANDONED`, a sessão já publica o mapa completo,
mesmo que `activeEncounterId` ainda esteja presente aguardando confirmação do
resultado. Publicar os nós não resolve o combate nem libera viagens: os comandos
e `legalNextNodeIds` continuam sendo as únicas autoridades de progressão.
Sem um snapshot de combate ativo, a projeção também mantém o mapa completo.
Coleções leem `/cards/presentations` sob demanda. Inspeções continuam em
`/combats/{id}/cards/{instanceId}/evaluation`. Histórico e replay têm consultas
próprias, sem uma segunda autoridade.

## Concorrência e cache

A consulta captura uma referência imutável e não faz HTTP interno. Sequência
esperada incompatível retorna 409. Sem versão esperada, entrega um head inteiro.
Nunca etiqueta o head atual como uma sequência passada.

ETag inclui estado, schema e permissões; `If-None-Match` compatível retorna 304.
O schema interno do ETag é 2 para invalidar respostas que compactavam indevidamente
o mapa de combates terminados; essa mudança de representação não altera a versão
de regras, o fingerprint do estado ou os saves.
O consumidor precisa já ter aquela representação. Não tratar 304 como snapshot
vazio. A Godot atualmente solicita o corpo compacto completo.

Após POST incerto, recupere o recibo pelo mesmo commandId. Não gere outro ID nem
reenvie cegamente. Se outro cliente avançou depois do recibo, leia o novo head e
descarte a apresentação antiga sem repetir a mutação.

## Enquadramento do mapa na Godot

Ao trocar uma projeção compacta pelo mapa completo, a geometria pode mudar sem
alterar `currentNodeId`. `MapView` recentraliza o nó atual quando os retângulos ou
os limites calculados mudam, além da montagem inicial, progresso e resize.
Atualizações apenas de escolhas, recursos, texto ou abertura do inspetor flutuante
não recentralizam o mapa e preservam o arrasto manual.

Mudanças de nó usam interpolação visual de 0,28 segundo, configurável em
`MapLayoutProfile.camera_transition_seconds` e ajustada pela velocidade das
animações. O retorno de uma atividade parte do nó atual registrado na última
visita ao mapa, mesmo quando a tela foi recriada. O primeiro enquadramento,
correções de geometria sem progresso e movimento reduzido/instantâneo permanecem
imediatos. Arrastar o mapa cancela a interpolação; pausa congela seu andamento.
Isso não atrasa comandos nem altera a posição autoritativa do jogador.

Regressões: `RunInteractionProjectionTests` cobre todos os estados terminais;
`WorldOneFoundationJourneyTests` consulta a sessão antes de `RESOLVE_COMBAT` em dez
jornadas isoladas; `InteractionSessionTests` cobre cache novo e ETag antigo.
Na Godot, `map_topology_refresh.gd` cobre expansão/redução de geometria nas três
resoluções, e `first_combat_map_transition.gd` cobre a vitória com sessão inicialmente
compacta, entrada no mapa completo, conexões, confirmação pendente e câmera estável.
`map_camera_motion.gd` cobre interpolação, troca rápida de destino, refresh,
pausa, arrasto, retorno com tela recriada, movimento reduzido e descarte da tela.

## Diagnóstico

`Performance:DiagnosticsEnabled=true` habilita spans e Server-Timing até envio dos
headers; o logger registra a duração completa do pipeline. Spans aninhados não
devem ser somados. Métricas não entram em commits, hashes ou RNG.

`health/live` é liveness. `health/ready` testa disponibilidade de I/O, não um
inventário/validação de todas as partidas. `persistedRuns` pode ser null.

Exporte o OpenAPI usando host e store isolados, sem a API do jogador:

```powershell
dotnet run --project tools/ExportOpenApi -- .runtime/openapi-live.json
```

Se houver binários em uso, acrescente `--artifacts-path .runtime/openapi-export`
antes de `--` para isolar a compilação.

O export é o documento gerado pelo host, usado para diagnóstico. O contrato
publicado em `openapi/heroscript-v1.json` também contém restrições e descrições
editoriais: atualize-o de forma aditiva e valide os testes de contrato; não o
substitua cegamente pelo export.
