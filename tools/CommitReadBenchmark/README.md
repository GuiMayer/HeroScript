# Benchmark de leitura de commits

Executar na raiz do repositório, apontando para uma run existente:

```powershell
dotnet run -c Release --project tools/CommitReadBenchmark -- "caminho-do-store-de-runs" "guid-da-run"
```

O programa não altera a run. Compara `LoadCommitsAsync` e `FindCommandAsync`
para um comando inexistente, com o cache desativado (0) e ativado (256).
Cada combinação usa uma instância nova do store, descarta uma amostra de
aquecimento e imprime cinco amostras e a mediana em JSON.

Execute sem outras cargas e mantenha a mesma run entre comparações. O teste
mede leitura local, não uma transação completa nem o desempenho de todos os
endpoints. A primeira leitura valida todos os commits; o benefício depende de
reuso e da capacidade do cache. Histórico maior que a capacidade pode causar
evicção contínua. Não use tempos absolutos como assertions no CI.
