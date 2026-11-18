# Autoria local de Cartas pela API

Use um checkout ou worktree por branch, com uma instância da HeroScript e um armazenamento local de runtime por checkout. A engine lê e grava os JSONs do pacote configurado. A revisão ativa vem desses arquivos. O journal local mantém os recibos e o histórico de desfazer e refazer.

## Preparar a instância

Na raiz do checkout, escolha uma porta e um diretório de runtime exclusivos. O pacote de gravação pode ser escolhido em **Settings e pacotes → Game settings → Pacote de gravação**, e a chave administrativa é informada no terminal.

```powershell
$repo = (Get-Location).Path
$runtime = Join-Path $env:LOCALAPPDATA 'HeroScript/worktrees/cards-review'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Mods__PackageRoots__0 = Join-Path $repo 'data/configs'
$env:Content__StartupSettings__0 = 'uno-clash'
$env:Content__StartupSettings__1 = 'circular-table'
$env:Persistence__ContentStorePath = Join-Path $runtime 'content'
$env:Persistence__RunStatePath = Join-Path $runtime 'runs'
$env:Persistence__OperationalTelemetryPath = Join-Path $runtime 'telemetry'
$env:Admin__Enabled = 'true'
$env:Admin__ApiKey = Read-Host 'Chave administrativa local' -MaskInput
$env:ToolAccess__Profile = 'dev_modder'
$env:Authoring__ApplyEnabled = 'true'
dotnet run --project src/API --no-launch-profile -- --urls http://127.0.0.1:5260
```
Reutilize as mesmas opções ao reiniciar o checkout. Para outro worktree, escolha outro diretório de runtime e outra porta. Use uma única raiz de pacotes por instância gravadora. Cartas herdadas, modificadas por patches ou com arquivos compartilhados por múltiplas declarações são de leitura.

A seleção é salva no campo `authoringPackageId` do JSON do setting através de `workspace/changes`. O seletor oferece os pacotes declarados pelo setting. Settings novos recebem o pacote proprietário e a referência é incluída na composição. Settings existentes usam o primeiro pacote declarado quando não há uma escolha persistida nem uma configuração legada em `Authoring:WritablePackages`.

O [contrato OpenAPI de autoria](../../openapi/heroscript-authoring-v1.json) é disponibilizado pela instância em `GET /openapi/authoring-v1.json`. Use `GET /api/v1/admin/authoring/capabilities` para verificar se gravação, desfazer e refazer estão disponíveis.
## Validar e aplicar uma alteração

Em outro terminal, informe a mesma chave administrativa. Este exemplo acrescenta uma tag à carta `uno_red_0`, valida o lote e aplica o mesmo envelope. Em uma atualização, envie a definição fonte completa, acessível no campo `source`.

```powershell
$key = Read-Host 'Chave administrativa local' -MaskInput
$authoring = 'http://127.0.0.1:5260/api/v1/admin/authoring/settings/uno-clash'
$auth = @{ 'X-Admin-Key' = $key }
$card = Invoke-RestMethod "$authoring/definitions/cards/uno_red_0" -Headers $auth -ResponseHeadersVariable readHeaders
$card.source.tags = @($card.source.tags) + 'designer_review'
$headers = @{ 'X-Admin-Key' = $key; 'If-Match' = @($readHeaders.ETag)[0] }
$changeId = [guid]::NewGuid()
$body = @{
    changeId = $changeId
    operations = @(@{
        operation = 'update'
        kind = 'cards'
        definitionId = 'uno_red_0'
        definition = $card.source
    })
} | ConvertTo-Json -Depth 100
Invoke-RestMethod "$authoring/changes/validate" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
$receipt = Invoke-RestMethod "$authoring/changes" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
$receipt.changedFiles
$receipt.activeRevisions
```

O modo **Salvar** envia um lote ao confirmar o rascunho. O **Hot Reload** envia um lote por ação confirmada. Ambos usam `POST .../changes`. A engine valida todos os settings que consomem o pacote antes de gravar e ativar o resultado.

Para desfazer, envie um novo `changeId` e `undoOf` com o ID do lote aplicado. Para refazer, use outro `changeId` e `redoOf` com o ID do desfazer aplicado. Use `POST .../changes/validate` para a prévia, `POST .../changes` para o lote confirmado ou as rotas diretas `.../changes/{changeId}/undo` e `.../changes/{changeId}/redo`. Nas rotas diretas, o corpo contém o novo `changeId`. Sempre envie a ETag atual em `If-Match`.

O recibo expõe a elegibilidade em `canUndo` e `canRedo` e o vínculo em `compensatedBy`. Para desfazer um refazer, use o ID do próprio refazer.

| Resultado | Próxima ação |
| --- | --- |
| `412` | Releia a fonte e confira a alteração da base antes de tentar novamente. |
| `409` | Consulte o recibo e o diagnóstico. Um ID reutilizado, uma compensação já aplicada ou uma alteração no mesmo conteúdo pode exigir uma nova decisão. |
| `422` | Consulte `validation.diagnostics` e ajuste o lote. Uma carta com referência nova pode impedir o desfazer de sua criação. |
| Resposta perdida | Consulte `GET .../changes/{changeId}` ou repita o mesmo envelope com o mesmo ID e a mesma ETag. |
| Workspace em recuperação | Reinicie com a mesma raiz de pacotes e armazenamento de conteúdo. O journal conclui ou reverte a transação. Se a inicialização reportar arquivo divergente, guarde a edição externa e resolva o conflito antes de reiniciar. |

## Revisar e versionar com Git

1. Quando a API confirmar `applied`, compare `changedFiles` com `git status --short` e revise `git diff -- data/configs`. Os caminhos do recibo são relativos à raiz de pacotes. Arquivos novos também aparecem em `git status`.
2. Adicione ao commit os JSONs revisados, incluindo `package.json` quando o lote criar ou remover um artefato. Use `git diff --cached -- data/configs` para revisar os arquivos que serão enviados. O journal `.heroscript-authoring/` e os dados locais de runtime ficam fora do commit.
3. Durante um checkout, merge ou rebase que altere as fontes, interrompa as gravações de autoria. Depois da mudança de branch, inicie a engine a partir dos JSONs atualizados e obtenha uma nova ETag.
4. Valide os settings afetados no checkout do PR. A revisão pode ser reconstruída a partir dos pacotes da branch com um armazenamento vazio. Para continuar runs antigas, mantenha o armazenamento que contém os bundles históricos aos quais elas estão vinculadas.

## Medir as etapas do Hot Reload

Com `Performance__DiagnosticsEnabled=true`, o cabeçalho `Server-Timing` mostra
`authoring_validation`, `authoring_prepare` e `authoring_apply` em milissegundos.
A prévia registra somente a validação. Para registrar uma amostra de cada
setting em um processo de teste isolado, execute:

```powershell
dotnet test tests/API.Tests --filter FullyQualifiedName~AuthoredRevisionsRebuildFromSourcesAndExistingRunsKeepTheirRevision --logger 'console;verbosity=detailed'
```

O teste registra os tempos e confere o conteúdo e as revisões após reiniciar.
As medições dependem da máquina, dos arquivos e do estado dos caches.
