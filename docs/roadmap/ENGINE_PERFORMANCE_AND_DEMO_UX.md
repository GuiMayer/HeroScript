# Desempenho da engine e experiência da demo Godot

Data: 2026-09-11. Escopo: engine headless e `examples/godot-engine-showcase`.

Atualização posterior: o cliente passou a usar inglês como chave/fallback e
camadas separadas de transporte, gateway, aplicação, playback e UI. A estrutura
atual está em [Arquitetura do cliente](../../examples/godot-engine-showcase/ARCHITECTURE.md).
As medições abaixo registram a entrega de otimização original.

## Resultado

A demo possui português/inglês em runtime, seleção de carta e alvo sem recriar
a tela, custos e prévias canônicas, animações simples e controles da fila de
apresentação. A campanha completa, os três sandboxes e os testes de interação
passaram. Nenhuma dessas preferências altera a run ou suas regras.

Foi reduzido o trabalho de busca de recibos no armazenamento em arquivos.
Isso não torna todos os comandos instantâneos: o teste completo ainda mediu
mediana de **1.690 ms** e máximo de **4.981 ms** em 41 requisições HTTP de
comando. O histórico crescente e a transação completa continuam merecendo
otimização. O antigo anúncio genérico de 5–8 ms foi removido do README.

## Diagnóstico e implementação de desempenho

### 1. Recibos não devem exigir materialização de todos os snapshots

`RunManager.FindReceipt` e o coordenador consultavam o histórico inteiro para
descobrir se um comando já havia sido persistido. A operação deserializava e
validava todos os commits, mesmo quando o ID procurado ainda não existia.

Foi adicionada `IRunCommitReader.FindCommandAsync`. O store em arquivos usa
identidades validadas por fingerprint dos bytes; só materializa o recibo
encontrado. A implementação padrão da interface preserva a semântica nos
outros stores, sem impor um índice paralelo como autoridade.

### 2. Validação repetida de bytes imutáveis

`FileRunCommitStore` reutiliza o `LruCache` existente, limitado a 256 entradas.
Guarda apenas identidade, sequência, ID do comando e hashes de um commit cuja
desserialização e validação já tiveram sucesso. A capacidade pode ser alterada
no construtor; zero desativa o cache.

Garantias mantidas:

- arquivos são relidos; a chave é SHA-256 dos bytes reais, não tamanho/mtime;
- bytes diferentes são novamente desserializados e validados;
- a busca confere identidade do caminho e a cadeia de hashes a cada leitura;
- objetos desserializados não são compartilhados pelo cache;
- o cache é descartável, não é persistência nem uma nova autoridade;
- resultados negativos não ficam armazenados e não escondem commits novos;
- cache desligado, evicção e restart preservam os resultados.

O custo ainda é linear nos bytes do histórico. A primeira leitura é fria;
mais de 256 entradas disputando o cache podem reduzir muito o benefício.
Não foram removidas validações de integridade nem alterados hashes, schemas
de run, regras, RNG ou política de publicação do conteúdo.

### 3. Menos espera entre leituras na Godot

O cliente consulta projeções independentes em paralelo. Após um comando,
reutiliza o snapshot de combate do recibo somente quando a sequência da run
recém-lida e o ID do encontro correspondem; caso contrário consulta a engine.
Isso evita uma leitura redundante no caminho comum.

Falhas de sincronização limpam comandos legais, bloqueiam novos envios e
oferecem reconexão. Um comando confirmado cujo refresh falhou não é reportado
como se não tivesse sido executado. Timeouts e conflitos exigem sincronização
antes de outro comando. Não há retry automático com um novo ID.

O launcher executa a engine em Release. `HeroAPI.timings` mantém no máximo 200
medições recentes, sem armazenar histórico ilimitado de telemetria na UI.

## Medições reproduzíveis

Ambiente local: Windows, .NET 10 Release e Godot 4.7.2. Uma mesma run com
40 commits foi lida seis vezes por configuração; a primeira amostra foi
descartada e a tabela registra a mediana das cinco restantes.

| Operação no mesmo histórico | Cache desligado | Cache 256 aquecido |
| --- | ---: | ---: |
| Materializar todos os commits | 1.243,99 ms | 534,42 ms |
| Buscar ID de comando inexistente | 1.147,97 ms | 70,16 ms |

A busca aquecida foi aproximadamente **94% mais rápida** que a mesma operação
sem cache. A primeira linha representa o caminho de leitura integral usado
anteriormente pelos consumidores de recibos, não uma comparação entre dois
binários históricos. Este benchmark não mede a duração da transação inteira.

Fixture local usada: `cc893ddb-ab43-88b9-be30-f510a6c75c6d`, no store isolado
`.runtime/qa-5272/runs`. Fixtures não são versionadas. Qualquer run existente
pode ser medida com [CommitReadBenchmark](../../tools/CommitReadBenchmark/README.md).

O teste posterior de campanha terminou as sete atividades e registrou
41 requisições POST de comando, mediana 1.690 ms e máximo 4.981 ms; run
`f4a99737-c14c-898b-aa57-5ccc94a284fc`. Os números incluem HTTP local e execução
do servidor, mas excluem a reprodução visual posterior. São uma observação
local, não um SLA, nem um antes/depois controlado por seed. A janela limitada
de telemetria pode excluir chamadas iniciais de testes mais longos.

