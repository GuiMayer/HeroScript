# Apresentação de combate — validação

Data: 2026-10-13. Implementação guiada pelo
[plano](GODOT_COMBAT_ANIMATION_ARCHITECTURE_AND_PLAN.md).

## Resultado

Um único fluxo canônico de apresentação coordena sincronização, saída da carta,
impactos, pause, cancelamento, skip e navegação. Timeline/replay reutilizam
planner/director/feedback por comando autorizado. Regras, RNG e persistência da
engine não foram modificados nesta implementação.

Na integração real surgiu uma falha não coberta pelas fixtures iniciais: atores
e cartas dos agregados persistidos são dicionários, enquanto a projeção de combate
usa listas. `contracts/snapshot_projection.gd` centraliza a adaptação defensiva,
preservando ordem publicada, components de resources e status. Gateway e
presenters reutilizam o adaptador; não há cálculo de regras na transformação.

## Testes automatizados

- Suíte Godot principal: importação nova e 37 scripts, 763 checks aprovados.
  Sem erros de script ou vazamentos reportados; somente os avisos intencionais de
  watchdog e composição inválida. Fixtures isoladas, sem preferências/saves pessoais.
  Evidência local: `.runtime/ui-refactor/ce2633ada28e4975b89fd473f8be6d74/validation.json`.
- Suíte adicional: 625 checks em seleção de cartas (22), layout de combate (441),
  pause/layers (24), mão (37), menus marfim (90) e controller (11); todos passaram.
- Integração Godot com API real: 26 checks, todos passaram, sem diagnóstico de
  script. Campanha/combate/uso de carta em default, ascendant e volatile-core;
  snapshot autoritativo inalterado pela animação e replay canônico válido em todos.
  Sandbox development_lab: commit histórico autorizado e playback compartilhado.
- Core: 54 testes filtrados de replay, zonas de cartas e resolução; todos passaram.
- API controllers/políticas: 7 testes, todos passaram.
- Suíte ampliada de API (incluindo superfícies de ferramentas e replay): 23 testes
  passaram antes do limite global de 300 s abortar a execução. Não é uma suíte
  completa aprovada. Não houve falha de assertion reportada, mas o timeout impede
  afirmar sucesso dos testes restantes.

Regressões específicas: leitura atrasada depois da aceitação, saída única da carta,
último beat bloqueante, confirmação repetida, pause de voo/flash, seleção neutra,
skip sem comando, resultado terminal esperando apresentação e destruição durante
await. Planner mantém A1/B/A2; pagamentos não provocam ferimento; zonas/resources
arbitrários e dados ocultos possuem testes de contrato.

A carga sintética de 1.000 aplicações preserva fatos e limita floats a seis por
alvo, liberando nós decorativos após dispose. Isso é um teste de limites/lifecycle,
não um benchmark de 60 FPS nem prova sobre todas as combinações possíveis.

## Renderização e inspeção

Teste nativo `animation_live`: 13 checks por resolução, em 1280×720, 1920×1080 e
2560×1080. Capturas de saída da carta, pause no voo e reconciliação final em
`.runtime/animation-certification/<resolução>/`. Inspeção direta de saída em 720p,
pause em 1080p e estado final ultrawide: modal acima das cartas, apenas uma
representação da carta em trânsito e nenhuma carta consumida após settling.

As capturas são fixtures mínimas com ator neutro, não imagens de arte/conteúdo
final dos três settings. O teste de layout percorre inglês/português, quatro
resoluções e escalas de texto 1,0/1,2. Não fizemos inspeção visual de todas as
combinações de contraste, idioma, setting e hardware; áudio foi validado por
estado/vozes/cues, não por aprovação subjetiva do mix.

Uma primeira importação isolada terminou com crash nativo no shutdown
(-1073741819), sem erro de script. A repetição da importação isolada passou, e a
importação da cópia final nova também passou (8.127 ms); a suíte executou sem erros.
Esse incidente não é tratado como uma importação aprovada
na primeira tentativa nem como evidência de defeito específico do código.

## Tempos observados

Três comandos reais no preset rápido, segunda execução após a correção histórica:

| Setting | POST até recibo | Leituras até sincronizar | Total até sincronizar | Sincronizado até conclusão visual |
| --- | ---: | ---: | ---: | ---: |
| default | 137 ms | 768 ms | 905 ms | 239 ms |
| ascendant | 158 ms | 856 ms | 1.014 ms | 229 ms |
| volatile-core | 144 ms | 708 ms | 852 ms | 229 ms |

Na primeira execução, o maior total até sincronizar foi 1.147 ms. Portanto o alvo
de sincronização sempre inferior a 1 s **não está certificado**. A apresentação
não depende desse tempo para sua causalidade. Seis comandos não justificam p95
global, comparação de throughput ou uma promessa de latência. Parsing, custo do
planner, FPS e término cosmético não receberam profiling dedicado; os timestamps
implementados cobrem os milestones descritos no guia de autoria.

## Dados de QA

A integração cria novas tentativas, sem abrir/alterar saves já existentes. Duas
execuções criaram seis campanhas e dois sandboxes de QA na API local fornecida
pelo usuário; eles permaneceram persistidos, sem remoção automática. As campanhas
da execução aprovada são:

- default: `10d7d5de-f593-8ce3-bde8-7074d86c2e23`;
- ascendant: `01a5de65-fb72-8244-80d4-74f606b69033`;
- volatile-core: `cd30f44e-902f-88a9-b887-62109532d1d4`.

O teste conectado é opt-in (`animation_online`), fora da suíte offline padrão.

## Limites e acompanhamento

- Sem benchmark de renderização/p95 completo, soak prolongado ou playtest real
  com gamepad e todas as famílias de build até vitória/derrota.
- Modo compacto permanece factual: não reconstitui mãos/status intermediários
  ausentes nem deduz morte a partir de resource. Replay histórico ainda não toca
  som ou reproduz mãos ocultas; usa atores/feedback e fatos disponíveis.
- Críticos mostram checkpoints/sucessos publicados; não há tier total calculado
  no frontend. Fontes/continuações permanecem no feedback factual/proveniência,
  sem uma nova arte específica para cada efeito.
- Manifesto captura durações/curvas/cues na fronteira; geometria e lift continuam
  nos resources visuais próprios. Sem watcher que altere um comando em curso.
- Ampliar a certificação, concluir a suíte API abortada e perfilar as leituras de
  sincronização são acompanhamentos explícitos; não foram escondidos como concluídos.

Autoria/configuração: [guia](../design/COMBAT_PRESENTATION_AUTHORING.md).
