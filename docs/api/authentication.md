# Autenticação e administração

O gameplay v1 não exige chave administrativa. Endpoints sob `/api/v1/admin`
e operações de manutenção marcadas como administrativas exigem o cabeçalho:

```http
X-Admin-Key: {{adminApiKey}}
```

Nunca inclua chaves em código de cliente, exemplos versionados, logs ou builds
distribuídos. Clientes de jogo devem receber apenas as rotas públicas de run,
combate, conteúdo e eventos.

## Configuração e respostas

O `AdminKeyMiddleware` consulta `Admin:Enabled` e `Admin:ApiKey`. As variáveis
de ambiente correspondentes são `Admin__Enabled` e `Admin__ApiKey`, não
`HERESCRIPT_ADMIN_KEY`. A operação também exige a capability `admin.operations`
no perfil configurado em `ToolAccess:Profile`; habilitação e chave não dispensam
esse terceiro controle. Consulte os [perfis de ferramentas](../content/hot-reload-and-tool-profiles.md).

Para endpoints marcados como administrativos, o middleware avalia nesta ordem:

| Condição | Resposta |
| --- | --- |
| `Admin:Enabled=false` | `404` |
| Chave configurada ausente ou cabeçalho ausente/incorreto | `401` |
| Chave válida, mas perfil sem capability administrativa | `403` |

Essas respostas são emitidas diretamente pelo middleware; não se deve exigir
um corpo JSON específico. Os códigos acima não substituem erros de validação
ou regras que o endpoint possa retornar depois de autorizado.

O gameplay v1 não implementa autenticação/ownership de jogador através dessa
chave. Hospedar a API publicamente exige uma fronteira adicional de acesso;
CORS e IDs de run não a substituem. Veja [segurança](../security.md).
