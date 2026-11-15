# Editor de conteúdo HeroScript

Inicie a engine conforme o [guia de autoria](../../docs/api/card-authoring.md). Na pasta deste editor, execute:

```powershell
npm ci
npm run dev
```

Abra `http://127.0.0.1:5174/editor/` e use a chave administrativa da engine. O servidor de desenvolvimento encaminha a API para `http://127.0.0.1:5260`. Defina `HEROSCRIPT_EDITOR_API` antes de iniciar o Vite se a engine usar outra porta.

Para servir pelo ASP.NET, execute `npm run build`, configure `Authoring__EditorPath` com o caminho absoluto desta pasta `dist` e inicie a engine. Abra `/editor/` na mesma origem. A chave fica na memória da aba.

Se o contrato de autoria mudar, execute `npm run generate:api` antes de compilar.
