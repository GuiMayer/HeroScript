# Manutenção do portal HeroScript

Portal: https://guimayer.github.io/HeroScript/.

## Responsabilidades das branches

- `main`: Markdown, contratos e exemplos canônicos; registro e dispatcher mínimos de Actions.
- `codex/docs-site`: aplicação Docusaurus, manifesto editorial, busca e pipeline de publicação.

Nunca mescle a árvore da branch do portal na main: ela deliberadamente não contém a engine. PRs de apresentação devem ter `codex/docs-site` como base. Não edite `generated/`, `build/` ou cópias de artigos.

## Desenvolvimento local

Use Node 24.14.0. No checkout desta branch, execute:

```powershell
cd site
npm ci
npm run prepare:docs
npm test
npm run typecheck
npm run build
npm run validate
npm run smoke
npm run serve
```

A importação usa exclusivamente o Git. O repositório precisa ter o histórico de main disponível (`git fetch origin main`). Em clone somente do portal, usa `origin/main`; em checkout com main local, usa essa revisão local. Em clone separado da fonte, configure `DOCS_REPOSITORY` para esse clone; `DOCS_SOURCE_SHA` aceita um commit completo pertencente à main. Nunca são lidos artigos ainda não commitados. O teste de integração regenera os arquivos: não execute build e testes simultaneamente.

## Edição e descoberta

Mude artigos na main. Um push dispara o workflow da branch dedicada com o SHA exato. Para incluir um artigo, revise `docs-manifest.json`: fonte, identidade, slug, público e categoria. Planos, GDDs e pesquisas permanecem excluídos. Para mover uma URL já publicada, adicione redirect antes da remoção. Fontes selecionadas ausentes e links internos quebrados bloqueiam a publicação.

A busca local é regenerada do mesmo commit e não indexa o repositório inteiro. OpenAPI, AsyncAPI, schemas e exemplos são downloads da mesma fonte. A cobertura da especificação REST é parcial; consulte também os guias e a governança.

## Deploy e diagnóstico

O workflow `Documentation Pages` da branch dedicada importa, testa, compila, verifica hyperlinks e executa smoke HTTP antes de publicar. PRs validam, sem deploy. Um dispatcher na main permite atualização automática sem PAT ou commit de cópias. O environment `github-pages` aceita somente `codex/docs-site`.

`site-version.json` público identifica os SHAs de fonte e portal. O rodapé também exibe versões curtas. `generated/import-report.json` registra hashes e exclusões; `generated/validation-report.json` registra a verificação do build. Falhas anteriores ao deploy preservam o artifact publicado. Builds ultrapassados são descartados pelo guard; publicação é serializada.

## Rollback explícito

Consulte um run bem-sucedido e copie seus dois SHAs completos. Eles devem pertencer ao histórico aprovado e a versão do portal deve incluir validate/smoke. Execute:

```powershell
gh workflow run docs-pages.yml --ref codex/docs-site -f source_sha=FULL_SOURCE_SHA -f site_sha=FULL_SITE_SHA -f rollback=true
```

O workflow atual valida a procedência e compila a versão escolhida. Para restaurar a versão mais recente:

```powershell
gh workflow run docs-pages.yml --ref codex/docs-site
```

Um novo push normal também retoma a fonte atual. Verifique o resultado do Actions e os dois SHAs no JSON público; um dispatch aceito sozinho não comprova publicação.

## Dependências e limites

Versões e lockfile estão fixados. Os overrides de tinypool e serialize-javascript eliminam os achados altos/críticos conhecidos na instalação usada nesta entrega. `npm audit --audit-level=high` é gate; existem avisos baixos/moderados transitivos, que devem ser reavaliados em upgrades próprios, sem forçar downgrade incompatível do Docusaurus.

O portal é estático: não hospeda a API e não executa comandos contra partidas. Links externos não são requisito de disponibilidade do build. A certificação automatizada de links e contraste/foco revisados não equivale a uma auditoria completa WCAG com leitores de tela.
