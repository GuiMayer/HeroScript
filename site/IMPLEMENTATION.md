# Execução do portal

## 2 — Fundação

Docusaurus 3.10.2, Node 24.14.0, React e lockfile fixados. Site isolado, três entradas e prefixo /HeroScript/. A engine não faz parte da árvore versionada desta branch.

## 3 — Navegação

84 artigos selecionados por manifesto, três trilhas e páginas próprias. Tipos de Docusaurus configurados. Overrides de tinypool/serialize-javascript atualizam dependências vulneráveis; compatibilidade conferida no build.

## 4 — Importação

Importador Git por SHA, limites de caminho, allowlist, AST e assets reproduzíveis. Cópias geradas não são versionadas. Verificação integral encontrou links antigos quebrados, bloqueados até revisão editorial das etapas 5–6.

## 5 — Consumidores

Entrada orientada ao fluxo REST e índice canônico atualizado com composição, pagamentos, suporte, recompensas e compartimentos. Mermaid recebeu a dependência de layout exigida pelo bundler.

## 6 — Desenvolvimento

Trilhas de autoria, internals e operação, com links aos sistemas. Corrigidos na main o link antigo da matemática e a versão executável do README, sem incluir outras alterações pendentes.

## 7 — Referências

Operações e schemas REST gerados sem expansão recursiva; AsyncAPI/SSE, três schemas de autoria e cinco exemplos HTTP do mesmo SHA. Sem execução de requests no portal.

## 8 — Links e Markdown

Prefixos de rotas normalizados, links internos por arquivo/âncora e downloads com pathname explícito para evitar slash em JSON/HTTP. O build mantém links quebrados como erro.

## 9 — CI e sincronização

Main contém somente dispatcher e registro de workflow. Cada push em main despacha a branch dedicada com SHA explícito; builds de PR não publicam. Política de fonte, cache e guards evitam refs arbitrários ou deploy supersedido.
