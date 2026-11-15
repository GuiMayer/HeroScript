# Contratos OpenAPI publicados

`heroscript-v1.json` é a cópia revisável do contrato público inicial.
`/openapi/v1.json` é o inventário gerado a partir das rotas da engine em execução.

A especificação estática pública é focada na superfície autoritativa v1.
As rotas administrativas são classificadas à parte na documentação. A CI
valida a estrutura do arquivo e a presença das rotas críticas, e os testes
de integração validam o comportamento real.

## Autoria de Cartas

`heroscript-authoring-v1.json` é gerado pelo mesmo servidor de teste que
expõe `/openapi/authoring-v1.json`. Esse documento contém a superfície
administrativa de autoria. O inventário gerado em `/openapi/v1.json`
continua incluindo essas rotas junto com as demais operações da engine.

Para regenerar o contrato em um checkout, execute na raiz:

```powershell
dotnet run --project tools/ExportOpenApi -c Release -- --authoring openapi/heroscript-authoring-v1.json
```

A CI executa a mesma geração com `--authoring --check` para detectar adições,
remoções e alterações de contrato. O modo de verificação compara o conteúdo
JSON, sem regravar o arquivo. Inclua o snapshot e o changelog nas alterações
de contrato feitas em um PR.
