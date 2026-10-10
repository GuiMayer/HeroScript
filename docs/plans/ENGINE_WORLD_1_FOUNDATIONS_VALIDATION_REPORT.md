# Certificação dos fundamentos do mundo 1

Data: 2026-10-10. Guia: [plano de implementação](ENGINE_WORLD_1_FOUNDATIONS_IMPLEMENTATION_PLAN.md).

## Escopo e contratos

A certificação cobre a infraestrutura necessária para produzir o primeiro ato,
não o balanceamento, duração ou catálogo final de Ascendant. Engine **28**,
commit schema **4**, meta schema **1**; .NET runtime 10.0.9 e Godot 4.7.2.
O launcher exige `canonical-effect-identities`, `foundation-reload-safety` e as capabilities anteriores.
Snapshots antigos permanecem em disco; nenhum save real foi usado, apagado,
reiniciado ou migrado nos testes. Histórico/provas meta podem continuar legíveis
sem autorizar execução de um runtime incompatível.

O [contrato REST/Godot](../api/world-one-foundations.md) descreve os campos públicos:
`characterResources`, owner da carteira, elegibilidade congelada, disponibilidade
e provas de perfil, unidades/origens de probabilidade e proveniência monotônica.
A Godot não calcula regras: só apresenta projeções e envia comandos.

## Jornada integrada

`WorldOneFoundationJourneyTests` publica uma fixture curta derivada do bundle
Ascendant **somente no store temporário**. Todos os comandos de gameplay são
enviados à API real; nenhum recurso, dano, desbloqueio ou receipt é simulado.
As modificações de conteúdo de teste não alteram os arquivos do jogo.

O roteiro usa dois settings (`default`, `ascendant`) e dois jogadores:

1. Criar tentativas e capturar elegibilidade ainda bloqueada.
2. Criar uma oferta, confirmar ausência da carta bloqueada e escolher outra.
3. Entrar no combate e passar o turno para a IA realmente ferir o personagem.
4. Ler previews repetidos sem mudar hash/RNG; executar crítico de 150% em
   três impactos com scopes e rolls distintos, pela pipeline publicada.
5. Matar o inimigo, resolver o encontro e promover os recursos uma única vez.
6. Pagar dez unidades da carteira e recuperar quinze do personagem em um
   único comando de preparação; retries não cobram nem curam novamente.
7. Resolver o nó de recuperação: a condição declarativa concede uma opção.
8. Iniciar outro encontro com a reserva recuperada; a captura antiga permanece
   idêntica mesmo após o grant.
9. Criar nova run e observar a nova opção na oferta legal; nenhuma progressão
   nem snapshot do outro jogador/setting muda.
10. Verificar replay depois da mudança do perfil. Restart é exercido após
    captura, crítico, resolução e recuperação.

São dez stores/runtimes independentes com seeds, IDs de comando e conteúdo
iguais. A comparação inclui hashes canônicos de receipts, commits inteiros,
provas e estado final. Ordem de propriedades de objetos JSON não é parte do
contrato; no retry apenas o indicador `duplicate` difere deliberadamente.
Os commits incluem os fatos de cálculo/RNG e transporte, não só os números
visíveis na interface.

### Exemplo de autoria exercido pela fixture

```json
{
  "effectId": "ascendant_critical_lance.damage",
  "type": "DAMAGE",
  "target": "TARGET",
  "targetResource": "health",
  "flatValue": 7,
  "repeat": 3,
  "targetLoss": { "policy": "StopRepeat" },
  "calculationPipelineId": "ascendant_critical_amount"
}
```

Esse fragmento complementa os `randomInputs` calculados da carta Ascendant;
não é uma definição completa isolada. `StopRepeat` é uma decisão configurável
de conteúdo, não um fallback para esconder falhas: com `Fail`, perder o alvo
durante os impactos cancela a transação conforme a regra escolhida.

```json
{
  "optionId": "recover",
  "costs": [{ "resourceId": "gold", "amount": 10 }],
  "effectOwner": "PlayerEntity",
  "effects": [{
    "effectId": "foundation.recovery",
    "type": "HEAL",
    "target": "SELF",
    "targetResource": "health",
    "flatValue": 15
  }]
}
```

