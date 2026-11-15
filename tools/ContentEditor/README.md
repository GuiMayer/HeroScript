# Editor de conteúdo HeroScript

No Windows, execute [Start-ContentEditor.bat](../../Start-ContentEditor.bat) na raiz do projeto.
O launcher precisa do Node.js e do SDK .NET do repositório. Ele prepara o
editor, inicia a engine local e abre `http://127.0.0.1:5279/editor/` já conectado.
O link de abertura é descartável. Se precisar reconectar manualmente, use a
chave administrativa exibida no terminal. Mantenha a janela aberta enquanto
estiver editando e pressione `Ctrl+C` para encerrar.
Se a porta estiver ocupada, execute `Start-ContentEditor.bat -ApiPort 5280`.

## Desenvolvimento

Inicie a engine conforme o [guia de autoria](../../docs/api/card-authoring.md). Na pasta deste editor, execute:

```powershell
npm ci
npm run dev
```

Abra `http://127.0.0.1:5174/editor/` e use a chave administrativa da engine. O servidor de desenvolvimento encaminha a API para `http://127.0.0.1:5260`. Defina `HEROSCRIPT_EDITOR_API` antes de iniciar o Vite se a engine usar outra porta.

Para servir pelo ASP.NET, execute `npm run build`, configure `Authoring__EditorPath` com o caminho absoluto desta pasta `dist` e inicie a engine. Abra `/editor/` na mesma origem. A chave fica na memória da aba.

Se o contrato de autoria mudar, execute `npm run generate:api` antes de compilar.

## Cartas

Escolha o game setting na barra superior. O catálogo consulta a engine
e permite buscar por nome ou ID. Abra uma carta para editar seus dados e
tags em **Visão geral**, condições e custos em **Regras**, a definição
completa em **Avançado** ou conferir a **Prévia** visual.

No modo manual, use **Validar** para consultar a engine sem gravar e **Salvar**
para aplicar o lote. Em **Hot Reload**, cada ação concluída é enviada
imediatamente. Campos de texto confirmam a edição ao sair do campo.
`Ctrl+Z` e `Ctrl+Shift+Z` desfazem e refazem ações do editor. O modo manual
prepara compensações de ações já salvas para o próximo **Salvar**.

Os rascunhos e o histórico do editor ficam na aba e são separados por setting.
Se a engine reportar conflito, o rascunho fica disponível. Atualize a base
quando os campos forem independentes ou compare o JSON atual em **Avançado**.
Outras áreas aparecem na navegação com um aviso de implementação pendente.

Use Node.js 24 para `npm test` e `npm run build`. A CI confere a geração
dos tipos da API, o histórico local e a compilação do editor.
