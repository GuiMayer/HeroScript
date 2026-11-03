# Portal de documentação — Docusaurus e GitHub Pages

Data: 2026-11-03. Estado: plano; nenhuma infraestrutura de publicação foi criada.

## 1. Objetivo e limites

Publicar documentação navegável para dois públicos:

1. **Consumidores da API:** quem integra Godot, Unity, web, bots ou ferramentas por REST, sem alterar a implementação da engine.
2. **Desenvolvedores:** quem cria conteúdo/settings em JSON, estende a engine, contribui com código ou opera o servidor.

Uma terceira área reúne referências de máquina e glossário. As trilhas compartilham hyperlinks, mas não obrigam o integrador a estudar internals para enviar seu primeiro comando.

O portal reutiliza os Markdown atuais. Não altera gameplay, contratos HTTP, determinismo ou persistência. Planos internos, GDDs, pesquisas e capturas de desenvolvimento não serão publicados automaticamente como documentação de uso.

Este documento não autoriza executar agora os passos de implementação/publicação. Não cria branch, instala dependências, altera configurações do GitHub ou faz deploy.

## 2. Diagnóstico do repositório

Verificação local e consulta somente leitura ao GitHub:

| Item | Situação observada |
| --- | --- |
| Repositório | [GuiMayer/HeroScript](https://github.com/GuiMayer/HeroScript), público |
| Branch padrão | `main` |
| Pages | API do GitHub retornou `has_pages: false`; ainda precisa ser habilitado |
| Site Docusaurus | Não encontrado na árvore analisada |
| Workflows | `.github/workflows/ci.yml` e `runtime-release.yml`; preservar suas responsabilidades |
| Markdown em `docs` | 248 arquivos na árvore de trabalho, incluindo alterações pendentes; não confundir esse inventário com o conteúdo já publicado em Git |
| Principais grupos | 18 documentos API, 21 de conteúdo, 23 de arquitetura, 28 de sistemas e 9 guias |
| Material editorial interno | 68 planos, 33 documentos de design e 20 de roadmap, além de pesquisas/GDDs |
| Contrato REST | [OpenAPI versionado](../../openapi/heroscript-v1.json), aproximadamente 200 KB |
| Eventos | [AsyncAPI](../../asyncapi/heroscript-events-v1.json) e [explicação](../../asyncapi/README.md) |
| Exemplos de requests | [`examples/http`](../../examples/http/deterministic-run.http), com cinco arquivos de jornadas |

Fontes úteis já existentes: [índice API](../api/README.md), [integração de clientes](../CLIENT_INTEGRATION.md), [conteúdo](../content/packages-and-settings.md), [arquitetura](../architecture/overview.md) e [sistemas](../systems/combat/combat-system.md).

Há revisão editorial a fazer antes de importar. Por exemplo, o README menciona engine 32, enquanto `DeterministicContext.CurrentEngineVersion` e o changelog consultados indicam 34. O índice de `docs` conserva metadados antigos e mistura instruções atuais com histórico. A sincronização de arquivos não resolve divergências semânticas desse tipo.

## 3. Decisões de arquitetura

### 3.1. Branch dedicada e fonte única

Usar **`codex/docs-site`** como branch permanente da aplicação de documentação.

| Local | Responsabilidade | Editado por humanos? |
| --- | --- | --- |
| `main`: `docs/**` | Conteúdo técnico canônico, próximo ao código que documenta | Sim |
| `main`: `openapi/**`, `asyncapi/**`, exemplos autorizados | Contratos e exemplos canônicos | Sim/geradores já existentes |
| `main`: dois pequenos workflows de integração | Notificar publicação e registrar o workflow despachável | Sim |
| `codex/docs-site`: aplicação Docusaurus | Tema, componentes, navegação, manifestos e ferramentas do site | Sim |
| `codex/docs-site`: páginas de entrada | Apresentação do portal e orientação das trilhas; sem duplicar contratos | Sim |
| Build: diretório `generated/` | Markdown importado, metadados, referências e assets derivados | Não |
| Artifact do Pages | HTML/CSS/JS compilados | Não |

**Sincronização escolhida:** a branch do site importa os Markdown de um **SHA explícito da `main` durante o build**. As cópias resultantes não são commitadas e não são editáveis. Isso mantém os arquivos sincronizados sem criar uma segunda fonte de verdade.

Não fazer merges periódicos da árvore inteira da engine para a branch do site. Não devolver arquivos gerados para `main`. Não criar outra branch `gh-pages` para HTML: o deploy será por artifact do GitHub Actions.

A árvore final da branch dedicada deve conter apenas o portal e sua automação; não deve carregar `src`, builds, saves ou o projeto completo da Godot. Prepará-la em checkout/worktree isolado, preservando a árvore de trabalho atual e suas alterações pendentes. Não usar remoção/reset no checkout da engine para montar o site.

### 3.2. Por que manter pequenos workflows na main

Um push em `main` não dispara, sozinho, um workflow que exista apenas em outra branch. Além disso, o GitHub exige que um workflow despachável esteja registrado na branch padrão para receber `workflow_dispatch`. A execução pode ser direcionada à branch do site com `--ref` [G3].

Portanto, `main` terá somente:

- `docs-source.yml`: detecta mudanças publicáveis e despacha a publicação.
- `docs-pages.yml`: registro mínimo de `workflow_dispatch`; executar acidentalmente esse arquivo em `main` deve falhar com instrução para escolher `codex/docs-site`.

Na branch `codex/docs-site`, o mesmo caminho `docs-pages.yml` contém o workflow real de validação/build/deploy. O dispatch seleciona explicitamente essa branch. O site, dependências Node, tema, scripts e sidebars continuam separados da engine.

Não depender de um push feito por `GITHUB_TOKEN` gerar outro workflow: esse encadeamento não acontece em geral. O dispatch explícito é uma exceção suportada [G2]. Não é necessário PAT para a proposta inicial.

### 3.3. Plataforma estática

Usar Docusaurus estável, TypeScript, preset clássico, sem blog inicialmente. Fixar versões compatíveis e lockfile na implementação; a documentação oficial consultada apresenta a linha 3.10.2 [D1]. Selecionar Node LTS suportado, fixado em arquivo de versão, `engines` e CI; não usar `latest` no build recorrente.

Configuração prevista:

```ts
url: 'https://guimayer.github.io',
baseUrl: '/HeroScript/',
organizationName: 'GuiMayer',
projectName: 'HeroScript',
trailingSlash: true,
onBrokenLinks: 'throw',
markdown: {
  format: 'detect',
  mermaid: true,
  hooks: {
    onBrokenMarkdownLinks: 'throw',
    onBrokenMarkdownImages: 'throw',
  },
},
```

Validar as opções contra a versão fixada; o trecho é direcionamento de configuração, não arquivo pronto. A URL esperada é **`https://guimayer.github.io/HeroScript/`**, condicionada à ativação do Pages. `baseUrl` deve respeitar o nome do repositório [D1].

Usar `.md` como CommonMark e reservar `.mdx` para páginas próprias com componentes. Isso reduz incompatibilidades com generics de C#, `<...>`, chaves JSON e textos existentes. O modo CommonMark possui limitações documentadas e deve passar pela prova de importação antes da migração completa [D2].

## 4. Arquitetura da informação

### 4.1. Navegação principal

Barra superior: **Início · Consumir a API · Desenvolver · Referências · GitHub**.

Página inicial com duas chamadas claras:

- **Quero integrar um cliente:** instalação mínima → primeira run → comando → recuperar estado.
- **Quero desenvolver com a engine:** autorar JSON/settings → compreender sistemas → contribuir/operar.

Usar um plugin de documentação com três sidebars manuais e IDs estáveis: `apiSidebar`, `developersSidebar`, `referenceSidebar`. Cada artigo possui uma localização canônica; as outras trilhas apontam para ele por hyperlink, sem cópia de conteúdo.

### 4.2. Trilha: consumidores da API

| Seção lógica | Conteúdo e fontes iniciais |
| --- | --- |
| Começar | [Quickstart](../api/getting-started.md), [integração](../CLIENT_INTEGRATION.md), visão headless, executar servidor e primeira jornada |
| Contrato de comunicação | [Governança](../api/governance.md), [comandos/erros](../api/contracts.md), IDs, idempotência, `expectedSequence`, `expectedStep`, 409/422 e recuperação de timeout |
| Loop de gameplay | [Runs/combate](../api/runs-and-combat.md), [sessão compacta](../api/interactive-session.md), mapa, atividades, ações legais e diferença entre consulta/preview/commit |
| Cartas e build | [Composição](../api/deck-loadouts.md), [compartimentos](../api/deck-compartments.md), [pagamentos](../api/card-payments.md), [inscrições](../api/inscription-loadouts.md), [suporte](../api/support-loadouts.md), [recompensas](../api/reward-decisions.md) |
| Histórico e ferramentas | [Sandbox](../api/combat-sandbox.md), replay, timeline, branches, simulações, capacidades e limitações por modo |
| Conteúdo e eventos | [Conteúdo publicado](../api/content-and-platform.md), revisões/settings, [eventos/SSE](../api/events.md) e reconexão |
| Integrações | Guias de Godot reutilizáveis, separação rede/apresentação, animações por fatos confirmados e exemplos `.http` |
| Operação para integradores | Autenticação aplicável, health/readiness, diagnósticos de integração e links para operação avançada |
| Mudanças | [Changelog](../api/changelog.md), estabilidade, experimental e planned |

Endpoints administrativos não aparecem misturados ao caminho de gameplay. Recursos `planned` devem ser identificados como indisponíveis, sem botões/exemplos que prometam execução. Não atribuir estabilidade nova apenas porque uma página foi publicada.

### 4.3. Trilha: desenvolvedores

Subdividir a entrada em três percursos, dentro da mesma área:

| Percurso | Seções e fontes |
| --- | --- |
| Criar conteúdo/settings/mods | [Packages/settings](../content/packages-and-settings.md), publicação/revisões, [hot reload e perfis](../content/hot-reload-and-tool-profiles.md), contratos JSON e exemplos mínimos |
| Autorar gameplay | Cartas como containers, composição, zonas, recursos, lifecycle, custos, efeitos, cálculo/buckets, crítico, stacks/condensação, status, inscrições, relíquias, modificadores, respostas e diálogos |
| Evoluir a engine | [Arquitetura](../architecture/overview.md), [determinismo](../architecture/deterministic-runs.md), [autoridades transversais](../architecture/cross-cutting-systems.md), comandos/transações, atores/IA, turnos e decisões arquiteturais |
| Testar e contribuir | Build/testes, testes de integração, autoria de uma extensão, critérios de imutabilidade/replay, contribuição com documentação e revisão de contratos |
| Operar e distribuir | [Produção](../PRODUCTION.md), [segurança](../security.md), [persistência](../persistence.md), [observabilidade](../observability.md), [distribuição](../distribution.md), configuração e troubleshooting |

Os documentos de `docs/systems/**` devem ser selecionados pelo assunto, não simplesmente despejados como uma árvore de nomes de arquivos. Separar explicação de autoria JSON de detalhes internos de implementação quando um artigo misturar os dois.

### 4.4. Referências compartilhadas

- Referência REST gerada a partir do OpenAPI versionado, com download do JSON original.
- Contrato AsyncAPI e guia SSE; não anunciar transporte que a engine não implemente.
- Schemas JSON autorizados e exemplos de payload/configuração.
- Glossário: run, setting, package, revisão, seed, comando, recibo, step, sequence, ator, recurso, componente, zona, módulo, inscrição, modificador, replay e branch.
- Matriz de estabilidade e links para governança/changelog.
- Exemplos de jornadas HTTP com valores fictícios e placeholders explícitos.

Na primeira versão, gerar páginas de referência estáticas por grupo/operação/schema. Não depender de serviço externo para renderizar contratos. Resolver `$ref` com links e proteção contra ciclos; não expandir schemas recursivos indefinidamente nem inventar exemplos ausentes da especificação.

Não habilitar execução de requests contra um servidor de demonstração público. O Pages hospeda documentação, não a engine, saves, chaves administrativas ou um proxy REST. Explicar limites de CORS/HTTPS para quem quiser consumir sua própria API pelo navegador.

### 4.5. Política editorial

Português como idioma editorial inicial, preservando termos/identificadores do contrato e trechos em inglês existentes até revisão. O portal não deve fingir ter tradução completa. Preparar configuração para inglês futuro, sem duplicar agora todos os documentos.

Não publicar `docs/plans`, GDDs, pesquisas e relatórios de UI por inclusão recursiva. Quando necessários, oferecer links para os arquivos no GitHub, classificados como histórico/referência interna. Um relatório técnico atual pode ser selecionado individualmente, com data, escopo e limitações.

## 5. Hyperlinks, Markdown e assets

### 5.1. Manifesto editorial

Criar `site/docs-manifest.json` com entrada explícita para cada artigo publicado:

```json
{
  "source": "docs/api/contracts.md",
  "docId": "api/contracts",
  "slug": "/api/contracts/",
  "section": "apiSidebar",
  "category": "Contrato de comunicação",
  "title": "Comandos, concorrência e erros"
}
```

O manifesto governa título, categoria, ordem, slug, status editorial e redirecionamentos. A implementação deve rejeitar fontes ausentes, IDs/slugs repetidos, travessia de diretórios e colisão entre páginas próprias e importadas.

Preservar dentro de `generated/docs` a estrutura relativa do material importado. Injetar front matter no destino quando necessário, sem sobrescrever metadados da origem silenciosamente e sem modificar arquivos canônicos. Páginas próprias de entrada usam namespace separado, por exemplo `_portal`.

### 5.2. Resolução de links

Processar links por AST Markdown, preservando blocos de código, exemplos e texto:

1. Destino importado: apontar para o documento/âncora canônicos, mesmo entre sidebars.
2. Código, testes ou material interno excluído: link para `github.com/GuiMayer/HeroScript/blob/<source_sha>/...`.
3. Schema/contrato ou exemplo baixável: asset estático copiado do mesmo SHA, com hyperlink de origem.
4. Imagem autorizada: copiar com caminho estável e preservar texto alternativo.
5. Caminho absoluto local (`C:\\...`, `file://`), arquivo ausente ou âncora inválida: erro de publicação a resolver editorialmente; não remover silenciosamente.

Cada artigo deve ter breadcrumbs, sumário, anterior/próximo dentro da trilha, link “Editar fonte” e pelo menos um “Veja também” pertinente. Exemplo: pagamentos → zonas → custos JSON → idempotência, sem copiar a explicação entre essas páginas.

O link de edição de um artigo importado aponta à `main`; “Ver fonte desta publicação” aponta ao SHA imutável. Páginas próprias apontam à branch do site. Não enviar correções técnicas ao diretório gerado.

Toda URL interna deve respeitar `/HeroScript/`; não espalhar concatenações manuais do prefixo. Testar navegação, imagens, downloads, links entre seções e recarga direta de páginas profundas.

### 5.3. Compatibilidade e publicação segura

- Validar CommonMark, tabelas, Unicode, blocos C#/JSON/HTTP, Mermaid e anchors existentes.
- Autorizar assets por manifesto/dependências; não copiar toda a pasta de capturas de design.
- Não usar substituições globais de `<`, `>`, `{` e `}` para fazer Markdown compilar.
- Tratar MDX e scripts do portal como código revisado; não executar arquivos trazidos da árvore da engine durante a importação.
- Bloquear links executáveis/HTML não autorizado e não importar arquivos fora do checkout fixado.
- Falhas de links/anchors/contrato derrubam o build; não configurar um `ignore` geral para publicar conteúdo quebrado.

## 6. Sincronização por commit

### 6.1. Estrutura prevista na branch do site

```text
.github/workflows/docs-pages.yml       # validação, build e deploy reais
site/
  package.json
  package-lock.json
  docusaurus.config.ts
  sidebars.ts
  docs-manifest.json
  source-policy.json                  # repositório, branch e fontes autorizadas
  docs/                               # somente páginas próprias do portal
  src/                                # home, CSS e componentes necessários
  static/                             # assets próprios, sem conteúdo da engine
  scripts/
    import-docs.mjs
    generate-reference.mjs
    validate-docs.mjs
  generated/                          # ignorado pelo Git
    docs/
    static/
    import-report.json
  build/                              # ignorado pelo Git
  README.md                           # desenvolvimento/publicação do portal
```

Também ignorar `node_modules`, `.docusaurus` e checkout temporário da fonte. A raiz da branch não contém uma cópia da engine.

### 6.2. Build reproduzível

Cada publicação fixa dois valores:

- `site_sha`: commit da branch `codex/docs-site` usado para tema/scripts/navegação.
- `source_sha`: commit da `main` usado para Markdown, contratos e exemplos.

Fluxo:

1. Resolver e validar o SHA da fonte; o padrão é o HEAD atual de `main`.
2. Fazer checkout separado da fonte, em modo detached/read-only para o importador.
3. Validar que o commit pertence à linhagem autorizada de `main`; não aceitar repositório/ref arbitrário por input.
4. Limpar somente o diretório gerado dentro do workspace do site, com limite de caminho validado.
5. Importar o manifesto, assets dependentes e contratos; gerar referências.
6. Registrar SHAs, hashes dos arquivos, páginas incluídas/excluídas e avisos no relatório de importação.
7. Validar e compilar; incluir `site-version.json` no artifact com os dois SHAs e o build ID.

Uma mesma combinação de fonte/site/lockfile deve produzir os mesmos documentos e rotas. Timestamp operacional não entra no conteúdo técnico nem exige commits automáticos.

### 6.3. Fluxo automático e manutenção

| Mudança | Onde editar | Como chega ao site |
| --- | --- | --- |
| Explicação de endpoint/sistema | Markdown canônico na `main` | Push aprovado dispara build com seu SHA |
| Novo artigo | Markdown na `main` + manifesto na branch do site | A fonte só é publicada após inclusão editorial validada |
| Arquivo removido/renomeado | `main`, com ajuste de links e manifesto | Build falha de forma explícita até a migração do caminho |
| Tema/navegação/página inicial | PR para `codex/docs-site` | Push nessa branch reconstrói com a fonte atual |
| Correção urgente de documento | PR/commit na `main` | Mesmo fluxo automático; sem editar HTML publicado |
| Refazer publicação após falha | Dispatch manual | Reusa SHAs explícitos, sem cherry-pick de Markdown |

Alterar um artigo já incluído não exige tocar na branch do site. Um artigo novo não deve entrar na navegação de consumidores só porque apareceu num diretório.

O CI deve emitir também uma lista de Markdown novos ainda não classificados. Eles podem permanecer fora do portal com justificativa editorial; fontes já publicadas desaparecidas são erro. Nenhum aviso desse inventário deve transformar planos internos em páginas públicas automaticamente.

Sincronização copia o que foi commitado, nunca arquivos locais não commitados. Alteração de código sem atualização dos textos não gera documentação correta por mágica: manter a revisão de impacto documental nos PRs e os checks de contrato/changelog já existentes.

## 7. Automação e deploy

### 7.1. Acionamento na main

Após os checks exigidos para a fonte, `docs-source.yml` despacha:

```text
gh workflow run docs-pages.yml
  --repo GuiMayer/HeroScript
  --ref codex/docs-site
  -f source_sha=<SHA aprovado>
```

O comando é esquemático, não deve ser copiado como linhas independentes no shell. No workflow, usar input estruturado/variável de ambiente e validar o SHA, sem interpolar texto de PR em comandos.

Usar **um workflow dedicado de `push` em `main`**, com os checks leves de fontes/contratos necessários e o dispatch. Não adicionar simultaneamente um segundo disparo por `workflow_run`/CI: isso duplicaria publicações. Manter o CI .NET existente e seus checks de contrato/changelog independentes. O build completo do portal e seus gates acontecem na branch do site, antes de qualquer deploy.

O dispatcher só publica eventos de push da própria `main`, nunca PRs nem código de forks. Uma validação de documentação em PR para `main` pode usar o importador confiável da branch do site com permissões somente leitura, mas não despacha publicação. Não executar scripts do checkout do PR.

Disparar para alterações em documentação, contratos, exemplos autorizados e arquivos que mudem versões públicas. Mudanças de código relevantes à API também devem passar pela avaliação documental. O primeiro bootstrap usa dispatch manual após a configuração do Pages. O portal inicial documenta o desenvolvimento atual, não uma release estável: sua publicação não deve exigir rodar toda a suíte da engine, nem impedir uma correção textual porque um teste longo do jogo falhou. Isso não dispensa os checks dedicados do portal nem a revisão técnica dos textos.

O dispatcher precisa de `actions: write` para iniciar o workflow e `contents: read` para validar refs; não precisa escrever em branches. Verificar as políticas do repositório para `GITHUB_TOKEN`, sem elevar permissões globais por conveniência.

### 7.2. Workflow da branch dedicada

Eventos:

- `push` em `codex/docs-site`: reconstruir o portal com a fonte atual de `main`.
- `pull_request` com base em `codex/docs-site`: validar, sem deploy.
- `workflow_dispatch`: receber `source_sha` opcional, com resolução/validação explícitas.

Jobs:

1. **Resolve:** fixar SHAs e validar política da fonte.
2. **Build:** checkouts separados, instalar Node fixado, `npm ci`, importar, gerar referência, typecheck, validar links e `npm run build`.
3. **Smoke:** servir o build sob `/HeroScript/`, navegar rotas/downloads e validar páginas profundas.
4. **Upload:** `actions/upload-pages-artifact`, apontando ao `site/build` validado.
5. **Deploy:** dependente de build/smoke, `actions/deploy-pages`, environment `github-pages` e URL como output.

Usar ações oficiais `configure-pages`, `upload-pages-artifact` e `deploy-pages` [G1]. Fixar as revisões efetivamente validadas, preferencialmente por SHA; não presumir que números de versão de exemplos antigos continuam atuais.

Permissões separadas por job: build com `contents: read`; deploy com `pages: write` e `id-token: write`, além da leitura necessária. Não usar `pull_request_target` para compilar contribuições com privilégios. PRs não recebem deploy nem segredos.

### 7.3. Concorrência e rollback

- Cancelar builds supersedidos quando apropriado; serializar publicações num grupo único de Pages.
- Antes do deploy automático, confirmar que `site_sha` e a fonte pretendida não foram supersedidos; a ordem da fila não garante que o artifact mais novo será o último.
- Se a fonte/site mudou durante um build, descartar a publicação antiga e solicitar a combinação atual; não interromper um deploy no meio para simular atomicidade.
- Guardar relatórios e artifacts com retenção definida, além do manifesto público dos SHAs.
- Rollback é um dispatch de manutenção para uma combinação anterior autorizada. Precisa de um modo explícito para não ser barrado pelo guard de publicação mais recente. Não fazer force-push ou reverter a engine para reverter o site.
- Se qualquer etapa falhar, conservar a última publicação válida e exibir diagnóstico no Actions.

### 7.4. Configuração inicial do GitHub

Na implementação, depois de revisar o build:

1. Confirmar que ainda não existe outro site a preservar no Pages deste repositório.
2. Em **Settings → Pages**, selecionar **GitHub Actions** como fonte.
3. Configurar o environment `github-pages` para aceitar deploy da branch `codex/docs-site`.
4. Para a automação desejada, não adicionar aprovação manual obrigatória a cada deploy; manter revisão dos PRs/código da branch do site.
5. Registrar o workflow despachável mínimo na `main` e publicar o workflow real na branch dedicada.
6. Fazer primeiro dispatch, verificar HTTPS e a URL retornada pelo GitHub.
7. Atualizar o README com o link público somente após verificar a publicação real.

Não alterar a branch padrão nem desabilitar os checks da engine para habilitar o site. Não depender de domínio próprio, PAT ou servidor adicional na primeira versão.

## 8. Qualidade da experiência

Interface técnica simples: fundo claro, contraste legível, tipografia sóbria, largura confortável, exemplos copiáveis, navegação por teclado, menu responsivo e boa hierarquia. Não reutilizar a HUD ou as animações do jogo no portal.

Incluir busca estática local, validando compatibilidade/tamanho do índice na versão fixada. Não exigir cadastro externo para a primeira publicação. Resultados distinguem API, autoria e internals; material interno excluído não entra no índice.

Cabeçalho/rodapé devem informar que a documentação corresponde à fonte publicada, com link ao commit. Distinguir **versão REST v1**, **versão executável da engine**, **revisão de conteúdo de jogo** e **commit da documentação**.

Não criar uma versão Docusaurus por commit. Inicialmente publicar a documentação de desenvolvimento atual, claramente identificada; versões congeladas por release ficam para uma evolução posterior, com política própria.

## 9. Etapas de implementação e commits

| Etapa | Entrega concreta | Validação/gate | Commit sugerido |
| --- | --- | --- | --- |
| 1. Inventário e classificação | Lista de artigos publicáveis, excluídos, lacunas e links; conferir estado do Pages e alterações pendentes | Fontes reais e responsáveis identificados; nenhum arquivo local pendente importado como publicado | `docs: classify public documentation sources` |
| 2. Branch e esqueleto | Checkout isolado, árvore dedicada `codex/docs-site`, Docusaurus/Node fixados e lockfile | Build mínimo e página profunda sob `/HeroScript/`; nenhum fonte/build/save da engine na árvore do site | `feat(docs-site): bootstrap Docusaurus portal` |
| 3. Navegação | Home, três sidebars, manifestos, slugs e páginas de entrada | Caminhos API e desenvolvimento compreensíveis, sem artigos duplicados | `feat(docs-site): organize audience navigation` |
| 4. Importação | Importador por SHA, fontes/asset allowlist, relatório e `generated` ignorado | Idempotência, remoção de conteúdo obsoleto, erro em fonte ausente/colisão/travessia; nenhum write na engine | `feat(docs-site): import canonical Markdown by commit` |
| 5. Conteúdo para consumidores | Quickstart, contratos, sessões, combate/build, eventos, replay, erros e exemplos | Jornada documentada criar → consultar → comandar → recuperar; links para referência válidos | `docs(api): prepare consumer documentation trail` |
| 6. Conteúdo para desenvolvedores | Autoria JSON, sistemas, arquitetura, contribuição e operação | Separar configuração de implementação; revisar versões/status e referências históricas | `docs(dev): prepare authoring and engine trails` |
| 7. Referências | Páginas a partir de OpenAPI/AsyncAPI, schemas e downloads | Arquivos do mesmo SHA; `$ref`/anchors válidos; não inventar contratos nem tratar spec parcial como cobertura total | `feat(docs-site): publish machine contract references` |
| 8. Hyperlinks e Markdown | Resolução por AST, assets seletivos, redirects e links de edição/origem | Zero links/anchors internos quebrados, exemplos intactos, C#/JSON/Mermaid renderizados | `fix(docs-site): preserve links and Markdown compatibility` |
| 9. CI e sincronização | Registro/dispatcher mínimos na `main`; build/PR/dispatch reais na branch do site | Push na fonte reconstrói usando o SHA certo; PR só valida; sem PAT/loop/commit automático de cópias | `ci(docs): synchronize canonical documentation builds` |
| 10. Pages | Environment, artifact, deploy, concorrência, manifesto e rollback | Publicação HTTPS válida, atualização automática e falha mantendo a versão anterior | `ci(docs-site): deploy validated site to GitHub Pages` |
| 11. Acessibilidade e busca | Busca local, mobile, teclado, contraste e navegação entre trilhas | Resultados úteis, foco correto, links profundos após reload e assets sob o prefixo correto | `feat(docs-site): polish documentation discovery` |
| 12. Certificação e manutenção | Relatório, README da branch, guia editorial e link público verificado | Matriz abaixo aprovada; registrar SHAs, URL e limitações; não anunciar publicação antes de validá-la | `docs: document portal maintenance and publication` |

Commits relativos à fonte são feitos na `main`; commits da aplicação são feitos na branch dedicada. Não misturar correções de engine nessa entrega. Nenhuma etapa inclui automaticamente planos internos na navegação pública.

## 10. Matriz de validação

### Importação e integridade

- Mesmos SHAs produzem os mesmos artigos/rotas; importar duas vezes não duplica conteúdo.
- Artigo canônico alterado aparece no build sem edição manual de cópia.
- Arquivo removido não persiste em saída antiga; fonte publicada ausente bloqueia o build.
- Links entre API, conteúdo e arquitetura preservam destinos e âncoras.
- Links para código/exemplos internos apontam ao SHA da publicação.
- GDDs, planos, screenshots volumosos, arquivos locais e material não autorizado ficam fora.
- Download OpenAPI/AsyncAPI/schema corresponde byte a byte à fonte fixada.

### Conteúdo e navegação

- Consumidor encontra autenticação, quickstart, idempotência, 409, replay e referência REST sem passar por internals.
- Desenvolvedor encontra criação de setting, componentes/efeitos, pipelines, testes e operação.
- Versões e classificações não contradizem o contrato vigente; páginas planned/experimental estão identificadas.
- Home, busca, breadcrumbs, links relacionados e anterior/próximo funcionam em desktop/mobile/teclado.

### CI, sincronização e deploy

- Push aprovado na `main` despacha a branch dedicada com o SHA correto.
- Push na branch do site compila com fonte fixada; PR não publica.
- Dispatch manual funciona porque o workflow está registrado na branch padrão.
- Duas atualizações concorrentes não deixam conteúdo antigo sobrescrever o novo.
- Falha de build/link/contrato não troca o site publicado.
- Acessar diretamente `/HeroScript/api/contracts/` e recarregar funciona; não basta testar a home.
- Assets, busca e downloads funcionam com o prefixo, sem endpoints absolutos indevidos.
- Rollback explícito funciona sem modificar código, saves ou a história da engine.

## 11. Operação após a entrega

1. Alterar texto técnico junto do código na `main`, com revisão de contrato quando aplicável.
2. Alterar apresentação/navegação por PR com base na branch do site.
3. Exigir justificativa para novo documento não classificado e criar redirects para slugs publicados movidos.
4. Atualizar dependências do site em PRs próprios, com lockfile, build e smoke completos.
5. Revisar periodicamente links externos, versões, artigos experimentais e documentos históricos.
6. Usar relatório de importação e `site-version.json` para descobrir qual fonte está no ar.
7. Tratar indisponibilidade do deploy separadamente de bugs da API; não mudar gameplay para corrigir o portal.

## 12. Critérios de aceite

- [ ] Docusaurus e infraestrutura do site residem na branch dedicada.
- [ ] `main` mantém os Markdown/contratos canônicos e apenas a integração mínima de Actions.
- [ ] Consumidores e desenvolvedores têm entradas e seções lógicas separadas, com hyperlinks cruzados.
- [ ] Não existem duas cópias editáveis de um mesmo artigo técnico.
- [ ] Artigos existentes selecionados são importados automaticamente por SHA.
- [ ] Navegação, assets, fontes e referências de máquina passam nos checks.
- [ ] O Pages publica por Actions a partir do workflow da branch dedicada.
- [ ] Alterações aprovadas na fonte e no site disparam a atualização automaticamente.
- [ ] Fonte/site SHAs, falhas e rollback são rastreáveis.
- [ ] A documentação distingue capacidade implementada, experimental, planejada e histórica.
- [ ] Não houve mudança de gameplay, determinismo, dados de run ou saves.
- [ ] URL pública e jornada de integração foram verificadas antes de anunciar a entrega.

## 13. Referências oficiais consultadas

Consultadas em 2026-11-03. Conferir versões das dependências/actions novamente na implementação.

- **[D1]** [Docusaurus — Deployment](https://docusaurus.io/docs/deployment): build estático, GitHub Pages e configuração de URL/prefixo.
- **[D2]** [Docusaurus — Markdown Features](https://docusaurus.io/docs/markdown-features) e [configuração de Markdown](https://docusaurus.io/docs/api/docusaurus-config): CommonMark/MDX, `detect`, Mermaid e hooks de links.
- **[D3]** [Docusaurus — Markdown links](https://docusaurus.io/docs/markdown-features/links): resolução de links e identidade das páginas.
- **[D4]** [Docusaurus — Installation](https://docusaurus.io/docs/installation): requisitos e criação do projeto.
- **[G1]** [GitHub — Custom workflows with Pages](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages): artifact, environment, dependência entre jobs e permissões de deploy.
- **[G2]** [GitHub — Triggering a workflow](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow): comportamento do `GITHUB_TOKEN` e dispatch explícito.
- **[G3]** [GitHub — Manually running a workflow](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow): registro na branch padrão e seleção de outra branch com `--ref`.

Estado final desta tarefa: plano salvo. Implementação, criação da branch e ativação/publicação do Pages permanecem para a execução posterior.
