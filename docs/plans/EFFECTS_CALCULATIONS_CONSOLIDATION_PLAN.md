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
- Etapa 1: ownership tipado, lados/relações direcionais em snapshots e cenários, targeting canônico sem `IsHero`, seleção ausente/inválida rejeitada. 27 testes direcionados aprovados. Consumidores especializados antigos serão removidos na etapa 9; validação do grafo de lados entra na etapa 8.
- Etapa 2: resolver compartilhado e namespace de variáveis canônico introduzidos; políticas de `Set` e recurso ausente, validação de enum/bounds/overflow e aliases sem valores negativos. 30 testes direcionados aprovados. A migração das cartas e a ampliação dos providers seguem nas etapas dependentes.
- Etapa 3: triggers resolvem e aplicam sequencialmente, com rollback por snapshot, escopo de chance, limites de expansão e passos verificáveis. Filhos dependem da aplicação do pai; aliases negativos rejeitados também no reducer. Onze testes direcionados aprovados, incluindo dez repetições por política de chance. Primitivas de deck/modifier dependem da migração do estado de run nas próximas etapas.
- Etapa 4: removida a expansão/cálculo independente de cartas; cartas e habilidades usam uma transação compartilhada, com custos no prefixo, contexto da carta e proveniência dos componentes. Traces preservados nos lifecycles e payloads da timeline. Suite completa: 1.373 Core + 147 API; teste adicional de fórmula pós-custo aprovado (cinco testes de cartas). Deck e modificadores serão conectados ao snapshot de run na etapa 7; adaptação da regeneração permanece pendente.
- Etapa 5: políticas compartilhadas de stacks/duração; dispel por filtros/ordem/limite; constraints de ação usadas pela avaliação e pela IA; `stunned` configurado. Status removidos não disparam; o snapshot inicial do boundary é global; cenários fixam revisão dos status. 1.386 Core e 147 API aprovados. Exclusão de modelos/stores antigos aguarda remoção de seus consumidores na etapa 9.
- Etapa 6: relíquias fixam dono, definição e revisão na aquisição; scopes de influência não dependem de `IsHero`; stacking explícito e apresentação separada. Triggers ligados aos boundaries de ativação/round e ao encerramento transacional, com proteção contra execução duplicada. Suite completa anterior às três novas regressões: 1.386 Core + 147 API; 16 testes direcionados incluindo novas regressões aprovados.

- Etapa 7: transições imutáveis compartilhadas de modifiers, ownership/revisão fixados e duração por comando, ativação, round, combate, nó e término do mapa. Efeitos de deck/modifier participam da transação e dos hashes; snapshots de run são propagados entre origens, boundaries, inicialização e commit. A inicialização registra os participantes anteriores aos efeitos para replay sem aplicação duplicada. Corrigida compra parcial que lançava exceção e alinhada resposta semântica da API. 1.400 Core + 147 API aprovados, incluindo dez repetições de transações mistas. Exclusão do manager antigo permanece na etapa 9; duração de run fora do término normal do mapa ainda precisa de auditoria.
- Etapa 8: contrato executável recursivo compartilhado pela publicação e pelo executor (inclusive branches não executadas); referências, limites, chance, timing, ownership, stacks/duração, triggers e destinos de influências validados. Fórmulas inline usam a gramática real do avaliador; fórmulas nomeadas têm validação estrutural de operações/parâmetros, sem executar regras durante publicação. Grafo de lados validado no cenário. Upgrades respeitam o contrato dos efeitos e retornam falha para políticas inválidas/overflow. JSON de componente desconhecido retorna diagnóstico, não exceção. 1.420 Core + 147 API aprovados. Validação da compatibilidade de todas as combinações de modo/conteúdo ainda depende da auditoria de alcance descrita abaixo.
- Etapa 9 (rotas de estado e resolução): `RunState` persistido tornou-se a única autoridade do combate; `CombatFactory` apenas materializa o snapshot inicial. Foram removidos `CombatSystem`, regeneração especializada, `Core.Damage`, o resolver/handlers paralelos de efeitos, stores mutáveis de status/modifier, o componente de status desconectado das entidades e os respectivos campos/JSON de compatibilidade. Testes arquiteturais impedem a reintrodução dessas autoridades. 1.165 Core + 147 API aprovados. A auditoria de eventos prematuros e dos adapters de fase/turno ainda conclui esta etapa.

## Pendências para concluir o plano

As entregas acima não significam que a consolidação inteira esteja concluída. Próxima etapa: migrar os consumidores restantes antes de excluir as implementações antigas.

- Remover o processador legado de regeneração junto com o fluxo antigo de `CombatSystem`; o lifecycle canônico já converte a regra de recurso em efeito comum e preserva steps, cálculo, proveniência e rollback.
- Auditar combinações de pipelines alcançáveis durante hot reload. Providers de modo e encontro já usam os mesmos componentes contextuais, fórmulas, tags e traces das demais fontes; publicação de modos e compilação de cenários rejeitam influências sem pipeline alcançável. Upgrades agora registram cada transformação permanente do valor base sem reaplicá-la como influência contextual.
- Auditar publicação prematura de eventos em gambits e fórmulas; `CombatSystem` e as demais autoridades paralelas já foram removidos.
- Completar uso de controllers/lados fora do targeting canônico e políticas de desempate; o campo de controller do lado ainda não substitui toda a lógica de controle de ator.
- Auditar encerramento imediato na inicialização, duração de modifiers quando a run termina por derrota, reaquisição de relíquias com políticas Replace/Highest e trace de remoção de múltiplos modifiers.
- Finalizar API/timeline/inspeção, traces da inicialização e ações automáticas, preview e documentação dos contratos/capacidades.
- Repetir integração completa, replay semântico/fork e dez execuções do fluxo completo após a remoção dos caminhos antigos. As dez execuções já adicionadas até aqui cobrem transações de efeitos, não substituem esse teste final.

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
