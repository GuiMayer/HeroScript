# Autoria e publicação de conteúdo pela API

Os endpoints deste guia são administrativos e dependem da configuração de acesso da instância. Consulte [autenticação](authentication.md), [conteúdo e plataforma](content-and-platform.md) e a [governança](governance.md) antes de integrá-los. O conteúdo publicado é identificado por revisão; criar ou validar um draft não ativa automaticamente essa revisão em uma run existente.

## Compilar e validar um setting

- `GET /api/v1/admin/settings`: retorna `200` com o catálogo de settings.
- `GET /api/v1/admin/settings/{settingId}`: compila e retorna o setting. Uma compilação rejeitada retorna `422`.
- `POST /api/v1/admin/settings/{settingId}/validate`: compila e valida o bundle. Em `200` retorna `settingId`, `revision`, `validation`, `packages` e `provenance`; falhas de compilação ou validação retornam `422`.
- `POST /api/v1/admin/settings/{settingId}/drafts`: compila o setting e cria um draft. A resposta `201` inclui o draft e um cabeçalho `Location` para sua consulta. Falha de compilação ou criação retorna `422`.

Os identificadores e pacotes são definidos pelo conteúdo da instância; os exemplos de outro setting não garantem sua existência.

## Validar e publicar um draft

1. `GET /api/v1/admin/content/drafts/{draftId}` consulta o draft; `404` indica que ele não foi encontrado.
2. `POST /api/v1/admin/content/drafts/{draftId}/validate` retorna `200` com o resultado da validação. Confira o resultado em vez de interpretar apenas o status HTTP como aprovação das regras de conteúdo.
3. `POST /api/v1/admin/content/drafts/{draftId}/publish` recebe a versão esperada:

   ```json
   {"expectedVersion": 1}
   ```

   Use a versão efetivamente lida do draft; o número acima é ilustrativo. Em `200`, o retorno inclui `revision` e `manifest`. Uma versão divergente retorna `409`; outras rejeições de publicação retornam `422`. Em conflito, consulte novamente o draft antes de decidir como prosseguir.

## Recarregar arquivos

`POST /api/v1/admin/content/reload` solicita recarga do setting a partir dos arquivos da instância:

```json
{"settingId":"default"}
```

O corpo é opcional e, sem `settingId`, o endpoint usa `default`. A recarga deve estar habilitada na configuração do servidor. Pode retornar `403` quando desabilitada, `400` para identificador inválido e `422` quando rejeitada. O retorno `200` é um comprovante da operação. O comando é administrativo e deve ser tratado como alteração de conteúdo, não como uma leitura ou prévia.

Para verificar se uma revisão pode ser aplicada a uma run existente, use a prévia de ativação descrita em [atividades e desafio diário](activities-and-challenges.md); a aplicação efetiva segue o comando e as capacidades da run.

## Validar conteúdo sem criar draft

`POST /api/v1/content/validate` aceita um objeto com `bundle` ou `revision`. Quando `bundle` não é fornecido e `revision` está preenchida, a engine carrega a revisão publicada. Sem nenhum dos dois, responde `400`; revisão não encontrada responde `404`. Uma validação bem-sucedida retorna `200`, e conteúdo inválido retorna `422` com o resultado da validação. Essa operação não publica o conteúdo.
