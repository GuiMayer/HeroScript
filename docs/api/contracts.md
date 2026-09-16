# Contratos compartilhados

## Comandos autoritativos

Os gateways de run e combate recebem o mesmo envelope:

```json
{
  "commandId": "54c0c99b-21b1-4e2f-a519-0de7fdf79ece",
  "expectedSequence": 17,
  "expectedStep": 42,
  "type": "EXECUTE_ACTION",
  "payload": {}
}
```

`commandId` é a chave de idempotência. `expectedSequence` e `expectedStep`
protegem contra decisões baseadas em estado antigo. O payload canônico participa
da identidade do comando; reutilizar o mesmo ID com conteúdo diferente é erro.

Uma transição aceita retorna a sequência, o step, o hash anterior, o hash
resultante, o read model imutável e os eventos projetados. Clientes devem usar
esses valores para atualizar a UI, não reconstruir estado a partir de regras
locais.

Comandos de combate também retornam `state.resolution`. Essa resolução é a fila
ordenada de transições que a engine já calculou e persistiu para apresentação.
Ela pode ser relida, inclusive após reconexão, em
`GET /api/v1/combats/{combatId}/resolutions/{commandId}`. O cliente confirma
animações apenas no próprio estado visual; essa confirmação não altera a run.

A resolução é verificável por três hashes: estado inicial do combate, estado
final e fingerprint da fila inteira. Seus frames expõem coleções tipadas de
`effectSteps`, `calculations` e `applications`; não é necessário interpretar o
objeto livre de `payload` para descobrir a consequência de gameplay. O payload
continua disponível para metadados específicos da transição.

Um `effectStep` registra a ordem, alvo, repetição, chance, proveniência,
resultado numérico e hashes antes/depois. Uma `application` registra a mutação
concreta sem presumir que um recurso específico representa vida ou mana.

Para jogar uma carta de uma zona autorizada pela configuração, envie o UUID em
`snapshot.playableCards[].cardInstanceId` como `payload.cardId`. IDs de definição, como
`basic_attack`, descrevem conteúdo; IDs de instância identificam a cópia
específica que será movimentada pelo fluxo de zonas configurado e pode ter
upgrades próprios. Cada entrada também informa `zoneId` e `zoneOwnerId`.

## Erros

Erros usam `application/problem+json` e possuem `code` e `correlationId`.
Conflitos de comando (`409`) também incluem `currentSequence` e `currentStep`;
o cliente deve recuperar o estado e decidir novamente. Regras de gameplay
violadas retornam `422`, enquanto requests estruturalmente inválidos retornam
`400`.

`detail` é texto para diagnóstico humano. Clientes devem reagir ao status e a
`code`, jamais interpretar texto de `detail`.
