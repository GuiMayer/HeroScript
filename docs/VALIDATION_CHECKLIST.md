# Checklist de validação do HeroScript

**Contrato validado:** API v1, runs determinísticas e recursos genéricos

**Atualizado em:** 2026-09-05

Use este documento antes de integrar um cliente, publicar conteúdo ou entregar uma
versão. O contrato de máquina está em `/openapi/v1.json`; exemplos narrativos ficam
em [docs/api](api/README.md).

## 1. Gate automatizado

- [ ] A solução compila sem erro.
- [ ] `Core.Tests` passa integralmente.
- [ ] `API.Tests` passa integralmente.
- [ ] A execução não cria diferenças em arquivos versionados.
- [ ] `git diff --check` não encontra whitespace inválido.

```powershell
dotnet build HeroScript.slnx
dotnet test tests/Core.Tests/Core.Tests.csproj --no-build
dotnet test tests/API.Tests/API.Tests.csproj --no-build
git diff --check
```

Não fixe no documento uma contagem de testes: novos cenários devem poder ser
adicionados sem tornar o checklist falso.

## 2. Saúde e contrato HTTP

- [ ] `GET /api/v1/health/live` retorna sucesso com o processo ativo.
- [ ] `GET /api/v1/health/ready` confirma dependências carregadas.
- [ ] `GET /api/v1/version` expõe a versão da engine.
- [ ] `GET /api/v1/capabilities` permite ao cliente descobrir funcionalidades.
- [ ] `GET /openapi/v1.json` corresponde aos controllers publicados.
- [ ] Erros usam `application/problem+json` e um código estável.
- [ ] Não existem rotas de gameplay fora de `/api/v1`.

## 3. Run determinística

- [ ] `POST /api/v1/runs` cria uma run com seed explícita.
- [ ] O estado contém `runId`, `sequence`, `step`, `contentRevision` e hash.
- [ ] `GET /api/v1/runs/{runId}` devolve o snapshot persistido.
- [ ] `GET /map` e `/available-commands` refletem o estado atual.
- [ ] Um comando com versões observadas é aceito.
- [ ] Repetir exatamente `commandId`, tipo e payload devolve o mesmo recibo.
- [ ] Reutilizar `commandId` com payload diferente é rejeitado.
- [ ] Versão concorrente incorreta retorna `409` sem alterar a run.
- [ ] Violação de regra retorna `422` sem alteração parcial.

Exemplo mínimo:

```http
POST /api/v1/runs
Content-Type: application/json

{
  "configName": "default",
  "runDefinitionId": "default_run",
  "playerEntityId": "player",
  "seed": 424242
}
```

Toda mutação posterior usa:

```http
POST /api/v1/runs/{runId}/commands
```

com `commandId`, `expectedSequence`, `expectedStep`, `type` e `payload`.

## 4. Início de combate

- [ ] `START_ENCOUNTER` cria o encontro dentro da run.
- [ ] `entityId` é tratado como alias da instância.
- [ ] `definitionId` é resolvido na revisão fixada pela run.
- [ ] Dois aliases podem usar a mesma definição.
- [ ] Aliases duplicados são rejeitados.
- [ ] Definição desconhecida é rejeitada atomicamente.
- [ ] Overrides aceitam qualquer recurso presente na entidade.
- [ ] Participante ou recurso desconhecido no override é rejeitado atomicamente.
- [ ] O journal preserva os participantes materializados.

```json
{
  "commandId": "54dc2f17-37cb-4e77-91ca-8464367f49e6",
  "expectedSequence": 1,
  "expectedStep": 1,
  "type": "START_ENCOUNTER",
  "payload": {
    "hero": { "entityId": "player", "definitionId": "player_warrior" },
    "enemies": [
      { "entityId": "enemy_1", "definitionId": "enemy_goblin" }
    ],
    "initialResourceValues": {
      "player": { "energy": 3 }
    }
  }
}
```

Não valide `POST /api/combat/start`: essa rota não faz parte do contrato.

## 5. Comandos de combate

- [ ] `GET /api/v1/combats/{combatId}` expõe o read model atual.
- [ ] `POST /api/v1/combats/{combatId}/commands` aceita apenas `PLAY_CARD`,
  `EXECUTE_ACTION` e `END_TURN`.
- [ ] `expectedSequence` e `expectedStep` são obrigatórios.
- [ ] Uma carta ausente da mão é rejeitada sem pagar custos.
- [ ] Alvo, ator, fase e orçamento de ações são validados pela engine.
- [ ] Custos compostos são pagos em uma única transação.
- [ ] Falha em qualquer efeito preserva recursos e zonas de carta anteriores.
- [ ] A resposta inclui o snapshot aceito e a resolução durável.
- [ ] A Godot pode recuperar a resolução em `/resolutions/{commandId}`.

