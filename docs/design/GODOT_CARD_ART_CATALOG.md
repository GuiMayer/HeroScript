# Catálogo de artes das cartas

O tema da run usa `examples/godot-engine-showcase/data/ivory_run_art_manifest.json`.
O catálogo é exclusivamente visual: associa o ID da definição da carta a um slot,
sem interpretar efeitos, tags, dano ou regras na interface.

Todas as 62 definições atuais dos settings default, ascendant e volatile-core têm
mapeamento explícito. As cartas do laboratório mantêm as mesmas imagens na run.
As dez famílias são ataque, defesa, fogo, recuperação/cura, carga/condensação,
veneno, múltiplos acertos, precisão/crítico, compra/recuperação de cartas e energia.
Cartas da mesma família podem compartilhar um símbolo; não são ilustrações finais
exclusivas. Os SVGs seguem o estilo marfim, tinta e circuitos a 45 graus.

Para substituir uma arte, altere o `path` do slot. Para uma ilustração exclusiva,
adicione um slot e associe apenas a carta desejada em `categories.cards`.
Marque `placeholder: false` quando a arte for definitiva, para omitir o nome
sobreposto do placeholder. Cartas novas de mods sem mapeamento continuam usando
`card_unknown`; não há classificação automática por regras de jogo.

Verificação: `ivory_run_contracts` cobre cada ID, importação das texturas,
diversidade, equivalência com o laboratório e fallback para conteúdo desconhecido.
