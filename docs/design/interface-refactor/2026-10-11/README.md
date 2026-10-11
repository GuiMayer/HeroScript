# Certificação da arquitetura visual substituível

Data: 2026-10-11. [Plano implementado](../../../plans/GODOT_UI_ARCHITECTURE_REFACTOR_PLAN.md). [Guia de uso](../../GODOT_VISUAL_PROFILES.md).

As onze etapas foram implementadas em commits separados. As capturas usam o tema preservado e componentes de produção com fixtures, não imagens geradas ou partidas reais. O perfil contrastante/lateral é uma prova automatizada; a repaginação artística Marfim não está concluída.

## Evidências

- [Suite offline](offline-validation.json): 22 testes, 1.092 checks funcionais, zero falhas de assert/script.
- [Suite REST](rest-validation.json): `hand_online` 150, `dialogue` 32 e `gameplay_polish` 175 checks; todos com saída zero e sem diagnósticos. Inclui combate nos settings default/ascendant, matriz de resolução/idioma, uso de carta, diálogo, campanha completa, compra, histórico e replay determinístico.
- [Capturas/inventário](validation.json): importação, regressões de geometria e 20 PNGs. [Métricas](metrics.json): mediana/p95, leituras sintéticas e escopo.
- `ui_lifecycle` também passou novamente após os últimos ajustes: overview compartilha contexto, diálogo retido envia payload atual, fechamento do voo libera await, sinais desconectados, zero novos nodes órfãos e objetos limitados em 30 ciclos.
- Importação do projeto fonte: saída zero, nenhum diagnóstico; UIDs gerados e versionados.

O runner copia o projeto e usa um user directory exclusivo. Os testes REST usaram uma API temporária em `127.0.0.1:5279` com runs, conteúdo publicado e telemetria exclusivos em `.runtime/ui-api-qa/f3738b53af1d4de6805c95f71ed79620`. Nenhuma preferência, run ou progresso real do jogador foi usado. O processo temporário foi encerrado ao concluir.

As primeiras tentativas sem API foram registradas como falhas de conexão, não como integração aprovada. A primeira execução de diálogo usava um jogador incompatível com o launch atual; o teste passou a consumir a identidade publicada. Uma tentativa inicial da campanha não terminou no merchant; o teste ganhou diagnóstico e saída segura, e duas campanhas posteriores completas passaram. Os JSONs anexados são as execuções finais, não apagam os logs anteriores em `.runtime`.

O hash registrado pelo capturador é a base `77655bf` mais as alterações da etapa 11 presentes nesta entrega; o arquivo não tenta incluir o hash do commit que contém a própria captura.

## Comparação de refresh síncrono

Mesmo roteiro, Godot 4.7.2 e política stretch/expand de produção. Vinte amostras por caso; o cenário alterado incrementa somente a sequência, não simula um comando com todos os dados mudando. Por isso não extrapolar estes números a qualquer update real.

| Resolução | Tela/caso | p95 baseline (ms) | p95 refatorada (ms) |
| --- | --- | ---: | ---: |
| 1280×720 | Combate, idêntico | 1,222 | 0,754 |
| 1280×720 | Combate, sequência alterada | 29,234 | 6,215 |
| 1280×720 | Jornada, idêntica | 7,980 | 0,799 |
| 1280×720 | Jornada, sequência alterada | 8,409 | 0,771 |
| 2560×1080 | Combate, idêntico | 0,975 | 0,646 |
| 2560×1080 | Combate, sequência alterada | 36,893 | 5,859 |
| 2560×1080 | Jornada, idêntica | 10,024 | 0,854 |
| 2560×1080 | Jornada, sequência alterada | 6,621 | 0,960 |

Uma medição intermediária revelou regressão pela reaplicação de StyleBox e rebuild da sidebar em mudanças irrelevantes. Isso foi corrigido antes desta captura final. Atualização idêntica não consulta inspeção; mudança de sequência mantém uma inspeção de mão por atualização, igual à baseline. Jornada não consulta gateway nesta fixture. Zero HTTP real durante capturas. O cronômetro não inclui GPU, layout diferido, rede ou execução da engine e não garante latência clique→resultado.

