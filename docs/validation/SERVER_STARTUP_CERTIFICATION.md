# Certificação — inicialização do servidor

## Escopo

A API usa builds isolados e uma chave de entradas e artefatos para reaproveitar o binário. Uma chave incompatível ou entradas de build não reconhecidas levam a um build. Bundles publicados continuam revisionados e são validados antes de ativação. Um índice opcional associa o hash das entradas, assemblies e runtime às revisões já publicadas. Um índice ausente, inválido ou incompatível provoca compilação normal. A validação de conteúdo é feita em até quatro leituras concorrentes e a ativação permanece ordenada.

A Godot apresenta as fases do launcher e da API, com estados localizados para preparação, verificação e compilação do build, inicialização, carregamento dos settings e conexão. Tentativas simultâneas na mesma porta acompanham a tentativa proprietária. A interface só informa conexão após a resposta da API e a leitura do catálogo.

## Medições

Windows 10.0.26200, .NET 10.0.9, 12 processadores lógicos. Processo da API até health/ready, em porta e stores isolados, OS cache não controlado. Amostras por nearest-rank; com dez amostras, p95 é o maior valor.

| Condição | Amostras | p50 | p95 |
| --- | ---: | ---: | ---: |
| Antes, store aquecido | 10 | 14.528 ms | 15.634 ms |
| Depois, store novo por execução | 5 | 14.510 ms | 14.535 ms |
| Depois, store aquecido | 10 | 5.111 ms | 5.683 ms |

A mediana e o p95 aquecidos caíram 64,8% e 63,6% nessas amostras. A meta proposta de p95 inferior a 5 s não foi atingida. As condições de carga não foram rigorosamente controladas e a diferença não representa garantia para outra máquina. O cold medido é um processo com store novo; o cache de disco do sistema operacional não foi esvaziado.

Na Godot headless, uma sequência de três inicializações com store isolado levou 39.873 ms com build e 11.258/11.284 ms nas seguintes até GameSession.connect_engine terminar. Em outra sequência instrumentada, o total foi 33.778 ms com build e 13.266 ms com reaproveitamento. Nessa última amostra, o launcher registrou 4.776 ms para verificar/reutilizar o build e 6.105 ms do início do processo até verificar compatibilidade. Não há amostra suficiente para calcular p95 da Godot. Essas execuções podem ter concorrido com testes.

## Reproduzir

Compilar para uma pasta de artifacts isolada e executar os exemplos a partir da raiz do repositório.

```powershell
dotnet build src/API/API.csproj -c Release --artifacts-path .runtime/bucket-pipeline-build
python tools/measure-server-startup.py --dll .runtime/bucket-pipeline-build/bin/API/release/API.dll --store .runtime/startup-bench --port 18580 --samples 11
python tools/measure-server-startup.py --dll .runtime/bucket-pipeline-build/bin/API/release/API.dll --store .runtime/startup-cold --port 18581 --samples 5 --fresh-store
python tools/test-startup-content-cache.py --dll .runtime/bucket-pipeline-build/bin/API/release/API.dll --resources .runtime/bucket-pipeline-build/bin/API/release/Resources
powershell.exe -NoProfile -NonInteractive -File tools/test-server-build-fingerprint.ps1
powershell.exe -NoProfile -NonInteractive -File tools/test-server-build-integration.ps1
```

A primeira amostra de uma pasta sem índice mede compilação/publicação de conteúdo. Execute uma preparação antes de medir dez amostras aquecidas. Os stores de teste permanecem em .runtime para inspeção.

Para observar fases internas da API, definir HEROSCRIPT_STARTUP_TIMINGS=1. Para o launcher, definir também HEROSCRIPT_STARTUP_LAUNCHER_TIMINGS_PATH para um arquivo local. O progresso visual usa arquivos temporários por tentativa e não integra os snapshots ou comandos da run.

## Verificações

- Build Release e testes de publicação, compilação e isolamento passaram. A validação concorrente também foi testada com leituras simultâneas e bundle corrompido.
- A API real preservou o catálogo/revisões entre execução inicial e cache aquecido, reconstruiu o índice inválido e invalidou o cache após alteração de entrada.
- Jornada de capítulo, replay, branch e publicação sem revincular run ativa passaram em três testes de integração.
- Na Godot, passaram os testes de fases em inglês e português, conexão real e reutilização da API. Duas instâncias simultâneas receberam as fases da inicialização compartilhada.
- A cópia limpa do commit testado passou 19 testes Core, um teste API e o menu headless. A primeira importação da Godot falhou quando executada junto de outros testes; a repetição isolada passou. A cópia limpa antecede alguns commits posteriores de ajustes e testes.
- Os testes no checkout principal usaram alterações locais preexistentes. A verificação de fingerprints cobriu mudança de código e dados, artefato alterado, import desconhecido e caminho com espaços. A falha de compilação não reutilizou o binário antigo.

## Limites restantes

Falta certificar p95 abaixo de 5 s em execuções aquecidas, com e sem carga, e levantar uma série maior do tempo completo pela Godot. A primeira compilação permanece significativamente mais lenta. O cenário de SDK indisponível e a recuperação de um bundle publicado autoritativo corrompido não foram certificados; a engine preserva falha explícita em vez de sobrescrever esse bundle. Os resultados de teste não certificam uma build exportada standalone.
