# Execução do portal Docusaurus

## Conclusão das etapas 1–12

A fonte canônica continua na main. A aplicação e o pipeline completos estão em `codex/docs-site`, sem incluir a árvore da engine. Portal: https://guimayer.github.io/HeroScript/.

| Etapa | Entrega | Commit da branch do portal |
| --- | --- | --- |
| 1 | Inventário e plano na main | `2aefff8a` (main) |
| 2 | Fundação isolada Docusaurus | `e3cde1fe` |
| 3 | Navegação por público | `58393251` |
| 4 | Importação por SHA e allowlist | `42900c15` |
| 5 | Jornada do consumidor | `5663ff5f` |
| 6 | Autoria, arquitetura e operação | `4b96d6ec` |
| 7 | Referências de máquina e glossário | `6911ae41` |
| 8 | Links, AST, assets e reprodução | `5cb96f09` |
| 9 | Dispatcher e CI | `36fda811` |
| 10 | GitHub Pages e guards | `23cba387`, ajustes até `6dc08b8f` |
| 11 | Busca e acessibilidade | `0769c657` |
| 12 | Certificação e manutenção | Commit `docs: document portal maintenance and publication` na branch dedicada |

## Evidências

Primeiro deploy concluído: [Actions 41697956383](https://github.com/GuiMayer/HeroScript/actions/runs/41697956383), fonte `47fd89dd`, portal `6dc08b8f`. HTTPS, JSON de procedência e navegação real confirmados. A versão final e as verificações de recuperação estão registradas em `site/CERTIFICATION.md` da branch dedicada; o arquivo público `site-version.json` é a referência do que está no ar.

Build com busca: 84 artigos, 70 operações e 222 schemas; 391 páginas e 67.225 links/âncoras/assets sem erro. Testes de reprodução byte a byte, segurança de caminhos, busca e proteção contra publicação ultrapassada. Typecheck, build e smoke HTTP aprovados. A referência OpenAPI continua parcial; as classificações de estabilidade são preservadas.

Não foram incluídos planos, GDDs ou pesquisas na navegação. Há 32 documentos não classificados excluídos até revisão editorial. Dependências têm seis alertas baixos e 21 moderados transitivos, sem altos/críticos na auditoria da entrega. A revisão de acessibilidade não substitui auditoria completa com leitores de tela.

## Manutenção

Editar texto técnico e contratos na main. Todo push despacha o site com SHA explícito. Editar apresentação/manifesto por PR para `codex/docs-site`; nunca mesclar sua árvore na main. Consulte [publicação](../publication/README.md) e `site/README.md` da branch dedicada para reprodução e rollback. Alterações preexistentes não commitadas da engine foram preservadas.
