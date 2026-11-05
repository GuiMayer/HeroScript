# Segurança — HeroScript API

Atualizado em 2026-10-16.

O contrato vigente está em [autenticação e administração](api/authentication.md).
Este resumo substitui as instruções antigas de reload por subsistema e a
variável `HERESCRIPT_ADMIN_KEY`, que o middleware atual não lê.

## Fronteiras atuais

- A API pública de gameplay não autentica jogadores por chave administrativa.
  Ownership por ator/jogador e operação multiplayer não estão certificados.
- Administração exige `Admin:Enabled`, chave `Admin:ApiKey` enviada em
  `X-Admin-Key` e capability administrativa no perfil de ferramentas.
- Variáveis de ambiente correspondentes: `Admin__Enabled`, `Admin__ApiKey`
  e `ToolAccess__Profile`. Não inserir segredos em código, logs ou clientes.
- Administração desabilitada retorna `404`; chave ausente/incorreta retorna
  `401`; perfil sem a capability retorna `403`. Não presumir um corpo JSON
  específico para as respostas diretas do middleware.
- O estado base e Production têm administração desabilitada. O launcher da
  demo habilita ferramentas para desenvolvimento; não é configuração de jogador.

## Conteúdo, origens e exposição

As rotas antigas `/api/action/reload`, `/api/config/load`, `/api/modifiers/reload`,
`/api/gambits/reload` e `/api/status/reload` não são o contrato atual. Use os
[endpoints v1](api/README.md) e o fluxo de
[publicação/ativação de revisões](content/hot-reload-and-tool-profiles.md).

Em produção, as origens web vêm de `AllowedOrigins` na raiz da configuração
(`AllowedOrigins__0`, etc.), não de `Cors:AllowedOrigins`. CORS é uma política
de navegador, não controle de acesso para clientes nativos ou outros processos.

Mantenha a instalação local em loopback. Não exponha o serviço publicamente
com base apenas na proteção administrativa. Distribuição e hospedagem precisam
de revisão de acesso, armazenamento e segredos adequada ao produto.
Consulte [operação e limites de produção](PRODUCTION.md).
