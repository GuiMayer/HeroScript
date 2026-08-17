# Autenticação e administração

O gameplay v1 não exige chave administrativa. Endpoints sob `/api/v1/admin`
e operações de manutenção marcadas como administrativas exigem o cabeçalho:

```http
X-Admin-Key: {{adminApiKey}}
```

Nunca inclua chaves em código de cliente, exemplos versionados, logs ou builds
distribuídos. Clientes de jogo devem receber apenas as rotas públicas de run,
combate, conteúdo e eventos.

Uma chave ausente ou inválida retorna `401` ou `403`, conforme a configuração
do servidor. Falhas administrativas devem usar `correlationId` para suporte,
não expor detalhes internos da publicação ou do filesystem.
