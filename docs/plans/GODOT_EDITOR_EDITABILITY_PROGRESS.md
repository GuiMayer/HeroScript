# Editabilidade do client Godot — implementação

- [x] Etapa 1: pais estáticos para ferramentas, pilhas, preview de ações e overlays; composição editável do ator de battlefield. Commit `a2f1952a`.
- [x] Etapa 2: recursos de métricas do MVP, superfície de arena no editor e âncoras de legenda do mapa. Commit `f0831b5b`.
- [x] Etapa 3: exemplos locais/F6 para mão, combate e mapa, sem trocar a sessão. Commit `be69eb32`.
- [x] Etapa 4: sinais fixos nos arquivos de cena e handlers dinâmicos nomeados. Commit `b8edd7c3`.
- [x] Etapa 5: fusão de seis templates triviais, estilos da mão compartilhados, suporte dos recursos/nós usados no editor, override de ícones de status e guia de edição.

## Validação

A rodada final passou em 12 testes, somando 934 verificações, sem diagnósticos de erro. Foram executados testes de geometria, interação da mão, seleção, overlays, navegação do mapa, loadout e configurações. A importação no editor passou; a abertura headless da cena MVP no editor, depois da importação, encerrou sem erros. Os testes de prévia simulam sete entradas isoladas e verificam que a sessão não foi substituída. Isso não é certificação de gameplay pela API real.

A edição cobre as regiões citadas e preserva os adaptadores existentes. Não significa que todas as 125 cenas tenham sido convertidas para F6 ou que todo override de tema do projeto tenha sido eliminado. As demais telas e as variantes experimentais continuam no inventário da auditoria para trabalho posterior por região.

Ver [guia de edição](../design/GODOT_UI_EDITING_GUIDE.md).