## 6. Recursos genéricos

- [ ] `GET /api/v1/content/resources` lista definições da revisão.
- [ ] Entidades expõem um dicionário `resources`, sem projeções numéricas paralelas.
- [ ] Um ID arbitrário, como `stability`, materializa, muda e serializa corretamente.
- [ ] `DAMAGE`, `HEAL` e `MODIFY_RESOURCE` exigem `targetResource`.
- [ ] Dano pode reduzir um recurso diferente de `health`.
- [ ] Custos podem consumir qualquer recurso configurado.
- [ ] Alterações de `Current`, `Minimum` e `Maximum` usam o mesmo redutor.
- [ ] Lote inválido não aplica mutações anteriores do mesmo lote.
- [ ] Valores não finitos são rejeitados.
- [ ] Clamp respeita `canBeNegative` e `canExceedMax`.
- [ ] `ResourceCategory` não decide derrota.
- [ ] Somente uma `thresholdPolicy` com `DefeatOwner` derrota o proprietário.

## 7. Cálculos e ciclo de vida

- [ ] Fórmulas reconhecem `source.resources.<id>.*` e
  `target.resources.<id>.*`.
- [ ] Regeneração reconhece `resources.<id>.*` e aliases do pool atual.
- [ ] Pipelines usam apenas buckets e canais publicados na revisão.
- [ ] `resourceInfluenceBindings` valida recurso, scope, campo, canal e bucket.
- [ ] A mesma carta produz o mesmo trace na prévia e na execução.
- [ ] Regenerações de começo e fim de turno geram aplicações auditáveis.
- [ ] Eventos de recurso são publicados somente depois do commit da run.

## 8. Cartas e inspeção

- [ ] `GET /api/v1/combats/{combatId}/cards/evaluations` evita N+1 para a mão.
- [ ] A avaliação individual informa custos, alvos, upgrades e influências.
- [ ] `isPlayable` considera condição, custo, alvo, ator, fase e orçamento.
- [ ] A prévia não altera a run.
- [ ] Upgrade altera o container efetivo, sem se confundir com scaling contextual.
- [ ] A definição da carta nunca é aceita do cliente durante `PLAY_CARD`.

## 9. Timeline, branches e replay

- [ ] `/journal` preserva a ordem completa de comandos.
- [ ] `/timeline` lista pontos de inspeção coerentes.
- [ ] Consultar estado histórico não altera a branch atual.
- [ ] Criar branch preserva a origem e não reescreve o histórico.
- [ ] Duas branches derivadas podem evoluir independentemente.
- [ ] `POST /api/v1/runs/{runId}/verify` reproduz e confirma o hash final.
- [ ] `POST /api/v1/combats/{combatId}/verify` confirma o encontro.
- [ ] Alteração de arquivo após a criação da run não muda seu replay.

## 10. Conteúdo e hot reload

- [ ] Validação rejeita referências quebradas antes da publicação.
- [ ] Publicação produz uma nova revisão imutável.
- [ ] Uma run existente continua ligada à revisão original.
- [ ] Reload administrativo exige a configuração e autenticação apropriadas.
- [ ] Modos sem hot reload não recebem conteúdo novo implicitamente.
- [ ] Se um modo permitir ativação em runtime, a troca é explícita e registrada.

## 11. Eventos e recuperação do cliente

- [ ] Polling paginado não perde nem duplica eventos.
- [ ] SSE retoma a partir do cursor suportado.
- [ ] Eventos são projeções do estado persistido, não comandos ocultos.
- [ ] Depois de desconexão, o cliente reconstrói a tela pelo read model.
- [ ] Depois de `409`, o cliente recarrega estado antes de criar nova intenção.
- [ ] Retry de timeout preserva o mesmo `commandId` e payload.

## 12. Sandbox de combate

- [ ] O cenário é validado antes da criação.
- [ ] Overrides respeitam capabilities do modo.
- [ ] Snapshot expõe recursos, cartas, status, fase e ator ativo necessários à UI.
- [ ] Timeline permite inspecionar e ramificar pontos anteriores.
- [ ] Simulação não altera a branch de origem.
- [ ] A fila de resolução contém dados suficientes para animação desacoplada.

## Critério de aceite

Uma entrega é considerada válida quando todos os itens aplicáveis passam, as duas
suítes automatizadas estão verdes e uma execução manual com seed fixa produz o
mesmo journal e o mesmo hash final em repetições independentes.
