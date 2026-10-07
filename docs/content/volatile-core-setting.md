# Volatile Crucible

Setting `volatile-core`, package `heroscript.volatile-core`, launch `core_volatile_run` / `core_volatile`. Depende de `heroscript.base`, sem patches nos settings default/ascendant. Engine version 20.

## Loop inicial

Três encontros: um adversário introdutório, dupla de inimigos e dupla com chefe. Cada inimigo usa o gambit/intents existentes. Nove stops incluem seleção de carta, três forjas antes da dupla, preparação de atributos e uma quarta forja antes do chefe. Isso permite montar afinidade + multi-hit + cascade + base na mesma carta através de oportunidades normais. Custos vêm dos JSONs; não existe ataque gratuito ilimitado para contornar o deck.

Cartas iniciais: Volatile strike, Ward, Renewal, Prepare charge, Condensed burst, Condensed recovery e Condensed overcharge. A intenção é validar combinações, não afirmar balanceamento ou diversão já aprovados em playtest.

- Ember transforma dano em Scorch e cura em Afterglow; Frost transforma dano em redução residual de mana e cura em aumento residual de block. O executor não reconhece esses nomes.
- Sharpen altera Amount base, preservando identidade. Multihit muda repeat para três e divide o orçamento de origem já calculado. Residual distribui um stack entre os slots do pai, sem triplicar stacks.
- Cascade configura continuação causal: overflow pós-aplicação, até três hops, menor health entre inimigos vivos, sem levar filhos automaticamente.
- Duas vagas Behavior permitem Multihit + Cascade. Uma vaga Affinity permite substituir/remover Ember/Frost nos momentos autorizados.
- Prepare charge armazena dois stacks com potência capturada. Burst/Recovery usam todos os payloads em um proc de dano/cura; Overcharge usa a contagem em um único proc APPLY_MODIFIER.
- Power pertence à base persistente do personagem e escala cartas `core_scale` no stage de origem. Treinamento custa 10 gold e aumenta power em 2; outra opção adquire Prepare charge.

## Contratos novos usados pelo conteúdo

`effect_continuation` é um patch tipado que altera apenas a policy de um componente, inclusive removê-la com null. Não substitui Amount, custos, filhos ou melhorias numéricas já aplicadas.

Atividades CardUpgrade aceitam `parameters.costs`. Planner/assessment/discovery verificam o mesmo custo que o comando cobra no candidato. Remove/Replace/Apply usam a mesma policy da atividade. Opções expõem `costs`; falta de saldo elimina opções executáveis e rejeita o comando sem mutação.

Modos dev que autorizam transformações fora de atividade continuam usando os mesmos planners/comandos/journal. Um custo de atividade presente também vale nesse perfil; fora de uma atividade CardUpgrade não há custo de atividade inventado.

## Verificação

Testes compilam o package real, validam cards/perfis/receitas/referências e executam componentes pelo executor comum. A combinação upgrade + afinidade + multi-hit + cascade mantém a instância e conserva magnitude. Dano/cura/modifier por condensação consomem uma vez. Compilar default antes/depois conserva sua revisão e não incorpora as cartas core.

`VolatileCoreJourneyTests` executa a jornada inteira dez vezes em runtimes/stores
novos, comparando recibos completos e verificando replay semântico. O teste exige
quatro transformações na mesma instância, condensação e salto reais.
`VolatileCoreBranchTests` usa `core_volatile_sandbox` para ramificar um snapshot
com cargas e cartas transformadas. O modo normal mantém branches desabilitadas;
o modo sandbox muda policies JSON, não o processador de efeitos.

`VolatileCoreBenchmarkTests` mede somente o executor com 2/48/98 stacks. Os testes
HTTP medem outra fronteira e não devem ser tratados como tempo puro de engine.
