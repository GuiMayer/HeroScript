# Referência da run antes da nova refatoração

72 capturas sintéticas: 12 estados × EN/PT × 1280×720, 1920×1080 e 2560×1080. Incluem mão cheia/vazia, preview, sidebar, pause, terceira equipe/recurso desconhecido, jornada, recompensa, loja, preparação, diálogo e resultado.

Geradas por `tests/run_graphics_baseline.gd`, antes das mudanças de composição e cenário deste plano. Os componentes são os de produção; os dados e leituras são fixtures. Nenhuma run foi criada nem comandos foram enviados à engine, e preferências pessoais não foram acessadas.

O relatório `captures.json` lista os arquivos, leituras e asserts. A referência registra as limitações existentes, incluindo agrupamento de neutros. Não é um certificado de ausência das falhas que motivaram a refatoração.

As baselines anteriores permanecem intocadas. Capturas depois da refatoração serão registradas em outro diretório para comparação.
