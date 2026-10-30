# Integracao independente com HeroScript

O manifesto `heroscript.json` na raiz fixa o runtime 0.1.0 e aponta para `content/`.
O consumidor carrega somente o executavel publicado; nenhum caminho depende de
um checkout pai do HeroScript. O runtime e seu launcher sao instalados do ZIP
oficial, sem recompilacao ou alteracao de binarios.

Os packages default, uno-clash e circular-table pertencem a esta distribuicao do
consumidor. A base foi copiada do pacote verificado; os JSONs dos dois modos foram
migrados sem alterar suas regras. As dependencias estao declaradas em package.json.

Start-UnoClash.ps1 delega a inicializacao ao launcher original do pacote, passando
explicitamente o manifesto raiz. A interface recebe ?api= com a URL da instancia.
Production usa allowedOrigins=["null"] para o HTML local. Se hospedar a interface
por HTTP, configure sua origem real no manifesto. Nao habilite ferramentas de
administracao ou recarga para distribuir o jogo.

Os comandos, as acoes legais, a propriedade das cartas, o percurso, as penalidades
e a vitoria continuam sendo decididos pela API e pelos JSONs. O transporte REST e
os bindings visuais dos dois modos nao foram substituidos por regras locais.

Para distribuir: inclua interface, content/, manifesto, Start-UnoClash.bat/ps1 e
heroscript/ ja instalado. O jogador nao precisa obter artefatos no GitHub. Para
versionar: commit os fontes e JSONs; restaure os binarios ignorados pelo Git com
Install-HeroScript.ps1 usando o ZIP e checksum fixados. Extrair o pacote generico
por cima do projeto nao e necessario e pode sobrescrever conteudo do consumidor.
