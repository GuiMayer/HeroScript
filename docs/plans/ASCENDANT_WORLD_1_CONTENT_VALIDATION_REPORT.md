# Ascendant — conteúdo do primeiro mundo: entrega e validação

Data: 2026-10-10. Engine: 28. Estado: **alpha jogável para playtest**, não
balanceamento certificado nem gate final do plano de design.

## Escopo entregue

O catálogo `ascendant` anuncia `ascendant_world_one_run` / `ascendant_world_one`.
O conteúdo é composto pelos arquivos `Resources/*/world_one.json` do package;
a interface recebe as regras exclusivamente pela REST API. O showcase antigo
permanece como fixture e saves anteriores não são migrados implicitamente.

| Área | Conteúdo publicado |
| --- | --- |
| Cartas | 30 definições; 28 inicialmente elegíveis, duas alternativas desbloqueáveis |
| Transformações | 12; upgrades de base e slots de afinidade/comportamento limitados |
| Relíquias | 8; escolhas de rota e recompensas específicas de elites |
| Adversários | 8 comuns, 3 elites e 1 chefe com dois ciclos por vida e pressão no round 12 |
| Percurso | 14 paradas principais, 9 combates por caminho, 12 templates comuns |
| Serviços | Ofertas seedadas com recusa, loja, forjas limitadas e recuperação de 25 vida |
| Eventos | 5 diálogos com escolhas, custo/oportunidade e efeitos de proprietário explícito |
| Recursos | Vida persistente 100, energia 3 por ativação, mão 5; ouro reservado aos serviços |
| Perfil | Duas opções laterais desbloqueáveis; sem aumento permanente de poder |
| Godot | Mesmos componentes de cartas/mão/inspector, nomes e descrições EN/PT, risco e custos nos metadados |

As famílias de crítico, DoT, condensação, multi-hit e defesa possuem ferramentas
e pontes no catálogo. Isso é cobertura de conteúdo, **não prova** de que todas
tenham desempenho equilibrado em uma tentativa completa.

## Ajustes descobertos durante a integração

1. Uma oferta opcional aberta não anunciava mais a recusa. `RunProgression`
   agora mantém `RESOLVE_NODE` disponível se a atividade for opcional; uma
   seleção obrigatória continua sem essa saída. Duas regressões cobrem isso.
2. A pipeline padrão precisava usar `scalar`, unidade dos efeitos simples de
   defesa/cura e das ações correspondentes. Os ataques críticos tipados têm
   pipeline `points` explícita.
3. Frações de origem, como `10 × 0,75`, não cabiam em um orçamento distribuído
   com `quantum: 1`, invalidando algumas ações da IA. O JSON agora arredonda o
   orçamento no fim da origem, antes da distribuição; o residual conserva
   sua potência fracionária. Não foi adicionado fallback oculto ao executor.
4. Desempate de iniciativa por ID fazia o oponente agir antes do jogador na
   entrada. A regra publicada passou a `PlayerControlledFirst`.
5. O cliente passa a preferir nomes/descrições publicados nos metadados,
   preservando fallback de apresentação e navegação pela autoridade da API.
6. O launcher exige também o launch atual do Ascendant, evitando reutilizar
   silenciosamente um host que ainda anuncia a campanha antiga.

## Evidências técnicas

### Catálogo e invariantes

- 1.660 testes do Core aprovados, sem falhas ou testes ignorados.
- Compilação do setting e validação do grafo completo.
- Enumeração de todos os caminhos: chegam ao chefe, têm nove combates,
  14 paradas, loja, oportunidades de crafting/recuperação e rota sem elite.
- Todas as 12 transformações aplicam às cartas declaradas elegíveis, sem
  modificar a definição-base.
- Todos os 30 containers de efeitos executados pelos serviços de produção,
  com comparação determinística das saídas e preservação dos inputs.
  Multi-hit registra scopes distintos; condensação produz registros reais.
- Todas as habilidades declaradas pelos 12 adversários continuam executáveis
  sob Weakened (redução de 25%), incluindo ciclos normais/enfurecidos do chefe.
- 205 regressões da API aprovadas, sem falhas/ignorados, incluindo esses dois
  testes de mecânicas. A jornada publicada foi executada separadamente e passou.
  As duas jornadas longas anteriores dos fundamentos/core volátil não foram
  repetidas nesta rodada; sua certificação prévia permanece separada.

### Jornada publicada pela API