A recuperação usa a pipeline default publicada para `effect_amount`, sem
cálculo paralelo no handler. Outras unidades/semânticas devem declarar suas
pipelines. `health` e `gold` são IDs desta fixture, não casos especiais da engine.

## Hotreload e isolamento

- Preview e commit validam os gates do **modo de destino**, não apenas o modo atual.
- Encontro terminal precisa ser resolvido antes da ativação; não se troca a
  política usada para transportar sua saída pendente.
- Atores de encontros já resolvidos permanecem byte-equivalentes, mesmo se
  um recurso histórico não existir mais no conteúdo futuro.
- Proveniência é conservada ao desabilitar cheats. Published → Experimental →
  Sandbox → DevModder → Branch é monotônica; policy escolhe as origens permitidas.
- Nenhuma leitura, simulação ou replay concede progresso; branches herdam captura
  e valores imutáveis. As regressões de recuperação/resolução também exercem
  branches, retries, restart e dez replays por roteiro.
- Cache de perfil usa sequência/revisão e invalidação por append; conteúdo usa
  revisão publicada e previews usam o snapshot. Concessões posteriores não
  invalidam/recriam a elegibilidade de runs existentes.
- O gate final acrescentou cinco casos de composição da origem declarada com
  as capabilities: elas não reduzem uma classificação mais conservadora
  definida no modo. Declaração, estado registrado e origem intrínseca usam a
  mesma ordenação, inclusive uma declaração explícita de Branch.

## Medição de perfil

`WorldOneProfileHistoryTests` usa 8, 64 e 256 runs imutáveis distribuídas em
quatro escopos jogador/setting, com contribuições e prova real. Compara rebuild
frio e projeção aquecida com a redução autoritativa e verifica escopo vazio.
São snapshots pequenos sintéticos de persistência; não equivalem a 256 campanhas
longas. Os tempos são evidência local, não assertions dependentes da máquina.
"Frio" significa novo store/projeção, não limpeza do cache de disco do Windows.
A primeira consulta de histórico ocorre após reconstruir a meta-progressão;
os cinco reads aquecidos são feitos sobre o mesmo store.

Gate Core final (com execução concorrente da jornada REST):

| Runs totais / correspondentes | Meta fria | Meta warm máx. (5 reads) | Perfil frio | Perfil warm máx. (5 reads) |
| --- | ---: | ---: | ---: | ---: |
| 8 / 2 | 10,53 ms | 2,81 ms | 27,09 ms | 26,80 ms |
| 64 / 16 | 211,36 ms | 0,90 ms | 430,41 ms | 310,72 ms |
| 256 / 64 | 247,85 ms | 2,35 ms | 696,65 ms | 402,41 ms |

Reconstrução fria cresce com o histórico; o cache incremental de meta não
transforma a listagem completa de histórico em O(1). Não foi introduzido um
segundo save de perfil nem escondida essa diferença de custo.

## Determinismo entre processos

Dez hosts dentro do mesmo processo não detectam o salt aleatório usado pelo
hash de strings do .NET. A certificação comparou duas execuções independentes:
na engine 27, o primeiro snapshot de combate diferia somente nas identidades
do preview da intenção inimiga e nos fingerprints derivados, apesar dos
mesmos inputs e resultados numéricos. Isso era uma falha real de determinismo,
não uma diferença de serialização irrelevante.

`EffectResultContext` agora ordena as tags antes do hash de execução. Tags de
parâmetros persistidos em payloads de stacks usam `ImmutableSortedSet` com
comparação ordinal inclusive na desserialização: o lot ID e seu snapshot não
dependem da ordem de buckets nem da cultura. Arrays de efeitos, impactos e
transações continuam preservando sua ordem semântica; a canonização JSON não
foi alterada para ordenar todos os arrays.

Um teste de permutação de buckets e outro de round-trip cobrem as fronteiras.
`CrossProcessFoundationTests` inicia três processos de verdade executando o
probe da solução: cada um usa storage temporário e comandos REST reais de
Ascendant e Volatile, incluindo crítico, intenção, payload de stacks e replay.
Comparam **18 hashes de receipts/commits** e os estados finais integralmente.
Os três processos concordaram:

