# Progresso e desbloqueios por setting

Disponível na engine `24`, com `RunCommit` schema `4` e capability
`configurable-profile-progress`. É a etapa 6 dos fundamentos do mundo 1.

## Autoridade e limites

A política de progresso do perfil é distinta da política de fluxo da run.
O modo seleciona `profileProgressPolicyId`; a definição resolvida fica pinada
em `ResolvedGameMode.ProfileProgressPolicy`, junto da revisão do conteúdo.
Sem essa referência não há desbloqueios automáticos, mesmo após uma vitória.

Um desbloqueio disponibiliza uma **opção**: carta, modificação de carta ou
relíquia. Não modifica atributos permanentes, recursos ou dano do personagem.
O histórico ainda apresenta os badges `first-run` e `first-completion`; eles
não são regras de disponibilidade de conteúdo.

**A filtragem das ofertas/deck inicial ainda pertence à etapa 7.** Esta etapa
registra o progresso e suas provas; o exemplo Ascendant não restringe ofertas
antes da implementação do snapshot de elegibilidade. A interface detalhada
de disponibilidade pertence à etapa 8.

## JSON

O kind canônico é `profile-progress-policies`. Incluir seu artefato no
`package.json`, como qualquer outro conteúdo publicado. Exemplo:

```json
{
  "support_options": {
    "profileProgressPolicyId": "support_options",
    "contributingModeIds": ["campaign"],
    "allowedProvenances": ["Published"],
    "deduplication": "RootLineage",
    "unlocks": [{
      "unlockId": "support_link",
      "displayName": "Support link",
      "targets": [{ "kind": "CardUpgrade", "definitionId": "support_link_upgrade" }],
      "condition": {
        "kind": "All",
        "conditions": [
          { "kind": "AttemptStarted", "count": 2 },
          { "kind": "EncounterCompleted", "runDefinitionId": "world_one", "nodeId": "calibration", "encounterOutcomes": ["VICTORY"] }
        ]
      }
    }]
  }
}
```

Todos os IDs do exemplo devem existir no setting. A publicação valida modos,
alvos, runs, nós de encontro e dependências; rejeita ciclos, campos ambíguos,
enums inválidos e condições excessivas (256 unlocks, profundidade 8, 2.048 nós).
O catálogo executável do showcase é
`data/configs/ascendant/Resources/profile-progress-policies/ascendant_options.json`.

## Condições tipadas

| Kind | Evidência autoritativa |
| --- | --- |
| `AttemptStarted` | Primeiro commit de uma run raiz aceita e persistida. |
| `RunCompleted` | Transição de `Active` para `Completed`. |
| `RunOutcome` | Transição terminal; exige `outcomes` explícitos. |
| `NodeCompleted` | Novo ID em `Map.ResolvedNodeIds`; exige run e nó explícitos. |
| `EncounterCompleted` | Encontro promovido a `Resolved`; exige run, nó e resultados aceitos (vitória por padrão). |
| `UnlockGranted` | ID já concedido, incluindo dependências satisfeitas no mesmo commit. |
| `All` / `Any` | Composição de `conditions`, sem filtros/counters de folha. |

`count` é inteiro positivo (1 por padrão). Folhas de tentativa/run podem filtrar
`runDefinitionId`. Não há análise de texto, telemetry, relógio ou RNG para
decidir condições. Finalizar o combate sem executar sua resolução canônica
ainda não registra `EncounterCompleted`.

## Provenance e deduplicação

Por padrão apenas os modos listados com provenance `Published` contribuem.
Branch tem provenance `Branch`; cenário é `Sandbox`; modo com cheats de
recursos/cartas é `Experimental`, mesmo se declarar `Published`. O modo pode
declarar `profileProgressProvenance` como `DevModder`, `Sandbox` ou
`Experimental`. Modos normais não se tornam dev só porque o host permite
ferramentas administrativas. `allowedProvenances` pode autorizar essas origens
explicitamente. **Simulações internas nunca contribuem.**

