# Segurança - HeroScript API

## AdminKeyMiddleware

Endpoints administrativos que modificam configuração em runtime são protegidos por `AdminKeyMiddleware`.

### Configuração

**Via appsettings.json:**
```json
{
  "Admin": {
    "ApiKey": "your-secret-key-here"
  }
}
```

**Via variável de ambiente (recomendado para produção):**
```bash
export HERESCRIPT_ADMIN_KEY="your-secret-key-here"
```

### Uso

Inclua o header `X-Admin-Key` em requisições para endpoints protegidos:

```bash
curl -X POST http://localhost:5260/api/action/reload \
  -H "X-Admin-Key: your-secret-key-here"
```

### Endpoints Protegidos

Os seguintes endpoints requerem `X-Admin-Key`:

- `POST /api/action/reload` - Recarrega definições de ações
- `POST /api/game-resources/reload` - Recarrega definições de recursos
- `POST /api/config/load` - Carrega nova configuração
- `POST /api/modifiers/reload` - Recarrega modificadores
- `POST /api/gambits/reload` - Recarrega gambits
- `POST /api/status/reload` - Recarrega status effects

### Resposta de Erro

Requisições sem `X-Admin-Key` ou com chave inválida retornam `401 Unauthorized`:

```json
{
  "error": "Unauthorized: Invalid or missing admin key"
}
```

### Segurança em Produção

⚠️ **IMPORTANTE:**

1. **Nunca commite chaves em `appsettings.json`** - Use variáveis de ambiente
2. **Use chaves fortes** - Mínimo 32 caracteres aleatórios
3. **Rotacione chaves regularmente** - Especialmente após exposição acidental
4. **Restrinja CORS** - Configure `AllowedOrigins` para domínios específicos, não `"*"`
5. **Use HTTPS em produção** - Nunca exponha a API via HTTP

### CORS

Em desenvolvimento, CORS permite qualquer origem. Em produção, configure:

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://yourgame.com",
      "https://admin.yourgame.com"
    ]
  }
}
```

### Implementação

Ver: `src/API/Middleware/AdminKeyMiddleware.cs`