`AscendantWorldOneJourneyTests`, seed 154, perfil novo, sem cheats/free draw,
usa apenas comandos REST canônicos. A política escolhe por preview de dano e
defesa, prefere algumas cartas de crítico e percorre a rota sem elite.

- Nove vitórias, 187 comandos aceitos, conclusão do ato.
- Chefe vencido no round 8; vida final 42/100.
- Recusa após abrir uma oferta, transformações, aquisição de relíquias,
  descanso de 25 e transporte de vida entre combates verificados.
- Replay semântico aprovado; host reiniciado e snapshot final comparado por
  hash canônico, sem depender da ordem de propriedades de JSON.
- Neste ensaio local: p95 de comando **833,67 ms**, máximo **1.186,46 ms**.
  Medição em processo via servidor de teste, incluindo envio/persistência;
  não inclui leitura seguinte, rede real nem reprodução/animação da Godot.
  Não representa garantia de todos os comandos abaixo de um segundo.

O percurso desse teste não compra na loja, não escolhe eventos ou elites e não
prova os cinco arquétipos. A cobertura estrutural desses conteúdos é distinta
de uma jornada funcional observada em cada um deles.

### Godot

- Importação/editor headless sem erros de parse.
- `world_one.gd`: 30 nomes em ambos os idiomas, fallback de conteúdo externo,
  risco/descrição e ausência de autoridade de navegação no cliente.
- `card_presentation.gd` e `foundations.gd`: aprovados.
- `combat_stage_layout.gd`: aprovado em 1280×720, 1920×1080 e 2560×1080,
  EN/PT, fontes 1,0/1,2, mão vazia, seleção, sidebar flutuante e pause.
- Esses são testes headless de apresentação/contratos. Não foi realizada
  uma partida humana completa do ato na Godot nesta entrega.
- Os testes headless ainda relatam dois objetos ObjectDB no encerramento,
  aviso preexistente, não falha de gameplay e não tratado como resolvido.

## Decisões e limites explícitos

- Condensação usa **150% da potência atual somada** e consome os lotes. Não
  agrega todos os ticks futuros ponderados por duração restante; adicionar
  esse modelo exige um agregado genérico novo, não números inventados na UI.
- A forja remove/substitui transformações. Remoção permanente paga de uma
  carta do deck não foi implementada nem anunciada como serviço disponível.
- Relíquias são escolhas/recompensas específicas: não foi simulado um sistema
  de ofertas aleatórias de relíquias que a atividade atual não fornece.
- O grafo tem ramificações reais com convergência, não geração procedural.
  O cliente atual mostra os nós na lista/rota compartilhada; um mapa amplo
  de colunas/linhas ramificadas é uma melhoria visual posterior.
- Chaves/fallback e regras em inglês; português apenas na apresentação.
- GDD core e conceitos legados permanecem intocados.

## Gates ainda abertos

1. Banco de pelo menos 50 seeds com políticas baseline, cinco famílias e
   híbridos; medir perda de vida, escolhas, economia e casos de derrota.
2. Exercitar compras/rerolls, os cinco eventos e as três rotas elite em
   jornadas publicadas completas, além da validação estrutural.
3. Dez execuções idênticas do ato publicado comparando todos os hashes/fatos,
   além da certificação anterior dos fundamentos e do replay deste ensaio.
4. Retomada no meio do ato após transformação, branches e desbloqueios do
   catálogo novo em vitória/derrota/abandono; serviços genéricos já têm
   regressões, mas isso não substitui uma matriz própria do ato.
5. Playtests humanos para confirmar meta de 25–40 minutos, decisões claras,
   cinco famílias viáveis e chefe relevante sem builds obrigatórias.
6. Latência útil de ponta a ponta na máquina-alvo, com Godot, sessão longa,
   host frio/quente e animações medidas separadamente.

Por isso, etapas de autoria estão entregues, mas os gates de balanceamento e
publicação final das etapas 11/12 do plano permanecem abertos.

## Como jogar a versão nova

Reinicie usando `examples/godot-engine-showcase/Start-Demo.ps1`, selecione
**Ascendant Matrix** no menu e inicie **Nova run**. Continuar uma tentativa
antiga conserva sua revisão e seu percurso original.

Referências: [catálogo e contratos](../content/ASCENDANT_WORLD_ONE.md),
[plano de design](ASCENDANT_WORLD_1_DESIGN_RESEARCH_AND_PLAN.md),
[certificação da engine](ENGINE_WORLD_1_FOUNDATIONS_VALIDATION_REPORT.md).
