# Plano — reduzir o tempo de inicialização da HeroScript

## Evidências e limites

Em uma execução local no Windows, iniciando o launcher com dados isolados e porta alternativa, o endpoint de prontidão respondeu em 22,08 s. A leitura subsequente do catálogo levou 21,9 ms. É uma amostra, que inclui PowerShell, preparação dos dados, dotnet run e inicialização da API. Não foi uma medição do frame em que a Godot mostra o jogo como conectado.

Um build incremental Release isolado com --no-restore levou 5,62 s. A duração não pode ser subtraída diretamente dos 22,08 s, pois ocorreu em outra execução e com artifacts diferentes. O launcher atual usa dotnet run em Release sem --no-build. A API, antes de app.Run, descobre os settings e compila e publica cada um em ordem, registra caches e prepara metadados de serialização. A Godot verifica /api/v1/health/ready em intervalos de 0,5 s e tem timeout de 120 s. A origem precisa do restante da demora exige medição instrumentada. A primeira tentativa de comparar com --no-build não foi executada porque o ambiente rejeitou o comando de gerenciamento do processo.

## Objetivos

- Medir início da Godot até interface conectada, além da prontidão HTTP.
- Meta inicial de trabalho: p95 abaixo de 5 s com artefatos e conteúdo já compilados. Primeira execução, alterações de código e conteúdo são cenários separados e serão medidos.
- Preservar seleção de settings, revisão fixada, validação de conteúdo, hotreload, replay, determinismo e dados existentes. A prontidão deve continuar refletindo recursos realmente utilizáveis.
- Não reduzir o conjunto de settings disponíveis nem introduzir recompilação no primeiro clique sem medir e informar esse custo.

## Etapa 1 — instrumentar e reproduzir

1. Medir com relógio monotônico launcher, mutex, teste de instância existente, migração, build, início do processo, descoberta, compilação e publicação por setting, aquecimento de serialização, bind HTTP, primeira resposta ready, conexão e catálogo na Godot.
2. Produzir medições em arquivo local estruturado, habilitadas por opção de diagnóstico, sem dados privados ou escrita no estado autoritativo.
3. Automatizar cenários com pelo menos 10 execuções quentes e 5 frias, reportando p50/p95/máximo, versão .NET, máquina, carga, número de settings, tamanho dos stores e definição de frio. Separar disco frio real de processo novo com cache de SO aquecido.
4. Testar servidor já ativo, dados novos, dados existentes, conteúdo alterado, código alterado e falha de compilação. Capturar também tempo da primeira ação e custo após troca de setting.
5. Critério: cada parcela do caminho crítico tem medição verificável e o processo de teste não interfere com a instância do jogador.

## Etapa 2 — remover build redundante

1. Definir caminho de desenvolvimento que verifica entradas e artefatos antes de usar binário já compilado. Na ausência ou invalidação, compilar uma vez e falhar claramente em caso de erro.
2. Usar execução direta da DLL ou executável publicado após verificação, com diretório de trabalho, configuração, variáveis de ambiente e fontes de conteúdo explícitas.
3. Tratar alterações de projeto, referências, SDK, configuração e código como invalidação. Evitar timestamps isolados como garantia de validade; avaliar build incremental do MSBuild e manifesto de entradas.
4. A primeira compilação deve permanecer visível. Testar edição de código, build quebrado, instalação sem SDK e paths com espaços.
5. Critério: nenhuma execução usa binário desatualizado e o boot quente elimina o custo comprovado de build redundante.

## Etapa 3 — reutilizar bundles de conteúdo revisionados

1. Medir descoberta, parsing, dependências, validação, serialização, hash e publicação para cada setting.
2. Criar manifesto determinístico de entradas transitivas, incluindo arquivos, dependências, versão do compilador, schema e opções. Reusar bundle validado somente quando todas as entradas e sua integridade coincidirem.
3. Invalidar dependentes ao alterar pacote, definição, schema ou versão da engine. Publicar atomicamente, sem trocar a revisão de runs existentes.
4. Tratar cache ausente, corrompido, parcial ou concorrente como cache miss e reconstruir. Falhas de validação precisam continuar explícitas.
5. Comparar conteúdo canônico, revisões e hashes entre compilação limpa e reutilização. Exercitar troca de settings, mods, hotreload, reinício, replay e branches.
6. Critério: boot quente evita trabalho repetido e produz exatamente as revisões esperadas.

## Etapa 4 — reduzir trabalho no caminho crítico

1. Com os tempos por fase, investigar compartilhamento seguro de parsing e compilação de dependências comuns por conteúdo e versão.
2. Avaliar paralelismo limitado somente para operações puras e independentes. Manter publicação, erros e ordenação determinísticos e evitar pressão excessiva de disco/CPU.
3. Avaliar preparação de metadados por custo medido. A remoção ou postergação exige medir a primeira ação e verificar que não cria latência oculta.
4. Se necessário, oferecer política explícita de inicialização parcial por configuração. Catalogar estados por setting e só marcar um setting disponível após validar/publicar sua revisão. A política padrão deve manter o contrato atual até haver validação de compatibilidade.
5. Critério: erros não são ocultos, settings declarados prontos respondem imediatamente e tempo total não é apenas deslocado para o primeiro uso.

