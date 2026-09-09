# Packages e settings

## Conceitos

Um **package** é uma coleção versionada e data-only de definições e patches. Um
**setting** é a receita ordenada de packages que forma um jogo. A compilação
produz um bundle validado; a publicação produz uma revisão imutável usada por
runs.

## Estrutura mínima

```text
my-package/
├── package.json
├── settings/
│   └── my-game.json
├── Resources/
│   ├── cards/
│   ├── entities/
│   ├── modes/
│   └── ...
└── patches/
```

`package.json`:

```json
{
  "schemaVersion": 1,
  "packageId": "studio.my-game",
  "version": "1.0.0",
  "dataOnly": true,
  "dependencies": [],
  "conflicts": [],
  "content": [
    {
      "schemaVersion": 1,
      "operation": "Definition",
      "sourcePath": "Resources/cards/strike.json",
      "kind": "cards",
      "artifactPath": "cards/strike.json"
    }
  ]
}
```

`settings/my-game.json`:

```json
{
  "schemaVersion": 1,
  "settingId": "my-game",
  "packages": [
    { "packageId": "studio.my-game", "versionRange": "1.0.0" }
  ]
}
```

## Compilação determinística

O compilador normaliza IDs e paths, resolve versões/dependências, rejeita
conflitos e ordena os packages antes de ler os envelopes. Cada artefato recebe
hash canônico. Patches exigem alvo explícito e podem exigir seu hash anterior.
O resultado registra proveniência por campo.

Dois hosts com os mesmos bytes de packages e o mesmo setting devem produzir o
mesmo bundle e a mesma revisão, independentemente da ordem de arquivos.

## Publicação e hot reload

Publicar é atômico: falha de schema ou referência deixa a revisão anterior
ativa. Hot reload não muta bundles nem runs. Ele publica outra revisão; uma run
de desenvolvimento só a adota por comando versionado quando seu game mode
permite. Runs publicadas permanecem fixas.

## Boas práticas

- prefira novas definições a patches amplos;
- use `expectedHash` em patches distribuídos;
- mantenha regras no JSON e apresentação fora do package;
- não leia packages diretamente durante gameplay;
- teste o setting completo, não apenas cada arquivo isolado;
- consulte definições via `/api/v1/content/{kind}` com a revisão desejada.

O package base de referência está em `data/configs/default`.
