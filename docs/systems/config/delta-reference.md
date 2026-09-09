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

Altera um artefato de package explicitamente identificado. `expectedHash` pode
fixar a versão exata da entrada alvo. `Merge` combina objetos; `Replace`
substitui o payload inteiro. Ordem é a ordem de dependências e depois a ordem do
setting, ambas validadas e determinísticas.

```json
{
  "schemaVersion": 1,
  "operation": "Patch",
  "sourcePath": "patches/frost_bolt.json",
  "kind": "cards",
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
