# Elegibilidade de conteúdo por run

Disponível na engine `25`, commit schema `4`, meta schema `1` e capability
`frozen-run-eligibility`. Implementa a etapa 7 dos fundamentos do mundo 1.

## Autoridade

O modo referencia uma [política de progresso](profile-progress-policies.md).
Os alvos desse catálogo são as opções gated: `Card`, `CardUpgrade` e `Relic`.
Conteúdo fora desse conjunto continua disponível; um modo sem política não
recebe gating implícito. Desbloqueios disponibilizam opções, não poder permanente.

O serviço de criação resolve jogador, setting e revisão antes de inicializar
cartas e efeitos. Captura `RunEligibilitySnapshot` com política/fingerprint,
origem, sequência/revisão do perfil, IDs de unlocks, alvos gated/disponíveis e
`effectiveFingerprint`. Coleções são únicas e ordenadas canonicamente.

O fingerprint efetivo incorpora escopo, conteúdo, política, regras e alvos.
Sequência/revisão do perfil e a lista histórica de unlocks são provenance,
não alteram a identidade efetiva se a disponibilidade continuar igual.
Grants são interpretados pelos **alvos registrados na prova**: reutilizar um
unlock ID para outro alvo não libera esse alvo por uma prova antiga.

A captura precede a contribuição de `AttemptStarted`. Um unlock obtido nesse
primeiro commit vale para tentativas futuras, não modifica a captura atual.
Nenhuma consulta, menu ou comando recaptura o perfil vivo.

## Configuração do modo

`eligibilityRules` é opcional. Com política de progresso e sem regras explícitas,
os defaults são `Profile` e `Reject`. Exemplo de desafio com catálogo fixo:

```json
{
  "profileProgressPolicyId": "support_options",
  "eligibilityRules": {
    "source": "Fixed",
    "fixedUnlockIds": ["support_link"],
    "emptyPools": "AllowEmpty"
  }
}
```

Os IDs precisam existir no catálogo publicado; não são inputs do jogador.

| Source | Entrada de disponibilidade |
| --- | --- |
| `Profile` | Provas autoritativas do jogador nesse setting, lidas apenas na criação. |
| `Fixed` | IDs declarados no modo; não consulta o perfil. |
| `Unrestricted` | Todos os alvos do catálogo; exige modo com cheats de cartas ou recursos explicitamente habilitados. |

Regras sem política, enums inválidos, IDs desconhecidos/duplicados e lista
fixed fora de `Fixed` são rejeitados na publicação/resolução.

`emptyPools: Reject` rejeita uma atividade que exige ofertas mas fica sem
opções elegíveis. `AllowEmpty` aceita nenhuma oferta: a seleção vazia fica
concluída, lojas não inventam mercadorias e preparações vazias podem ser
resolvidas. Nunca há carta fallback adicionada fora do catálogo. Pools são
filtrados antes da ordenação/sorteio; fingerprints incluem a elegibilidade.

## Todos os caminhos de aquisição

A mesma captura é consumida por deck inicial, recompensas, lojas, rerolls,
transformações, preparações, relíquias e criação de cartas por fluxos de zonas.
Inclui triggers de inicialização/fronteira, efeitos e ferramentas. Alterar
quantidade/stack também não autoriza adquirir uma opção bloqueada.

Há validação antes da criação em fluxos e uma barreira final compartilhada
antes do append canônico. Lotes validam cada candidato intermediário: criar
uma carta bloqueada e destruí-la no mesmo lote não evita a proteção.
Remover um upgrade, movimentar ou jogar uma instância existente não é nova
aquisição. O snapshot de elegibilidade não pode ser substituído por comandos.

## Criação e retry REST

`POST /api/v1/runs` exige `settingId`, `contentRevision` e `modeId`. Recomenda-se
informar `commandId` UUID para cada tentativa e reutilizá-lo em retries:

```json
{
  "settingId": "ascendant",
  "contentRevision": "<revisão publicada>",
  "modeId": "ascendant_showcase",
  "runDefinitionId": "<run publicada>",
  "playerEntityId": "player",
  "seed": 150,
  "commandId": "4f85c2ed-c9ec-4927-a910-cdb096092715"
}
```

- Mesmo comando e mesmos inputs: retorna o estado original da criação,
  sequência 1, incluindo a mesma captura; não relê o perfil nem conta tentativa.
- Mesmo comando com inputs diferentes: rejeita com 400.
- Outra tentativa com a mesma seed: informe um novo `commandId`.
- Sem `commandId`, mas com seed: inputs idênticos recuperam a criação original.
- Sem seed e sem `commandId`: cada POST cria uma tentativa nova, com nonce/seed
  capturados. Esse formato não oferece retry seguro após timeout.

O retry de criação **não é continuar a partida** e não retrocede o save.
Use `GET /api/v1/runs/{runId}` para o estado atual. O relaunch de sandbox
recupera o encontro atual, sem substituí-lo pelo estado da criação.

O read model expõe `eligibility` e `creationReceipt` (`key`,
`requestFingerprint`). O comando raiz usa o `commandId` informado.
`capturedEligibility`, `eligibility` ou listas de unlocks não são aceitos no
request público; campos desconhecidos são rejeitados. O controller só traduz
o request: a engine resolve os fatos confiáveis.

## Persistência, replay e branches

A receipt reside no primeiro commit, não em um segundo save. Lease operacional
`_creation-leases/<key>.lease` serializa criação/retry entre hosts no mesmo
store. Não contém estado autoritativo. Caches LRU de receipts/localizadores são
limitados e reconstruíveis; a leitura correspondente valida o commit original.

Replay efêmero usa os inputs capturados no `START_RUN`, sem consultar o perfil
atual e sem publicar contribuições. Branches e simulações herdam a elegibilidade;
uma branch não herda a receipt de criação da run raiz. O gate transversal de
restart/replay/hotreload/provenance será ampliado na etapa 9.

Hotreload não pode mudar os alvos gated de uma run nem remover conteúdo
disponível capturado. Alterar condições mantendo o mesmo catálogo não
recaptura disponibilidade; novas condições dizem respeito a futuras provas.
Ativação incompatível falha antes de publicar estado parcial.

Versões de execução antigas não são migradas silenciosamente. Seus bytes são
preservados; provas meta schema `1` continuam legíveis independentemente da
disponibilidade do runtime histórico. Não há concessões retroativas implícitas.

## Limites desta entrega

A apresentação de disponibilidade/perfil foi entregue na etapa 8:
[contrato REST/Godot](../api/world-one-foundations.md).
Certificação com histórico grande, reconstrução fria e latência pertence à
etapa 10. Os testes usam stores isolados, nunca os saves reais do jogador.
