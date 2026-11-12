# Plano: tela dedicada para o Pattern Workshop do MVP

Estado: aprovado; implementação em andamento. Data: 2026-11-12.

## Objetivo e referência existente

No MVP, o Pattern Workshop aparece no inspetor do mapa, que mostra comandos sem o espaço necessário para explicar as opções. Propor uma tela de atividade em tela inteira, alinhada à seleção de recompensas e aos componentes de itens do loadout. O mapa identifica o nó e oferece entrada/retorno; a compra e a aplicação são realizadas na tela dedicada.

Referências do projeto: `activity_screen.tscn` e `journey_view.tscn` (estrutura de atividades), `activity_generic_offer.tscn` (ofertas), `card_modifier_inventory.tscn` e `card_modifier_target.tscn` (item e cartas compatíveis), `transformation_confirmation.tscn` (comparação de cartas com dados autoritativos).

## Esquema visual proposto

```text
┌────────────────────── PATTERN WORKSHOP ──────────────────────────┐
│ Recursos: 2 Parts                    Voltar ao mapa / Sair       │
│ Escolha um padrão. Compre-o e aplique-o a uma carta compatível.   │
├──────────────────────────────────────────────────────────────────┤
│  PADRÕES DISPONÍVEIS                                             │
│  ┌────────────────┐ ┌────────────────┐ ┌────────────────┐       │
│  │ Nome           │ │ Nome           │ │ Nome           │       │
│  │ Efeito         │ │ Efeito         │ │ Efeito         │       │
│  │ Custo: 3 Parts │ │ Custo: 3 Parts │ │ Custo: 3 Parts │       │
│  │ [Selecionar]   │ │ [Selecionar]   │ │ [Selecionar]   │       │
│  └────────────────┘ └────────────────┘ └────────────────┘       │
│                                                                  │
│ Padrão selecionado: explicação e custo        [Adquirir padrão]   │
│                      [Gerenciar padrões adquiridos]              │
└──────────────────────────────────────────────────────────────────┘
```

As três ofertas, nomes, efeitos e custos vêm das informações publicadas; exibir indisponibilidade e saldo insuficiente somente quando os dados forem suficientes para uma comparação segura. Se uma informação estiver ausente, informar sua indisponibilidade. A tela adapta o número de colunas ao espaço e permite rolagem da área de conteúdo quando necessária; cabeçalho e saída permanecem acessíveis.

## Fluxo proposto

1. **Entrada:** ao chegar ao nó do workshop, abrir a tela dedicada automaticamente, assim como nas demais atividades detalhadas. No mapa, mostrar nome, descrição, estado e um botão de retornar/entrar no workshop; não listar compras ou combinações de aplicação no inspetor. Retornar ao mapa para consultar a rota não deve resolver o nó nem comprar nada.
2. **Inspecionar e adquirir:** mostrar as ofertas lado a lado quando couberem, com nome, descrição, preço e seleção explícita. Antes de enviar a compra, pedir confirmação com padrão e custo; continuar usando a escolha e validação publicadas pelo motor. Com saldo insuficiente, explicar o motivo quando confirmado pelos recursos publicados, mantendo a validação definitiva no motor. Falha mantém a tela e apresenta uma mensagem útil.
3. **Após a compra:** mostrar que o padrão foi adquirido, que o workshop aceita apenas uma compra e que a carta ainda não mudou. Oferecer **Aplicar agora**, **Aplicar depois** e **Continuar**; permitir consultar padrões adquiridos anteriormente. Não resolver/avançar automaticamente antes da decisão explícita do jogador. Respeitar que a compra já marca a atividade como concluída; não inventar um novo comando para reabri-la.
4. **Aplicação:** apresentar inventário e cartas elegíveis em uma etapa interna da mesma tela em tela inteira, reutilizando componentes do gerenciador existente, sem obrigar o jogador a navegar para outra tela. Mostrar indisponíveis e motivo quando publicados, incluindo cartas da reserva autorizadas. Escolher carta abre comparação de antes/depois baseada na prévia do motor; somente a confirmação envia a aplicação. Cancelar não altera dados. Quando prévia ou dados de uma carta estiverem indisponíveis, explicar e não simular valores.
5. **Saída e retomada:** permitir sair sem comprar, com confirmação clara quando isso for resolver o nó. Após compra, permitir aplicar agora ou depois e avançar por ação explícita. Reabrir o mapa ou a tela de padrões não deve repetir comandos. Garantir retorno após refresh, reconexão, mudança de idioma e carregamento de uma campanha com o nó já concluído. Padrões ainda disponíveis continuam gerenciáveis fora do workshop pelo loadout.
6. **Compatibilidade:** identificar o fluxo pelo tipo de atividade e capacidades publicadas, preservando outros settings com forjas maiores e opções de melhoria, remoção e substituição. Não forçar a interface de três padrões nesses casos; cobrir ambos os formatos e não ocultar outras ações legais. Integrar as alterações pendentes do mapa de forma deliberada para evitar duas interfaces de compra ou aplicar preço não publicado como gratuito.
7. **Validação e commits:** criar testes para entrada/retorno, saldo suficiente e insuficiente, compra e recusa, uma compra por nó, padrões já adquiridos, aplicação em carta ativa e reserva, incompatibilidade, prévia e cancelamento, atualização de sessão e tratamento de falhas. Verificar visualmente em 1280×720, 1920×1080 e texto ampliado, além de teclado/controle e português; testar um fluxo conectado em sessão isolada. Separar commits por etapa lógica.

## Restrições

Preservar regras de preço, compatibilidade, persistência e resultado no motor; não deduzir dano ou modificações no cliente. Usar somente os ícones da coleção game-icons.net autorizada, sem emojis, em proporção 1:1 e com créditos aos artistas. O projeto ainda contém alterações não commitadas do painel do mapa e elas devem ser preservadas e reconciliadas com a solução aprovada.

## Registro de implementação

- Tela de atividade e ofertas usam nós declarados nas cenas do Godot; a aplicação reutiliza a coleção e a comparação de cartas existentes.
- Compra e aplicação usam referências e prévias publicadas pelo motor; visitas ao mapa não encerram automaticamente o workshop.
- Verificadas capturas em 1280×720 com tamanho normal e texto em 160%, incluindo estado após a compra; executados testes offline de interface e testes de integração do motor. Ainda é necessária uma partida completa conectada para confirmar todo o percurso do capítulo.