- Ascendant: `7ec1ba6628c8c34333cdb559278b9ab6ec67913216eaae4d6864a3533f06bb26`.
- Volatile: `339ae385a93ead9aa7de3bdd435ef3e3ac147214700ddc1c1d2c26153bc115b4`.

O teste exige build de `HeroScript.slnx`, que também gera `Determinism.Probe`.
Ele é parte das regressões REST na CI, não um comando manual opcional.

## Gate final

A jornada de fundamentos passou em **1m46s**: dez repetições, 18 comandos
principais por tentativa, mais criação de perfis/runs e oferta da nova run.
Todos os transcripts/commits concordaram:

- Transcript: `33af571452af593db0a0a3882da4799a93acba8eb6ff801a08fd5082440366bf`.
- Estado final: `c32fd40bbc4a5506539c63186207ebf62190e3ad933d984035b6c86b25ed848a`.
- Comandos (180): p95 **199,82 ms**, máximo **508,07 ms**.
- Previews (20): p95 **81,63 ms**, máximo **107,06 ms**.

Core completo final: **1.655 aprovados**, sem skips. Godot offline: foundations,
setting_progression, combat_stage_layout, card_presentation e volatile_core
aprovados. Layout inclui 1280×720/800, 1920×1080 e 2560×1080, inglês/português
e escalas de fonte 1,0/1,2. Nenhum erro de script; aviso ObjectDB abaixo.

REST não pesado: **203 aprovados**, sem skips. As duas jornadas aprovadas
separadamente totalizam **205 testes API**, sem reduzir assertions. Builds da
solução em Debug e Release sem erros/avisos.

A campanha Volatile passou em **4m51s**, dez stores/runtimes com 55 comandos
por tentativa. Condensação e continuação causal foram realmente executadas;
receipts, commits, restart e replay concordaram. Estado final:
`6c1ee8fdb968b3b4bd74367436f6fbad203106091d29a32d2f8699ed1aee4eb0`.

Medição local sem outra jornada concorrente (820 comandos/consultas):

| Recorte | Amostras | p95 | p99 | Máximo |
| --- | ---: | ---: | ---: | ---: |
| HTTP total | 820 | 297,27 ms | 459,30 ms | 705,29 ms |
| Comandos iniciais | 150 | 93,67 ms | 202,02 ms | 588,09 ms |
| Comandos intermediários | 150 | 211,98 ms | 304,25 ms | 432,28 ms |
| Comandos finais | 250 | 429,62 ms | 522,60 ms | 705,29 ms |
| Avaliações | 270 | 207,39 ms | 289,03 ms | 319,53 ms |

Os custos crescem com o estado da campanha; neste recorte todas as respostas
medidas ficaram abaixo de um segundo. Isso não elimina os limites de histórico,
hardware e rede listados abaixo.

CI e README agora executam todos os testes REST, incluindo classes sem `Category`,
e isolam as duas jornadas longas com orçamento explícito. O workflow foi atualizado;
os resultados deste relatório são locais, não uma execução remota da CI.

A auditoria de dependências do novo probe revelou o aviso NU1903 de
Microsoft.OpenApi 3.5.3. A API usa agora o patch 3.5.4 indicado no
[advisory do mantenedor](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc).
Não foi silenciado o warning nem alterado o contrato HTTP para contorná-lo.

As medições e gates foram executados localmente no Windows, em storage
temporário. A última regressão do leitor de tags também preserva duas strings
Unicode equivalentes por collation, mas distintas ordinalmente: a leitura não
pode fundir IDs antes de normalizar o comparador.

## Limites e próximo trabalho

- Tempos in-process incluem contrato HTTP/persistência local, mas não rede real,
  hardware mínimo de jogador nem latência percebida de animações Godot.
- Não há promessa de resposta abaixo de um segundo para qualquer conteúdo ou
  tamanho de histórico; os casos medidos delimitam a evidência.
- Nos testes Godot offline permanece um aviso preexistente de duas instâncias
  ObjectDB no encerramento. Ele não é reportado como correção concluída.
- Produzir o mundo 1 ainda exige o [plano de conteúdo e balanceamento](ASCENDANT_WORLD_1_DESIGN_RESEARCH_AND_PLAN.md):
  mapa ramificado, encontros, builds, lojas/crafting, chefe e playtesting de
  duração/dificuldade. A fixture técnica não foi apresentada como ato pronto.
