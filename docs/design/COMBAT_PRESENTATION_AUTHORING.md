# Autoria da apresentação de combate

Data: 2026-10-13. Cliente: `examples/godot-engine-showcase`.

## Fronteira com a engine

A engine resolve o comando, publica o resultado e mantém o journal. A Godot
não paga custos, sorteia críticos, aplica stacks ou executa buckets. Cores,
movimento, som e ritmo não participam do RNG nem dos comandos de gameplay.

O fluxo é: resposta aceita → leitura coordenada completa → envelope contextual
→ beats causais → saída da carta → impactos → reconciliação/navegação.
Falha de leitura após aceitação mantém o último visual e bloqueia comandos;
a recuperação é uma leitura, não uma repetição da escrita confirmada.

## Perfil JSON

Arquivo: `examples/godot-engine-showcase/data/motion_manifest.json`. É configuração
de apresentação, separada do conteúdo de regras. Chaves/fallback são em inglês;
labels passam pelo tradutor do cliente.

```json
{
  "version": 1,
  "durations": {"prepare": 0.075, "card_flight": 0.16, "retain": 0.09,
    "draw": 0.18, "discard": 0.15, "staged_discard": 0.07, "create": 0.14,
    "destroy": 0.12, "stagger": 0.025, "batch_span": 0.15, "impact": 0.085,
    "float": 0.45, "zone_transfer": 0.18, "hand_transition": 0.11,
    "hover": 0.085, "watchdog": 12, "max_floats": 24, "max_transfers": 12},
  "easings": {"card_flight": {"curve": "cubic", "ease": "out"}},
  "cues": {"impact": {"sound": "impact", "label": "Impact", "color": "#ca452f"}},
  "settings": {"my-setting": {"durations": {"impact": 0.15},
    "cues": {"heal": {"sound": "heal", "label": "Recovery", "color": "#3eaa8d"}}}}
}
```

Durações são segundos, finitas e entre 0 e 64. Limites são inteiros com mínimo
efetivo 1. Campos desconhecidos são ignorados. Ausência do arquivo, raiz inválida
ou versão não suportada usa defaults limitados. Curvas aceitas: `linear`, `cubic`,
`sine`, `quad`; easings: `in`, `out`, `in_out`. Pares inválidos conservam o fallback.
O perfil é verificado na fronteira de cada apresentação; o JSON só é parseado
quando seu texto muda, inclusive em alterações do mesmo tamanho/segundo. Não há watcher que altere
tweens em curso. A geometria/lift da mão continua no `HandVisualStyle`, não nos
JSON de regras. Curvas do voo, entrada/hover da carta e transferência usam o
perfil; interpolação simples de barras/mão permanece linear.

Presets: normal respeita velocidade entre 0,25× e 2×; fast usa 2×; instant usa
duração zero. Movimento reduzido elimina deslocamento/flash intenso, mantendo
informação factual. Avanço manual é uma preferência separada. Somente uma operação
visual executável pede confirmação. Sem alvo, delta, deslocamento ou duração,
o beat termina imediatamente. Não há pausa mínima por aplicação ou lifecycle.

## Cues e informação factual

`SemanticCues` distingue impacto, cura, pagamento, ganho/perda/limite de resource,
status, modifier e transferência. Pagamento usa proveniência de custo, não nome
de recurso. Um recurso arbitrário funciona como qualquer outro. Dados sem cue
específico mantêm descrição factual neutra.

`enabled: false` no cue suprime seu feedback; o fato e a projeção continuam sendo
consumidos. Impactos aguardam tweens concretos; pagamento/status/ganhos e texto
flutuante podem terminar sem bloquear. Cancelamento também encerra resíduos e
restaura os endpoints publicados das barras e a cor dos retratos.

`CardTransitRegistry` mantém uma representação por instância. Usa a própria carta
da mão durante saída/retorno; entrada oculta usa um verso anônimo. `CardHandView`
reserva slots e suspende hover/fade local durante trânsito. Addresses são resolvidos
antes do ID: leque vem das zonas apresentadas como jogáveis, pilhas dos botões
vinculados a addresses. Não existe semântica de nome de zona no executor.