## Capturas preservadas

A [baseline anterior](../../interface-baseline/2026-10-11/README.md) não foi sobrescrita. Inglês, escala 1,0 e movimento reduzido. Há testes adicionais para português, 1,2, mouse/teclado/eventos de controle e outras resoluções; não é certificação de todos os modelos de gamepad físicos.

| Estado | 1280×720 | 2560×1080 |
| --- | --- | --- |
| Principal | [imagem](menu-1280x720.png) | [imagem](menu-2560x1080.png) |
| Combate | [imagem](combat-hand-1280x720.png) | [imagem](combat-hand-2560x1080.png) |
| Preview | [imagem](combat-preview-1280x720.png) | [imagem](combat-preview-2560x1080.png) |
| Sidebar | [imagem](combat-sidebar-1280x720.png) | [imagem](combat-sidebar-2560x1080.png) |
| Pausa | [imagem](pause-1280x720.png) | [imagem](pause-2560x1080.png) |
| Mão vazia | [imagem](combat-empty-1280x720.png) | [imagem](combat-empty-2560x1080.png) |
| Jornada | [imagem](journey-1280x720.png) | [imagem](journey-2560x1080.png) |
| Ofertas | [imagem](offer-1280x720.png) | [imagem](offer-2560x1080.png) |
| Configurações | [imagem](settings-1280x720.png) | [imagem](settings-2560x1080.png) |
| Replay | [imagem](replay-1280x720.png) | [imagem](replay-2560x1080.png) |

A fixture de recursos passou a publicar `minimum: 0` explicitamente. Antes faltava o mínimo e a view o inventava; a implementação agora só mostra barra com limites publicados. A paleta conhecida foi declarada no JSON visual para conservar a aparência, não em um mapa de IDs no código. As capturas não avaliam balanceamento ou efeitos completos sem inspeção.

Ofertas distinguem preço de aquisição de custo de uso: o preço fica fora da face da carta. Se o contexto não publicar o custo de uso, a face informa que ele está indisponível, sem interpretar o preço da loja como energia/mana.

## Encerramento e limites conhecidos

Saída zero não é prova de ausência de vazamento. Foram investigados separadamente:

1. **Preexistente:** dois objetos `AudioStreamWAV`/`AudioStreamPlaybackWAV` às vezes permanecem no encerramento imediato headless. O mixer libera em tempo real; testes que aguardam seu ciclo podem não emitir aviso. Não foram introduzidos pela migração.
2. **Retenção nova identificada no teste de mão:** o diagnóstico verbose aponta `visual_theme_profile.gd`, `preserved.tres`, `app_theme.gd`, `art_catalog.gd` e classes nativas relacionadas — 12 objetos, ou 14 se o áudio também permanecer. São referências de scripts/perfil usados como fachada/cache de processo; a retenção ao encerrar continua pendente. Não foi rotulada como preexistente nem declarada resolvida.
3. Um cache estático redundante de texturas provocava um RID residual durante a investigação. Foi removido: ResourceLoader compartilha texturas vivas. Nas execuções finais não há erro de RID nem textura órfã nesse diagnóstico.
4. `ui_lifecycle` não detectou novos nodes órfãos e a contagem permaneceu limitada em 30 ciclos, incluindo modais/voo interrompido. Isso distingue os resíduos de processo de crescimento por interação, sem provar ausência de qualquer vazamento futuro.

O runner falha para asserts, scripts, parsing e outros erros nativos. `resources still in use at exit` permanece no relatório como diagnóstico separado, não é silenciosamente tratado como teardown limpo. A certificação funcional passou; a limpeza completa desses residentes em encerramento headless é uma ressalva explícita da entrega.

Quanto à arquitetura, a auditoria dos overrides e seus donos está no guia. Algumas alterações ainda usam subtrees desejadas temporárias; identidade/foco da árvore viva são mantidos. Não há promessa de zero alocações em mudanças de dados nem editor universal de layouts.
