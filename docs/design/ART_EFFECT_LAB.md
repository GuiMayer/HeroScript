# Laboratório de efeitos de arte — Marfim

2026-10-20. Protótipo visual isolado, sem comandos REST ou alterações de gameplay.

## Acesso e escopo

No laboratório de cartas, abrir **Art effects lab / Laboratório de efeitos**. O popup usa o ArtSlot real, o catálogo de atores do combate Marfim e o mesmo executor que poderá ser chamado pelo combate. As animações novas **não são habilitadas automaticamente na run**: a integração visual será aprovada no laboratório primeiro. O flash anterior de combate permanece inalterado nesta etapa.

## Etapa 1 — impacto

- Seletor extensível, comparação sem efeito/prévia, duração em segundos e intensidade.
- Pausa de prévia, movimento reduzido e reset/fechamento com restauração de material.
- Shader CanvasItem local, sem screen texture, SubViewport ou leitura de pixels para CPU.
- Tinta vermelha Marfim, sem flash branco de tela inteira ou deslocamento de hitboxes.
- `ArtSlot.play_art_effect()` usa `ArtEffectPlayer`; shader compartilhado, material independente por arte ativa, referência fraca ao alvo.
- Efeito ausente, duração/intensidade zero, alvo sem textura, modo instantâneo ou movimento reduzido retornam imediatamente, sem Tween/material/espera.
- Paleta e valores iniciais pertencem ao Resource `scripts/ui/styles/art_effect_style.gd`, não à engine.

Validação inicial: `art_effects.gd` (13 checks headless, 14 com captura renderizada) e `animation_contracts.gd` (8). São testes de apresentação, não certificação de latência REST ou benchmark de GPU.
