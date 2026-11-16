# Editor de conteúdo HeroScript

No Windows, execute [Start-ContentEditor.bat](../../Start-ContentEditor.bat) na raiz do projeto.
O launcher precisa do Node.js e do SDK .NET do repositório. Ele prepara o
editor, inicia a engine local e abre `http://127.0.0.1:5279/editor/` já conectado.
O link de abertura é descartável. Se precisar reconectar manualmente, use a
chave administrativa exibida no terminal. Mantenha a janela aberta enquanto
estiver editando. Para encerrar a engine, feche a janela ou pressione `Ctrl+C`.
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
tags em **Visão geral**, condições e custos em **Regras** e a definição
completa em **Avançado**.

No modo manual, use **Validar** para consultar a engine sem gravar e **Salvar**
para aplicar o lote. Em **Hot Reload**, cada ação concluída é enviada
imediatamente. Campos de texto confirmam a edição ao sair do campo.
`Ctrl+Z` e `Ctrl+Shift+Z` desfazem e refazem ações do editor. O modo manual
prepara compensações de ações já salvas para o próximo **Salvar**.

Os rascunhos e o histórico do editor ficam na aba e são separados por setting.
Se a engine reportar conflito, o rascunho fica disponível. Atualize a base
quando os campos forem independentes ou compare o JSON atual em **Avançado**.
Áreas ainda em construção aparecem na navegação com um aviso.

## Regras e fluxos

Escolha o tipo no catálogo. A seção inclui regras de combate e fluxo,
sequências de fases, modos de jogo e políticas de permissões, vínculo de
conteúdo, replay e linha do tempo. **Campos** organiza os valores editáveis
segundo o schema da engine. Abra os grupos para consultar configurações
aninhadas. Use **Avançado** para estruturas sem controle guiado, o JSON
compilado e a proveniência.

As sugestões de referência vêm do setting ativo. O botão de criação ao lado
de uma referência abre a categoria correspondente quando ela está habilitada.
No modo manual, volte à definição original e escolha a nova referência para
enviar as duas mudanças em um lote. As mudanças em **Cartas** e **Regras e fluxos**
compartilham o rascunho do setting e a validação da engine.

Use Node.js 24 para `npm test` e `npm run build`. A CI confere a geração
dos tipos da API, o histórico local e a compilação do editor.
