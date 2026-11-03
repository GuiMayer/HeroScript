# HeroScript documentation portal

Portal público: **https://guimayer.github.io/HeroScript/**.

- [Consumir a API](https://guimayer.github.io/HeroScript/api/): integração REST, comandos, sessões e recuperação.
- [Desenvolver](https://guimayer.github.io/HeroScript/developers/): autoria JSON, arquitetura, sistemas e operação.
- [Referências](https://guimayer.github.io/HeroScript/reference/): OpenAPI, AsyncAPI, schemas e exemplos.

Esta branch (`codex/docs-site`) contém exclusivamente a aplicação Docusaurus. Os Markdown técnicos, contratos e exemplos canônicos permanecem na `main` e são importados por commit. Pushes nas duas branches atualizam o portal automaticamente após os checks. Não mescle a árvore desta branch na main.

Veja [manutenção e rollback](site/README.md), [certificação](site/CERTIFICATION.md) e [etapas executadas](site/IMPLEMENTATION.md). O JSON público [site-version.json](https://guimayer.github.io/HeroScript/site-version.json) identifica as revisões publicadas.
