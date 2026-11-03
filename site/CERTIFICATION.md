# Certificação do portal

Data: 2026-11-03. Escopo: documentação estática, importação e publicação. Nenhuma alteração de gameplay, saves, determinismo ou modelos da engine.

## Resultados

- 84 artigos canônicos selecionados e importados por SHA da main.
- 70 operações e 222 schemas REST, referência SSE/AsyncAPI, três schemas de autoria e cinco exemplos HTTP.
- 391 páginas HTML e 67.225 hyperlinks, âncoras e assets verificados sem erro no build com busca.
- Cinco testes aprovados: caminhos/manifesto, reprodução byte a byte busca com acentos/filtros e guards de publicação.
- TypeScript, build de produção e smoke HTTP aprovados. Smoke cobre páginas profundas, contratos, busca e versão sob `/HeroScript/`.
- Busca local com 376 entradas; planos/GDDs não indexados. Teclado, labels, foco e movimento reduzido contemplados; página inicial verificada em largura móvel.
- Auditoria de dependências: zero alertas altos/críticos, seis baixos e 21 moderados transitivos. O gate não trata esses avisos como resolvidos.
- Primeira publicação: Actions [41697956383](https://github.com/GuiMayer/HeroScript/actions/runs/41697956383), fonte `47fd89dd78dcb314c3e13bd841757d706ce4c99a`, portal `6dc08b8f242bfe65bbb28493d346596bb0a64cef`. HTTPS e JSON de procedência confirmados.

## Operação

O workflow executa checks antes do upload/deploy, não publica PRs e descarta revisões ultrapassadas. O environment restringe a branch dedicada. As dependências/actions estão fixadas e o dispatcher usa token temporário. O repositório da fonte e a ancestralidade dos commits são validados antes da importação.

Os SHAs de cada publicação estão em `site-version.json`, no rodapé e no run do Actions. Consulte `README.md` para reprodução, manutenção editorial e rollback. Contagens correspondem à revisão certificada; novas páginas podem alterá-las.

## Limites

Validação automatizada não equivale a auditoria completa com leitores de tela. Links externos estão sujeitos à disponibilidade dos destinos. Há 32 Markdown não classificados excluídos até revisão editorial. A referência OpenAPI cobre somente a superfície declarada, sem promover recursos experimentais a estáveis.

## Recuperação exercitada

[Actions 41699101388](https://github.com/GuiMayer/HeroScript/actions/runs/41699101388): dispatch com `rollback=true`, fonte `47fd89dd78dcb314c3e13bd841757d706ce4c99a` e portal anterior `6dc08b8f242bfe65bbb28493d346596bb0a64cef`. Build e deploy concluídos; o JSON público confirmou esse par. A versão foi selecionada a partir de um workflow mais recente, verificando a separação entre pipeline atual e artifact anterior aprovado. A atualização normal seguinte é disparada pelo push da main, sem rollback.
