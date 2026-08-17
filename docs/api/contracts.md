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

## Erros

Erros usam `application/problem+json` e possuem `code` e `correlationId`.
Conflitos de comando (`409`) também incluem `currentSequence` e `currentStep`;
o cliente deve recuperar o estado e decidir novamente. Regras de gameplay
violadas retornam `422`, enquanto requests estruturalmente inválidos retornam
`400`.

`detail` é texto para diagnóstico humano. Clientes devem reagir ao status e a
`code`, jamais interpretar texto de `detail`.
