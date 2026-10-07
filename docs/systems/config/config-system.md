# Configuração, packages e settings

## Modelo atual

Gameplay é carregado como conteúdo publicado, não como configuração global
consultada durante uma ação. Um `setting` escolhe packages data-only; o
compilador resolve dependências e patches, valida o grafo semântico inteiro e
produz um `ContentBundle`. A publicação desse bundle gera uma revisão imutável.

```text
package.json + arquivos JSON
          ↓ descoberta e validação
setting.json → ordem determinística dos packages
          ↓ compilação
ContentBundle → ContentManifest/revision
          ↓ início da run
RunState fixa settingId + contentRevision + engineVersion
```

As regras nunca dependem da pasta atual, ordem de enumeração do filesystem ou
de um “config ativo” mutável. Loaders de autoria existem apenas antes da
publicação. Durante gameplay, serviços consultam o catálogo fixado pela revisão
da run.

## Limites

- `data/configs` é uma fonte de packages do host, não estado de gameplay.
- `appsettings.json` configura infraestrutura (armazenamento, limites de host,
  telemetria), nunca dano, fases ou conteúdo.
- uma definição é identificada por `kind + artifactPath/definitionId`;
- colisões, dependências ausentes, ranges incompatíveis, paths ambíguos e
  propriedades desconhecidas falham antes da publicação;
- hot reload compila e publica outra revisão. Uma run só muda de revisão por
  `ACTIVATE_CONTENT_REVISION`, quando a política do modo permite.

## API

- `GET /api/v1/content/settings` lista settings jogáveis, suas revisões atuais e
  os IDs canônicos necessários para iniciar uma run;
- `GET /api/v1/content/revisions` lista revisões publicadas e a atual do setting;
- `GET /api/v1/content/revisions/{revision}` retorna o manifest imutável;
- `GET /api/v1/content/{kind}` e `/{definitionId}` publicam definições de uma
  revisão;
- endpoints administrativos criam drafts, validam e publicam atomicamente;
- entidades usam o mesmo catálogo `content/entities`; não existe catálogo CRUD
  paralelo.

O guia de autoria, manifests, patch e proveniência está em
[packages-and-settings.md](../../content/packages-and-settings.md).
