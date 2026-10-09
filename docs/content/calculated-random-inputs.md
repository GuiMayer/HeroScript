# Inputs aleatórios calculados

Disponível a partir da engine version 22. Um input aleatório é um fato numérico reutilizável por qualquer efeito, não uma implementação de dano/crítico.

## Contrato JSON

`chance` literal e `probability` calculada são mutuamente exclusivos. Sem ambos, a chance é 1, como no contrato anterior. O resultado calculado deve ser finito e estar em `[0, 1]`; valores fora desse intervalo falham. Limites/conversão só são aplicados quando declarados.

```json
{
  "inputId": "proc",
  "scope": "Impact",
  "probability": {
    "flatValue": 0,
    "formulaValue": "source.resources.focus.current / 20",
    "channel": "proc_probability",
    "pipelineId": "proc_probability",
    "unitId": "probability",
    "stageIds": [],
    "sharedContextCapture": "SourceOnly"
  }
}
```

O ID de recurso acima é ilustrativo. A pipeline precisa ser publicada e habilitada pelo modo. Fórmula e influências usam o mesmo `CalculationResolver` que resolve os demais parâmetros. A unidade `probability` é obrigatória. A engine não lê arquivos por impacto; resolve definições no runtime revisionado.

## Captura e aplicação

- `Impact`: cálculo e fato independentes em cada impacto elegível, lendo o snapshot daquele momento.
- `Action`: primeiro uso elegível captura cálculo e sorteio para toda a ação.
- `ParentProc`: primeiro uso captura cálculo e sorteio para o proc pai.
- `groupId`: compartilha o fato entre componentes do mesmo scope; contratos incompatíveis falham na validação.

Scopes compartilhados usam `SourceOnly` por padrão e rejeitam dependência de alvo/índice de impacto. Para essa dependência, declarar `FirstEligibleImpact`: o primeiro impacto elegível captura explicitamente seu alvo/contexto; usos seguintes não recalculam nem mudam o fato.

O executor disponibiliza `rolls.proc.success` (0/1) e `rolls.proc.probability`. Esses nomes são reservados: não podem ser fornecidos pelo cliente como variáveis de comando. Um input não decide se o efeito será aplicado: o conteúdo usa o fato em condições/fórmulas de efeitos dependentes, como já ocorre com inputs literais.

Cálculo é `CaptureOnly`: pipelines com settlements de recursos ou buckets `ConsumeCapacity` são rejeitadas, inclusive em efeitos/children dormentes. RNG pertence ao executor. Chances 0 e 1 não avançam RNG; frações consomem um sorteio por captura.

## Auditoria e transações

O fato persiste probabilidade, resultado/roll, trace completo e fingerprints do cálculo, revisão, proc/impacto de captura e hashes dos snapshots de combate/run. O trace também integra a coleção de cálculos da transação, inclusive em atividades fora de combate.

Falha em cálculo, efeito posterior ou persistência não publica RNG/recursos parciais. Receipts duplicados continuam reutilizando o commit existente. Novos fatos alteram hashes de execução: saves antigos permanecem preservados, sem promessa de replay semântico na versão 22.

Capturas numéricas e alternativas condicionais estão disponíveis a partir da versão 23; veja [críticos multinível](multi-tier-critical-inputs.md). O contrato não converte percentuais acima de 100% em tiers implicitamente: essa composição é declarada nas pipelines do setting.