Lotes têm sobreposição e espaçamento limitado por `batch_span`. `max_transfers`
limita movimentos, com contagem compacta permitida e reconciliação final. Reordenação
usa a acomodação da mão; ausência de uma ordem intermediária autorizada usa fallback
factual. Uso e fluxo posterior reaproveitam a mesma instância visual.

Os IDs de áudio são resolvidos pelo catálogo existente; sons desconhecidos não
criam regras nem novas vozes ilimitadas. O catálogo de artes continua compartilhado
com o laboratório e as cartas do jogo. O manifesto de movimento não substitui
esses catálogos.

Em `settings.<id>.random_inputs`, mapeie somente resultados publicados:

```json
{"critical": {"label": "Critical", "checkpoints": {"guaranteed": "Guaranteed tiers"},
  "success_label": "Extra tier"}}
```

A UI mostra o checkpoint e o sucesso gravados, associados ao `impactId`. Não soma
níveis para inventar um resultado final nem converte chance >100% em tier. Esses
detalhes ficam no texto de feedback; não há uma nova badge de tier calculada na UI.

## Lifecycle e segurança

- Há um director por cursor. Handles anunciam started/impact/completed e terminam
  uma única vez: completed, skipped, cancelled ou fallback.
- Pause congela director, tweens, cenário e vozes de combate. O menu permanece ativo.
- Watchdog é proteção de executor defeituoso, não relógio normal de cada animação;
  pause e espera manual não o consomem.
- Skip altera só apresentação. Remover/trocar a view cancela ghosts/handles e invalida
  callbacks; geração do playback e epoch de navegação protegem reconciliação.
- Sessão mantém estado autoritativo. Views usam snapshots intermediários publicados
  e endpoints numéricos explícitos; modo compacto tem fallback factual.
- Transferências resolvem addresses publicados sem conhecer a finalidade da zona.
  Identidades/ordens ocultas são filtradas; cartas desconhecidas usam verso.
- Replay e timeline consultam um commit autorizado por vez. Seus cursores não
  consomem o combate vivo; o setting salvo define os cues históricos.
  Não há reconstrução exata de mãos ocultas nem som histórico nesta primeira versão.

## Orçamento e diagnóstico

O perfil limita floats simultâneos e representações de transferências; há também
até seis lanes por alvo. Sob pressão, omitimos resíduos decorativos, nunca fatos
do journal/diagnóstico. Não foi adicionado pooling sem profiling que o justifique.

`Playback.measurements()` retorna cópias defensivas de até 64 comandos: escopo,
resultado e timestamps submitted, received, synchronized, first_cue, first_impact
e blocking_completed quando disponíveis. Eles separam espera da API de ritmo
visual; não constituem benchmark de parsing, FPS ou p95 universal. Após settling,
`release_facts()` libera recibos/frames grandes. O transporte conserva suas próprias
medições. Consulte o relatório de validação antes de fazer afirmações de desempenho.

As leituras também registram `run_loaded`, `projections_loaded`, `actions_loaded`,
`evaluations_loaded` e `views_prepared`. O director expõe contadores de dispatch,
operações instantâneas e cessões do orçamento de CPU (4 ms por passagem). Uma cessão
não cria timer/handle; serve para preservar o quadro sob custo real de processamento.

No laboratório `scenes/card_hand_lab.tscn`, selecione a operação e use **Play motion**.
Há 14 fixtures, presets normal/fast/instant, avanço manual e skip. Os mesmos executores
do combate consomem fatos sintéticos; nenhuma regra ou chamada REST é executada ali.

## Testar

Execute `tools/test-godot-ui.ps1` em PowerShell 7. O helper copia o projeto e usa
dados de usuário isolados. O teste `animation_live` usa adapters/views reais com
respostas sintéticas atrasadas, sem escrever em partidas pessoais. Não substitui
um playtest conectado aos três settings.

Teste conectado opt-in (cria tentativas próprias de QA no servidor indicado):

```powershell
./tools/test-godot-ui.ps1 -Tests animation_online -ApiUrl http://127.0.0.1:5271
```

Os agregados históricos e as projeções vivas passam pelo adaptador puro
`contracts/snapshot_projection.gd`. Ele adapta dicionários/listas e extrai
resources/status gravados; nunca calcula ticks ou resultados de regras.
