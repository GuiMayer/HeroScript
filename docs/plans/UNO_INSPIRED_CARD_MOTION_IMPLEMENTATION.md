# Implementação das animações inspiradas no UNO Clash

Guia: `UNO_INSPIRED_CARD_MOTION_ARCHITECTURE_AND_PLAN.md`.

As alterações são exclusivamente de apresentação/input da Godot. Durações de ciclo, disposição da interface e fatos da engine são preservados. As melhorias de pose e chegada em curva já pendentes e necessárias à integração foram incorporadas à implementação; alterações não relacionadas do workspace permanecem separadas.

## Etapas

| Etapa | Resultado | Commit |
|---|---|---|
| 1 | Baseline com 2/5/10 cartas e marcos locais separados da engine | `377e101` |
| 2 | Pose completa, pais transformados e retorno ao leque | `7c91eea` |
| 3 | Curvas normalizadas e acomodação dentro do orçamento existente | `c70bfbe` |
| 4 | Reorientação contínua de hover e reorganização | `902a4ca` |
| 5 | Arraste da própria carta, ponto de pega e retorno | `bc02359` |
| 6 | Confirmação reversível e passagem para o recibo REST | `275fd56` |
| 7 | Arco de uso, rotação, escala e acomodação | `38994e7` |
| 8 | Chegada na pose final e escalonamento limitado ao ciclo | `299a74d` |
| 9 | Cancelamento seguro e abertura decorativa de painéis | `9ed28c1` |
| 10 | Presets compartilhados no seletor existente, realce de alvos publicados, proteção contra clipping pendente e certificação | Commit de certificação deste documento |

As dez etapas de implementação estão concluídas. A avaliação subjetiva da sensação de movimento continua disponível no laboratório; medições e limites estão no relatório de validação.

## Validação

Os testes usam cópia isolada do projeto Godot e diretório de usuário separado. Capturas, logs e dados de teste ficam em `.runtime`, sem alterar saves do jogador.

Ver `UNO_INSPIRED_CARD_MOTION_VALIDATION.md` para resultados, comandos reproduzíveis e limitações.

## Como validar a aparência

No laboratório de cartas, usar o seletor existente: **Card use → Motion style (shared with combat)**. Os presets **Swift**, **Smooth** e **Linear comparison** usam os mesmos executores do combate.

O campo **Cycle duration (s)** continua controlando o ciclo completo. **Play animation** faz a prévia; **Apply animation to combat** salva a duração da família selecionada e a curva visual compartilhada. A curva aplicada vale para uso, retenção e transferências; não altera conteúdo, setting ou regras. Animações já iniciadas conservam sua política capturada.

Os parâmetros de amplitude e as curvas padrão ficam em `examples/godot-engine-showcase/data/motion_manifest.json`. Os overrides visuais explícitos do laboratório ficam em `user://card_animation_profile.json`. Nenhuma duração padrão foi reduzida: uso normal de 1 s e queima central de 0,5 s permanecem configurados. Escalonamento zero permanece zero.