`RootLineage` conta o mesmo fato/run/nó uma vez por linhagem; `RunInstance`
permite contar a conclusão separadamente por branch quando `Branch` está
explicitamente autorizada. Criar uma branch copiando o passado nunca conta
as conclusões herdadas como novas nem registra uma nova tentativa raiz.

Troca de conteúdo/capabilities em runtime e a certificação transversal de
provenance/hotreload pertencem ao gate da etapa 9. As regras de configuração
não devem ser confundidas com migração implícita de histórico anterior.

## Commit, concorrência e prova

O `FileRunCommitStore` continua sendo a única implementação de armazenamento
autoritativo. Seu coordenador de progresso extrai fatos comparando os estados
canônicos, reduz as condições e acrescenta `RunCommit.ProfileProgress` **antes
do mesmo append atômico**. `RunManager` usa o commit efetivamente retornado na
receipt. O hash do estado de gameplay não incorpora o perfil atual: a decisão
entre runs possui sua própria entrada externa capturada.

Cada delta contém jogador/setting, sequência e revisão anterior do perfil,
política completa pinada, revisão do conteúdo, run/seq/comando, novas
contribuições e grants. Um grant contém ID estável por jogador/setting/unlock,
fingerprint da política, contagens das condições e referências à base/delta.
Nenhuma prova precisa conhecer o próprio hash final do commit.

Uma lease por jogador/setting serializa a leitura da base e o append, inclusive
entre processos no mesmo armazenamento local. A redução verifica a sequência
e revisão esperadas. Retry do comando recupera a prova original antes de
reduzir novamente; falha de persistência não publica progresso. A ordem entre
duas runs concorrentes é uma entrada externa registrada pela cadeia do perfil,
não uma promessa de que o agendamento dos processos seja determinístico.

O arquivo `_profile-progress/<scope>.lease` só contém um token operacional de
invalidação de cache. É atualizado e sincronizado **antes** do append para
outros processos reconstruírem após falha/crash. Não contém counters/grants,
não entra em hashes determinísticos e pode ser descartado durante manutenção
com os hosts parados. Não é um segundo save autoritário.

O cache LRU limita a quantidade de perfis residentes. Sem cache, a projeção
filtra streams pelo checkpoint inicial e reduz envelopes sem reconstruir cada
delta de gameplay. Comandos sem novos fatos de progresso preservam os bytes
preparados e não adquirem a lease nem leem o histórico do perfil. Certificação
com históricos grandes continua na etapa 10; não é garantia de latência aqui.

Grants históricos não são revogados ou reescritos por uma nova definição.
Excluir streams com contribuições é recusado: apagar uma run não pode remover
silenciosamente a prova de progresso. Não há endpoint para forjar grants.

## Leituras e replay

`GET /api/v1/profiles/{playerId}?settingId=...` retorna `progressSequence`,
`progressRevision`, IDs em `unlocks` e provas em `unlockProofs`. Estatísticas e
histórico continuam separados da cadeia de progresso. `/unlocks` conserva a
lista de IDs; sua apresentação completa será expandida na etapa 8.

O rebuild aplica cada delta na sequência capturada, verificando base, condições,
grants e revision hash. Consultas não concedem novos unlocks. Replay executa um
runtime efêmero, sem armazenamento autoritário: verifica gameplay e não
republica contribuições. A validação transversal de bases históricas,
compatibilidade/hotreload/branches será ampliada na etapa 9.

Saves incompatíveis de execução anterior permanecem preservados; não são
reinterpretados pela política nova. Provas de progresso com schema meta `1`
continuam legíveis independentemente da disponibilidade do runtime histórico:
o leitor extrai apenas o envelope meta e valida sua cadeia, sem executar ou
migrar os estados de gameplay antigos. Histórico anterior sem provas não vira
uma concessão retroativa. Criação repetida com inputs determinísticos
iguais ainda retorna `Run already exists`; a receipt idempotente de criação
com elegibilidade capturada pertence à etapa 7.