## Localização

- `i18n.gd` carrega `pt_BR.json`/`en.json` como recursos nativos `Translation`.
- Chaves de interface são mensagens-fonte; tradução ocorre antes da formatação.
- `content.json` associa IDs estáveis a nomes de apresentação por idioma.
- Menu, configurações, combate, atividades, sandbox, timeline e códice usam
  a camada de localização; números de resources não são truncados para inteiros.
- Preferência de idioma é local e persistida; trocar idioma reconstrói a UI,
  não a run nem a definição de conteúdo.
- JSON bruto, IDs e erros técnicos vindos da API permanecem canônicos. Conteúdo
  de mods sem tradução usa o nome fornecido ou o ID formatado; não existe
  tradução automática arbitrária de conteúdo.

A integração segue a documentação de
[internacionalização da Godot](https://docs.godotengine.org/en/stable/tutorials/i18n/internationalizing_games.html).

## Decisões de jogabilidade e apresentação

Referências de gênero consultadas: [Slay the Spire, da Mega Crit](https://www.megacrit.com/games/)
e [Balatro, da Playstack](https://www.playstack.com/games/balatro/). A aplicação
a seguir é uma interpretação de design para esta demo, não reprodução de
assets, código ou sistemas desses jogos.

1. **Escolha legível:** mão persistente durante seleção, alvo válido destacado,
   custos visíveis, intents, recursos e contadores das pilhas.
2. **Consequência antes da confirmação:** applications e cálculos da prévia vêm
   do executor canônico. Descrições locais deixaram de prometer números fixos.
3. **Confirmação inequívoca:** clicar no alvo envia o candidato correspondente;
   escolhas alternativas são explícitas. Cancelar não envia comando.
4. **Feedback curto:** entrada da carta, destaque no hover/foco, deslocamento
   ao alvo após aceitação e números flutuantes a partir dos frames reais.
5. **Ritmo controlável:** velocidade, movimento reduzido, fila automática ou
   manual. O pause suspende gameplay e a apresentação; menus continuam ativos.
6. **Uma autoridade:** a engine calcula a transação inteira. A Godot exibe o
   snapshot final e reproduz os registros recebidos. A animação não reexecuta
   efeitos e não é um estado intermediário autoritativo.

Correções importantes: seleção de alvo não recria a tela; os frames são lidos
de `receipt.state.resolution`; encerrar turno exige candidato legal; a UI não
escolhe implicitamente entre alternativas de custo; botões principais ficam
dentro do viewport no layout padrão.

## Validação executada

- .NET Release: **1.073 Core + 155 API**, todos aprovados.
- Cache: mudança de bytes mantendo tamanho/mtime, objetos independentes,
  capacidades 0/1/256, append após busca negativa, evicção e restart.
- Godot campanha: sete atividades, relíquia, recompensa, loja, preparação,
  upgrade, três sandboxes, timeline, branches, simulação e replay; zero falhas.
- Godot UI: paridade dos catálogos/placeholders, troca de idioma em runtime,
  seleção/alvo, custos, envio real, frames, pause, bloqueio de input e replay;
  zero falhas. Trocar idioma e selecionar cartas preservam os bytes da run.
- Capturas de combate em português e inglês revisadas visualmente.

Comandos de reprodução, na raiz:

```powershell
dotnet test HeroScript.slnx -c Release
./examples/godot-engine-showcase/Start-Demo.ps1 -Headless -Port 5272
./examples/godot-engine-showcase/Start-Demo.ps1 -UiSmoke -Port 5272
```

## Próximas otimizações, por prioridade

1. **Perfilar a transação completa por etapa.** Separar validação, execução,
   canonicalização, publicação, escrita e projeções. O comando de quase cinco
   segundos demonstra que otimizar apenas leitura de recibos não basta.
2. **Eliminar buscas duplicadas por recibos dentro da mesma transação.**
   Gateway/coordenador/manager ainda precisam preservar idempotência e
   concorrência; compartilhar resultado apenas dentro do escopo seguro.
3. **Paginar no armazenamento.** `RunCommitProjectionReader` aplica filtro e
   limite depois de `LoadCommitsAsync`; timeline longa paga materialização de
   toda a run. Preservar verificação da cadeia com uma política explícita.
4. **Índice derivado e recuperável para históricos grandes.** A busca atual
   continua O(bytes). Avaliar índice por commandId/sequence sem promovê-lo a
   autoridade, com teste de corrupção e reconstrução após restart.
5. **Medir tamanho e repetição dos snapshots/receipts.** Avaliar projeções mais
   compactas para a UI e checkpoints/segmentação no armazenamento somente com
   plano de schema, migração e provas de replay. Não descartar journal/facts.
6. **Concorrência entre runs e prévias.** O semaphore do store serializa as
   operações dessa instância. Locks por run e cache de prévias por hash completo
   são candidatos, não alterações seguras sem testes de corrida/invalidação.

Esses itens permanecem pendentes; esta entrega não afirma que a engine atingiu
latência de produção nem cobertura visual de qualquer conteúdo arbitrário.
