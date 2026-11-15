# Plano: expansão de conteúdo e balanceamento do MVP

Status: proposta para aprovação. Escopo: setting jogável `prototype-test`, capítulo `prototype_chapter`. Nenhum valor abaixo é balanceamento já validado.

## Objetivo

Expandir cartas e opções de build; medir dano, defesa, cura e ritmo a partir das cartas, inscrições, padrões, módulos e recursos realmente acessíveis; ajustar encontros e cartas de forma iterativa. Toda carta deve ter de uma a três tags temáticas explícitas, distintas das tags funcionais. Escudo e cura sempre usam Divino; podem combinar Divino com até duas outras tags.

## 1. Inventário e referência reproduzível

- Extrair definições carregadas pelo package, composição inicial, pools de recompensa, custos, efeitos, disponibilidade, inscrições e respectivos slots/capacidade, padrões, progressão e inimigos. Separar conteúdo publicado, acessível em cada etapa e arquivos de protótipo não carregados. Examinar os textos de apresentação contra os valores efetivos e corrigir divergências.
- Levantar valores atuais e cenários por etapa: início (composição inicial e inscrições legalmente equipáveis), após cada recompensa/preparação e antes do chefe. Incluir módulos Vazio e Eco, combinações legais das inscrições e padrões, seus custos e cartas elegíveis; usar apenas opções acessíveis antes do encontro. Não somar bônus mutuamente exclusivos nem presumir acesso a todas as recompensas.
- Construir avaliação reproduzível usando o cálculo autoritativo do jogo. Apurar dano esperado por carta e por energia, dano por turno/combate, efeito de múltiplos acertos, crítico, status, duração, alvo, condições, atrasos, descarte e consumo; medir também escudo e cura. Registrar probabilidades de compra e opções disponíveis, estratégias de referência, variância entre sementes e o dano efetivo após defesas. Explicitar política de jogo, população de seeds e incerteza; não tomar a média simples das cartas como dano real por turno.
- Produzir relatório com valores por carta/build/etapa, mediana e percentis, contribuição por fonte, condições e custo de oportunidade. Tratar dano a múltiplos alvos e total ao longo do combate separadamente para evitar contagem dupla. Comparar com desempenho de bots/estratégias e testes humanos quando disponíveis.

## 2. Fórmula e identidade temática

Implementar e validar `Valor = (Base + Flat) × (1 + Σ peso_tag × Increased_tag) × More...`. As parcelas Increased entram no mesmo somatório; fatores More independentes são multiplicados uma única vez. Definir fonte e unidade de cada atributo, tags que participam, ordem de cálculo, arredondamento, limites, crítico, resistências e interações com dano ao longo do tempo.

Proposta inicial para testar: cada tag recebe peso 1,00 em carta de uma tag; 0,60 em duas; 0,45 em três. Com +20% Increased em cada tag e base 10, sem Flat ou More, os resultados antes do arredondamento são 12, 12,4 e 12,7. É uma vantagem moderada quando os investimentos nas tags são iguais; não garante vantagem quando os atributos da build diferem. Testar e ajustar pesos após simulações, em vez de apresentar esses números como definitivos.

- Inventariar tags atuais e criar conjunto explícito incluindo Divino, Fogo, Vazio e Sombra, sem misturar tags funcionais como ataque ou defesa com as temáticas. Garantir validação de 1–3 tags por carta, sem duplicações.
- Para escudo/cura, Divino é obrigatório e suas influências sempre participam; outras tags só contribuem se presentes no efeito e suportadas pelo contrato. Auditar cartas híbridas e efeitos secundários para não escalar dano, escudo ou cura indevidamente. Confirmar que Divino está disponível em atributos e fontes de progressão, sem exigir investimento inalcançável.
- Substituir ou adaptar as fórmulas e agrupamentos existentes, que atualmente usam pesos e retorno não linear, nas pipelines pertinentes; não manter uma redução adicional escondida que altere a fórmula aprovada. Evitar aplicação dupla de atributos e inscrições. Usar traces da engine, não recálculo próprio da interface.
- Testar 1, 2 e 3 tags com atributos iguais e diferentes, apenas Divino, Divino híbrido, atributos ausentes/negativos se permitidos, Flat, múltiplos More, crítico, resistências, arredondamento, captura de status e aplicação por efeito. Rever compatibilidade das revisões de partidas existentes antes de publicar o novo conteúdo.

