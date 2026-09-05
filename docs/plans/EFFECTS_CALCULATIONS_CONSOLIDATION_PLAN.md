# Consolidação de efeitos, cálculos e influências

## Contrato aprovado

- Toda alteração de gameplay pertence a uma transação sobre snapshots imutáveis. Falhas descartam também consumo de RNG, custos e alterações do deck; eventos públicos só podem ser publicados depois do commit.
- O processador não distingue a origem do efeito. Carta, habilidade, status, relíquia, modo e encontro fornecem contexto e proveniência, nunca uma implementação alternativa das regras.
- `DAMAGE` e `HEAL` são aliases de subtrair/adicionar uma quantidade não negativa a um recurso explícito. Alterações com sinal usam `MODIFY_RESOURCE`; nenhum nome de recurso determina derrota ou proteção.
- A lista de efeitos é sequencial: cada condição, seleção e cálculo observa o snapshot produzido pelo efeito anterior. Filhos só executam quando o pai foi aplicado. Repetições e alvos têm ordem determinística e limites de execução.
- `TARGET` exige seleção válida. Relações entre participantes são definidas por lados, não pelo papel visual de herói. Donos são tipados: entidade, lado, run ou global.
- Probabilidade declara escopo por efeito ou por alvo. Fórmulas usam `source.resources`, `target.resources`, `owner.resources`, `run.resources`, `stacks`, `duration`, `repeat_index` e `target_index`. Erros não têm fallback numérico silencioso.
- Upgrades alteram o valor base da carta; influências contextuais aplicam o scaling uma única vez. Cada contribuição preserva sua origem no trace.
- Status, relíquias e modifiers são containers de componentes e políticas explícitas de ownership, stacks, duração, influência e triggers. As instâncias pertencem aos snapshots e fixam sua revisão de conteúdo.
- Preview usa a mesma execução sem persistir. Replay verifica estado, RNG, fila e fingerprints. Reações continuam uma capacidade reservada/desabilitada, não uma implementação parcial.

## Sequência e critérios de conclusão

Cada etapa deve possuir um commit próprio e testes proporcionais. Não remover consumidores antigos antes de migrá-los. Arquivos locais não relacionados ficam fora dos commits.

| Etapa | Entrega | Critério |
| --- | --- | --- |
| 0 | Contratos e baseline | Suite existente registrada; sem alterações locais incluídas |
| 1 | Ownership e targeting | Lados e relações explícitos; seleção determinística; `TARGET` ausente falha |
| 2 | Resolução única de cálculos | Políticas de recurso ausente e conflito `Set`; validação numérica; variáveis e proveniência únicas |
| 3 | Transação de efeitos | Execução sequencial/atômica, limites, chance, passos e hashes |
| 4 | Migração das origens | Cartas, habilidades e lifecycles usam o mesmo executor; custos e deck atômicos |
| 5 | Status como componentes | Stacking/duração/constraints/dispel; sem store paralelo autoritativo |
| 6 | Relíquias | Dono/revisão fixados, stacking, triggers de início e fim; traces preservados |
| 7 | Modifiers | Um container persistente; lifecycle e efeitos; nenhuma influência global implícita |
| 8 | Validação de conteúdo | Grafo recursivo, fórmulas, políticas, referências e capacidades executáveis |
| 9 | Remoção das rotas antigas | Pipeline genérica substitui dano especializado; remover registries e processadores redundantes |
| 10 | API e timeline | Fila completa, inspeção, preview e contratos públicos coerentes |
| 11 | Verificação final | Regressão completa, replay/fork e dez execuções determinísticas; documentação atualizada |

## Registro de execução

- Baseline Git: `8e29131` (`main`). Revisão anterior registrou 1.356 testes Core e 147 API aprovados; repetir nesta implementação antes de considerar esse resultado atual.
- Etapa 0 concluída: contrato registrado; baseline repetido nesta implementação, 1.356 testes Core e 147 API aprovados, nenhuma falha.

## Testes obrigatórios da migração

1. Um efeito idêntico em diferentes origens gera a mesma transição numérica, com proveniência distinta.
2. Uma alteração de recurso influencia o cálculo imediatamente seguinte; custo é observado antes dos efeitos.
3. Falha no último efeito não altera nenhum snapshot, deck, status, modifier ou cursor de RNG original.
4. Chance zero/condição falsa não executam filhos; limites impedem recursão e expansão excessiva.
5. `Set` respeita a política configurada; overflow, NaN e infinito são falhas explícitas.
6. Status removidos durante um boundary não disparam depois; recém-aplicados não expiram no mesmo boundary.
7. Um modifier de run não melhora inimigos por acidente; ownership e revisão persistem em replay/fork.
8. Fim de combate dispara lifecycle uma única vez, na mesma transação.
9. Recursos arbitrários funcionam sem ramificação por nome; políticas de derrota continuam sendo conteúdo.
10. Dez execuções com entradas iguais produzem os mesmos snapshots, passos, journals e fingerprints.
