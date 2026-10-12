# Implementação Marfim da run e diálogos

Plano: [GODOT_IVORY_RUN_AND_DIALOGUE_PLAN.md](GODOT_IVORY_RUN_AND_DIALOGUE_PLAN.md). Início: 2026-10-12.

Snapshot anterior: `bce6921`, enviado para `origin/main` antes da implementação.

| Etapa | Estado |
| --- | --- |
| 0 | API QA isolada em 5293, sem dados pessoais. Diálogo REST: 32 checks; mão online nos settings: 150 checks aprovados. Capturas anteriores preservadas. |
| 1 | Implementada: identidade contextual completa, Choice/Continue explícitos e metadata opcional de speaker. Core DialogueTests: 8 testes; Godot dialogue_contracts: 6 checks aprovados. Defaults de metadata omitidos da serialização para preservar snapshots sem esses campos. |
| 2 | Implementada: cena/controller próprios, início publicado único, conclusão pelo RESOLVE_NODE publicado e fronteira de retorno ao mapa por contexto. Diálogo REST: 32 checks; reconciliação de atividade: 7; troca de composição: 13. |
| 3 | Implementada: mapa dedicado, inspector, inspeção de qualquer nó visível sem comando e ação explícita por referência atual. Atividades não possuem mapa embutido. Estado de leitura por run/branch; 6 checks de seleção, 7 de atividade e diálogo REST aprovado. |
| 4 | Implementada: perfis de UI/cenário e manifesto Marfim; cartas/fontes do laboratório reutilizadas, atores/fundo SVG próprios. Contextos: 8, tema: 10, custos: 35; nova importação isolada e ivory_run_contracts: 7 checks aprovados. |
| 5 | Implementada: renderer único de nós inspecionáveis, estados/ícones/texto, mapa amplo e inspector Marfim. Layout SCC estável mantém saída de ciclos em camadas posteriores. 10 checks de geometria, 12 de atualizações, 6 de seleção e 37 checks/18 capturas do marco visual. |
| 6 | Implementada: pan com limiar, centralização, próximas escolhas, visão compacta, destaque por tipo sem ocultar nós e legenda somente de leitura. Seleção/scroll/overview/filtro são locais e restauráveis; testes de seleção e câmera adicionados. |
| 7 | Implementada: composição dedicada com retrato/ambiente superior e fala/opções inferiores, histórico em modal somente leitura, paginação e revelação opcional local (instantâneo por padrão). Confirmar revela/avança leitura, nunca escolhe. Estado por setting/run/branch/instância/nó; 7 checks de leitura e diálogo REST 32 aprovados em EN/PT e duas resoluções. |
| 8 | Implementada: paleta de recursos por perfil (sem semântica de regras), contraste no HUD/sidebar/custos e arte com máximo configurável maior, preservando geometria mínima e mão flutuante. Contratos incluem recursos desconhecidos; validar novamente matriz de combate na estabilização. |
| 9 | Implementada: ofertas existentes usam perfil Marfim e rótulos centralizados; preço permanece fora do custo de jogar; confirmação usa contexto próprio. Entrada após travel só executa um comando de abertura único publicado, nunca compra/prêmio/escolha; avanço forçado preserva a barreira de retorno do diálogo e não depende de refresh visual. |
| 10 | Implementada: Marfim ativado como padrão da run; cartão de gameplay compacto preserva fonte em 120%, callbacks/opções retêm identidade, escopos de leitura isolados e importação fonte sem erros. Core completo 1661/API completo 208 aprovados; campanhas/diálogo/replay reais, 36 capturas e matriz de UI aprovados. Ver relatório de certificação e limites abaixo. |

Os testes usam cópia da Godot com user directory próprio e persistência da API em `.runtime/ivory-run-qa`. Não foi utilizado o servidor das partidas pessoais.

Commits de entrega: etapa 1 `a770e7d`; 2 `39756ba`; 3 `dab0756`; 4 `f5c91de`; 5 `34abf25`; 6 `87dbe15`; 7 `7c2ef27`; 8 `d5350cf`; 9 `02c524a`. Etapa 10 possui commit próprio de estabilização.

[Certificação, capturas e limites](../design/ivory-run/2026-10-12/README.md). O smoke privilegiado não é certificado no host QA normal; o fluxo normal e os testes específicos passaram. Avisos ObjectDB de encerramento são registrados no relatório, separadamente das verificações funcionais.
