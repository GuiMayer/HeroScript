# Referência de composição de conteúdo

O formato público de extensão é o package manifest. Cada entrada de `content`
declara explicitamente `operation: Definition` ou `operation: Patch`. Não há
inferência baseada no nome do arquivo.

## Definition

Publica uma nova definição a partir de `sourcePath` e exige `kind` e
`artifactPath` ou `definitionId` compatíveis com o registro de conteúdo.

```json
{
  "schemaVersion": 1,
  "operation": "Definition",
  "sourcePath": "Resources/cards/frost_bolt.json",
  "kind": "cards",
  "artifactPath": "cards/frost_bolt.json"
}
```

## Patch

Altera um artefato de package explicitamente identificado. `expectedHash` é
obrigatório e contém o SHA-256 canônico do alvo imediatamente antes do patch.
`Merge` combina objetos e substitui listas alteradas por inteiro. `Replace`
substitui o payload inteiro. O compilador ordena os pacotes por dependências
e ID e aplica as entradas de cada pacote na ordem declarada no manifesto.

```json
{
  "schemaVersion": 1,
  "operation": "Patch",
  "sourcePath": "patches/frost_bolt.json",
  "target": {
    "packageId": "example.base",
    "kind": "cards",
    "artifactPath": "cards/frost_bolt.json"
  },
  "expectedHash": "<sha256-do-artefato-alvo>",
  "patchStrategy": "Merge"
}
```

O compilador registra proveniência por JSON Pointer, package, versão, arquivo e
operação. O subsistema `Core.Config.Delta` continua restrito a fontes internas
que ainda o utilizam; ele não é o contrato de mods e não deve ser introduzido
em novos packages.