## 3. Ampliação do catálogo e mecânicas

- Mapear separadamente suporte da engine, integração da interface e uso efetivo no MVP. Compra, descarte, exílio temporário e retenção já aparecem nas configurações: não classificá-los como inexistentes sem uma auditoria detalhada.
- Propor cartas novas para cobrir lacunas de decisões: compra ou filtragem, escolha entre efeitos, custos alternativos, preparação para turno futuro, risco/recompensa, efeitos condicionais, interação com descarte/exílio e combinações entre tags. Para cada proposta, verificar primeiro se a engine e a UI já suportam o comportamento; implementar infraestrutura faltante somente quando necessária.
- Para cada carta publicar nome, raridade, custo, 1–3 tags temáticas, papel, texto, números, alvo, janela, restrições e fonte de aquisição. Garantir representação suficiente de cartas de ataque, escudo e cura com Divino puro e híbrido. Projetar sinergias reais com inscrições e padrões acessíveis.
- Definir tamanho final do catálogo a partir das lacunas encontradas e testar cada mecânica com uma ou mais cartas; evitar um número arbitrário que sacrifique clareza ou viabilidade.

## 4. Balanceamento de cartas e encontros

- A partir das distribuições medidas, definir faixas-alvo de dano/energia, defesa e cura por etapa, com orçamento explícito para utilidade, risco, setup, dano em área e efeitos atrasados. Comparar cartas de custo e função equivalentes sem reduzir todas à mesma eficiência.
- Dimensionar vida e escudo dos inimigos pelos turnos de sobrevivência desejados contra builds legais de desempenho baixo, mediano e alto. Dimensionar dano e frequência das intenções pela capacidade real de defesa/cura, diversidade de mão e margem para decisões. Definir metas de duração, pressão e derrota, identificadas como hipóteses sujeitas a playtest.
- Diferenciar comuns, encontros em grupo e chefe por padrões de intenção e contrajogadas. Testar combinações de cartas, inscrições, módulos e padrões; procurar loops, trivialização, ausência de opções defensivas e builds inviáveis. Ajustar cartas e inimigos em ciclos, preservando escolhas distintas.
- Simular múltiplas seeds e políticas com logs reproduzíveis; relatar distribuição e razões de falha, não apenas taxa de vitória. Validar com jogadores humanos, pois simulação e médias não comprovam diversão ou legibilidade.

## 5. Integração e validação

- Publicar dados e revisão de conteúdo seguindo o padrão existente; preservar partidas em revisão anterior e definir como novas partidas recebem as mudanças. Não alterar snapshots antigos.
- Atualizar telas de cartas, inscrições, atributos, intenções e inspeção para explicar tags e fontes de escalonamento usando os dados da engine. Manter nós definidos em cenas Godot. Adicionar ícones apenas da biblioteca autorizada e creditar artistas em CREDITS.md, se forem necessários.
- Verificar contratos de dados, projeção, preview versus aplicação, custos, alvo, persistência, replay determinístico, progressão completa, compatibilidade de padrões, navegação e acessibilidade. Cobrir 1280×720, escala de texto e mouse/teclado; registrar imagens das telas relevantes.
- Gerar um relatório final de números anteriores/posteriores, fórmulas, resultados e limitações. Fazer commits separados por etapa lógica, sem incluir alterações alheias.

## Entregáveis e ordem

1. Auditoria e ferramentas de medição, com relatório de referência verificável.
2. Escalonamento temático e Divino, com testes e atualização da apresentação.
3. Mecânicas e cartas adicionais, com testes e documentação.
4. Ajuste iterativo de cartas, inscrições e inimigos, com relatório de simulações e cenários.
5. Validação integrada, playtest disponível e documentação final.

As métricas iniciais e os pesos acima são hipóteses. Os valores finais de cartas e inimigos serão definidos somente depois de medir as builds legais e executar as simulações; nenhuma implementação está autorizada por este plano até aprovação.
