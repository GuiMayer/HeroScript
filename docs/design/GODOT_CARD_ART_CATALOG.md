# Catálogo de artes das cartas

Os temas da run e dos menus, assim como o laboratório de cartas, usam
`examples/godot-engine-showcase/data/programmer_art_manifest.json`.
O catálogo é exclusivamente visual: associa o ID da definição da carta a um slot,
sem interpretar efeitos, tags, dano ou regras na interface.

O catálogo possui 94 associações explícitas de cartas, incluindo amostras de
laboratório. As cartas do laboratório mantêm as mesmas imagens na run.
As famílias incluem ataque, defesa, fogo, recuperação/cura, carga/condensação,
veneno, múltiplos acertos, precisão/crítico, compra/recuperação de cartas e energia.
Cartas da mesma família podem compartilhar um símbolo; não são ilustrações finais
exclusivas. Os SVGs seguem o estilo marfim com símbolos geométricos, sem rotação
decorativa e sem linhas triplas nas artes. Arquivos estão em `assets/programmer-art`.
O gerador `tools/build-programmer-art-assets.ps1` produz as artes estaticamente;
`-Check` verifica os arquivos e mapeamentos, e `-AssetsOnly` preserva o catálogo.

Para substituir uma arte, altere o `path` do slot. Para uma ilustração exclusiva,
adicione um slot e associe apenas a carta desejada em `categories.cards`.
Marque `placeholder: false` quando a arte for definitiva, para omitir o nome
sobreposto do placeholder. Cartas novas de mods sem mapeamento continuam usando
`card_unknown`; não há classificação automática por regras de jogo.

Os perfis preservam suas medidas: a carta de combate mantém 248×360 e arte de
64 unidades; o laboratório mantém suas próprias medidas. Título adaptável ocupa
os 24 originais, com custo em uma linha abaixo do nome. Cores de raridade e de
comparação dos valores continuam na política da carta. A mudança não altera
animações, regras da engine ou o posicionamento externo das telas.

Verificação: `ivory_run_contracts` cobre IDs conhecidos, importação das texturas,
diversidade, equivalência com o laboratório e fallback para conteúdo desconhecido.
