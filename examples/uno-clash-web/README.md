# Clash Web

Jogo independente com Duelo e Todos contra Todos (quatro participantes), usando
HeroScript 0.1.0 pre-compilado para Windows x64. Nao precisa do repositorio,
codigo-fonte, SDK ou compilacao do HeroScript.

## Abrir o jogo

Depois de instalar o runtime, abra `Start-Clash.bat`. O inicializador le o
`heroscript.json`, carrega os JSONs locais e abre `index.html` com a API correta.
Mantenha a janela do inicializador aberta; Ctrl+C encerra a API iniciada por ele.
Fechar somente a aba do navegador nao encerra o servidor. Portas ocupadas sao
recusadas; outros processos nao sao encerrados automaticamente.

## Instalar o runtime uma vez

Com o ZIP oficial baixado:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./Install-HeroScript.ps1 -Archive C:/downloads/heroscript-runtime-v0.1.0-win-x64.zip
```

Ou, para desenvolvedores com GitHub CLI autenticada:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./Install-HeroScript.ps1
```

O instalador obtem o artefato `runtime-win-x64` da execucao 40812254602 do
repositorio GuiMayer/HeroScript, verifica o SHA-256 fixado no manifesto e instala
apenas o runtime e seu inicializador em `heroscript/`. Nunca sobrescreve os JSONs
do jogo. A abertura normal nao precisa de gh, internet, Python, Node ou .NET
instalado. Fontes visuais externas podem usar a internet; as regras sao locais.

Esse primeiro pacote foi publicado como artefato do Actions, com retencao limitada,
e nao como release permanente. Guarde o ZIP verificado para instalacao offline.
Para trocar a versao depois da primeira instalacao, feche a API e use
Update-HeroScript.ps1 conforme a secao abaixo; ele atualiza o manifesto.

## Atualizar o runtime

Feche o jogo e o inicializador. Para substituir a pasta heroscript pela versao
especificada e atualizar automaticamente runtimeVersion/runtimePackage:

```powershell
./Update-HeroScript.ps1 -Version 0.2.0
```

A versao precisa existir como release v0.2.0 no HeroScript, com ZIP Windows x64 e
arquivo .sha256. Para um pacote publicado apenas como artefato do Actions:

```powershell
./Update-HeroScript.ps1 -Version 0.2.0 -RunId 123456789
```

Para atualizar offline, forneca o ZIP e seu arquivo .sha256 ao lado (mesmo nome
base), ou passe explicitamente -Sha256 com o hash oficial:

```powershell
./Update-HeroScript.ps1 -Version 0.1.0 -Archive C:/downloads/heroscript-runtime-v0.1.0-win-x64.zip
```

O comando valida checksum, versao e plataforma antes da troca. Recusa runtime em
uso, substitui a pasta inteira (arquivos antigos nao ficam misturados) e preserva
content/, a interface, configuracoes do projeto e saves. Se falhar na troca,
restaura a pasta anterior. O backup fica em .runtime/update-*/previous-runtime,
junto do manifesto anterior. Nao realiza migracao de regras ou saves, nem tenta
executar a nova versao: compatibilidade de gameplay deve ser testada separadamente.
Os exemplos com 0.2.0 sao ilustrativos, nao indicam uma versao ja publicada.

## Estrutura

- `heroscript.json`: versoes, settings, pasta de conteudo, porta e interface.
- `heroscript/`: binarios pre-compilados; ignorados pelo Git.
- `content/default/`: dependencia basica do pacote oficial.
- `content/clash/` e `content/circular-table/`: regras e conteudo do jogo.
- `app.js`, HTML, CSS e `game-modes.js`: apresentacao e comunicacao REST.
- `.runtime/`: fixtures e resultados locais de desenvolvimento; ignorados pelo Git.

Edite os JSONs em `content/` e reinicie o inicializador para publicar a revisao.
Runs existentes continuam vinculadas ao conteudo com que foram criadas. A API
executa toda a regra de negocio; a interface apresenta comandos e efeitos.

Saves, revisoes publicadas e logs ficam em `%LOCALAPPDATA%/clash-web/`,
fora da instalacao. Esta migracao nao importa nem apaga saves antigos do repositorio
HeroScript. Nao misture stores nem remova conteudo fixado de partidas existentes.

A porta padrao e 15260, configurada no manifesto. Para uma sessao em outra porta:

```powershell
./Start-Clash.ps1 -ApiPort 15360
./Start-Clash.ps1 -CheckOnly -ApiPort 15360
```

O parametro `-NoBrowser` inicia somente a API. `-CheckOnly` valida a inicializacao
e a publicacao dos dois settings e encerra sua propria instancia.

## Jogo

A mesa abre vazia; Nova Partida oferece os dois modos em um menu radial. O circulo
cinza gira lentamente. Todas as maos compartilham 108 cartas fisicas; o descarte
pode ser reciclado preservando a carta ativa. Maos laterais usam perspectiva.
Tema claro/escuro e preferencia de auto-turno ficam na interface. A telemetria pode
revelar as cartas reais dos bots conforme a visibilidade configurada nos JSONs.
Efeitos e compras sao apresentados na ordem registrada pela API, mantendo os
nos das cartas existentes. Trocar tema nao reinicia a partida.

## Testes de desenvolvimento

Node e Python sao ferramentas de teste, nao requisitos para jogar.
Execute na raiz deste projeto:

```powershell
node --test tests/*.test.cjs
powershell -NoProfile -ExecutionPolicy Bypass -File tests/launcher.tests.ps1
./Start-Clash.ps1 -NoBrowser
# Em outro terminal:
node tests/modes.browser.cjs http://127.0.0.1:15260
node tests/modes.integration.cjs http://127.0.0.1:15260
```

Fixtures usam o mesmo API.exe instalado, sem dotnet ou build:

```powershell
./tests/Start-TestApi.ps1 -Mode effects-four -Port 15262
node tests/four-effects.browser.cjs http://localhost:15262 1440 reverse
```

O recibo `.runtime/clash-fixture-15262.json` identifica processo, store e logs da
fixture. Encerre apenas esse processo ao concluir o teste. Outras fixtures cobrem
+2 acumulado, reciclagem e compras consecutivas de +4.