## Etapa 5 — launcher e Godot

1. Medir mutex, migração e verificações HTTP sequenciais. Reutilizar conexão e reduzir chamadas quando houver contrato que comprove compatibilidade sem perder validações.
2. Ajustar sondagem de prontidão e timeout com base em dados. Evitar loops agressivos e tentativas sobrepostas.
3. Exibir estados claros de preparação, compilação e conexão, com erro acionável e opção de tentar novamente.
4. Validar servidor já ativo, porta ocupada, cliente encerrado, falha do processo e duas instâncias simultâneas. Garantir limpeza apenas dos processos iniciados pelo launcher.
5. Critério: medição na Godot confirma o ganho e falhas não deixam processo órfão ou indicam prontidão incorreta.

## Etapa 6 — certificação e entrega

- Repetir benchmark da etapa 1 antes/depois nas mesmas condições. Registrar medianas, p95, máximos e primeira ação.
- Executar testes de compilação/publicação, migração de dados, troca e isolamento de settings, ativação de conteúdo, concorrência, falhas, persistência, determinismo, replay e branches.
- Comparar revisão e hashes apenas quando entradas e regras forem equivalentes. Testar mudança de conteúdo como nova revisão, com runs antigas preservadas.
- Documentar requisitos de distribuição, política de invalidação, recuperação de cache e limitações. Fazer commits separados por etapa validada.

## Ordem

Instrumentação primeiro, depois build redundante e cache de conteúdo. Priorizar os demais custos conforme os dados. Interface e certificação acompanham os passos anteriores e concluem a entrega.

## Progresso de inicialização na interface

A área de estado da engine na Godot deve apresentar a etapa real durante a inicialização local. O texto usa as traduções da interface, com inglês como fallback. Estados iniciais previstos:

- Preparing data — preparação ou migração dos dados locais.
- Building server — verificação e compilação dos artefatos, quando necessárias.
- Starting server — criação do processo e inicialização da API.
- Loading settings — descoberta, validação e publicação do conteúdo necessário.
- Connecting — confirmação da prontidão e conexão da Godot.
- Engine online — conexão estabelecida.
- Startup failed — falha com mensagem específica e opção de tentar novamente.

### Contrato de comunicação

1. O launcher publica eventos de progresso estruturados, com versão do protocolo, identificador único da tentativa, etapa, estado, timestamp e código de erro opcional. Os textos ficam na Godot e são associados aos códigos.
2. Usar um arquivo de progresso efêmero no diretório de runtime específico da porta como mecanismo inicial, com gravação atômica e leitura tolerante a arquivo ainda inexistente. A API deve publicar ou encaminhar suas etapas de carregamento antes de abrir a porta HTTP. A instrumentação da etapa 1 fornecerá os eventos necessários.
3. A Godot acompanha somente a tentativa vinculada ao processo iniciado, descarta registros antigos e interrompe a leitura após conclusão, cancelamento ou timeout. Duas instâncias concorrentes devem acompanhar a tentativa proprietária da porta sem sobrescrever seu progresso.
4. Se o servidor já estiver ativo, a interface passa pela verificação de conexão. Para servidor remoto ou processo sem suporte ao protocolo, mostra Connecting até a resposta HTTP.
5. Engine online só aparece depois da confirmação de prontidão e da conexão pelo gateway. Atualizações de progresso não alteram esse critério.
6. Uma tentativa encerrada deve apresentar erros específicos quando disponíveis, incluindo build, conteúdo inválido e porta ocupada. Se o processo terminar sem registrar erro, mostrar falha genérica com acesso aos logs. Uma nova tentativa recebe outro identificador.

### Validação

- Testar sequência e traduções EN/PT, fallback para servidor remoto, servidor já ativo e progresso indisponível.
- Testar escrita parcial, registros obsoletos, falha de build e de conteúdo, timeout, cancelamento, encerramento do processo e duas instâncias simultâneas.
- Confirmar que a interface recebe o progresso antes da API responder e que nunca exibe Engine online prematuramente.
- Medir o custo da comunicação e manter o arquivo de progresso fora dos dados autoritativos de gameplay e do conteúdo revisionado.

Implementar o contrato de eventos junto da instrumentação da etapa 1 e integrar a apresentação na etapa 5. Fazer commit de cada etapa após sua validação.

## Primeira instrumentação da API

Com HEROSCRIPT_STARTUP_TIMINGS=1, a API emite eventos JSON no stdout contendo duração de descoberta, compilação e publicação por setting, construção do host, preparação de serialização e abertura do listener. Sem a variável, a instrumentação não emite eventos.

Uma execução isolada da DLL Release, com store novo e sem launcher, respondeu pronta em 14,618 s. A soma das etapas sequenciais de compilação e publicação dos dez settings ficou em aproximadamente 13,1 s. Ascendant consumiu 2,23 s de compilação e 1,77 s de publicação; prototype-test consumiu 1,40 s e 1,37 s; reward-lab consumiu 0,93 s e 1,01 s. A preparação de serialização levou 0,27 s. A medição foi única e não deve ser interpretada como p95 ou comparada diretamente ao tempo de launcher com build.
