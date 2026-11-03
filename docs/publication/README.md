# Publicação da documentação

Fonte técnica canônica: `main`. Aplicação do portal: `codex/docs-site`.

O inventário distingue candidatos e documentos que exigem revisão. Ele não é
uma autorização para publicar todos os arquivos. O manifesto da aplicação
selecionará artigos, títulos, slugs e hyperlinks. Planos, GDDs e pesquisas ficam
fora por padrão. Builds importam somente arquivos commitados de um SHA fixo.

Consumidores: começar, contratos/erros, sessão, run/combate, cartas/build,
eventos, histórico e integrações. Desenvolvedores: autoria JSON, sistemas,
arquitetura, testes/contribuição e operação. Referências de máquina são compartilhadas.

Alterações pendentes na árvore da engine devem ser preservadas. Nenhuma cópia
gerada deve voltar para main. O registro de execução está em
[progresso](../plans/DOCUSAURUS_GITHUB_PAGES_IMPLEMENTATION_PROGRESS.md).

## Portal e sincronização

Portal público: https://guimayer.github.io/HeroScript/.

A aplicação vive em `codex/docs-site`; veja `site/README.md` nessa branch para instalação, seleção editorial e rollback. Nunca mescle a árvore dessa branch na main. Artigos técnicos continuam sendo editados aqui, junto das mudanças da engine. Todo push na main despacha o build do portal com o SHA exato, sem copiar artigos commitados de volta.

O manifesto da branch dedicada seleciona 84 artigos. Há entradas separadas para consumidores REST e desenvolvedores, referência gerada de contratos, busca local e links cruzados. Planos e GDDs não são publicados. A presença de uma operação no OpenAPI não modifica sua classificação de estabilidade.

Use o `site-version.json` público e o resultado do Actions para verificar qual commit está no ar. Falhas de importação, testes, links ou build impedem o novo deploy. O environment aceita apenas a branch dedicada. Não é necessário PAT: o dispatcher utiliza o token temporário do GitHub Actions.
