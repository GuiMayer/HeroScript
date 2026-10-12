# Áudio básico da demo

## Direção

Feedback curto, discreto e não contínuo: clique seco nos menus, textura de atrito
no uso/movimento de cartas, impacto mais encorpado no dano, madeira na proteção
e vidro suave na recuperação. Confirmação de recompensa e erro têm identidade
separada. Hover, reconstrução de interface e fases vazias não produzem som.

Foram removidos todos os tons sintetizados anteriores, inclusive o drone de
fundo. Não há música substituta aprovada: o catálogo deixa `music` vazio.
Uma faixa autoral/licenciada pode ser configurada nesse campo posteriormente;
o volume Music continua independente. Não se mantém o som ruim como fallback.

## Organização

- `data/audio_manifest.json`: eventos, variantes, ganhos, cooldown, máximo de
  instâncias por evento e associações visuais opcionais para ganhos de recursos.
- `GameAudio`: carrega 20 samples no início, usa oito players prealocados e
  encaminha música/efeitos para buses separados. Limiter protege o bus SFX.
- `CombatAudioPresenter`: converte aplicações publicadas em cues. Não calcula
  dano, não assume que health é o recurso de dano e não confunde custo com ataque.
- Telas disparam feedback de seleção, play aceito, cancelamento, abertura de
  modal/pausa, ação sistêmica aceita, erro e recompensa. Frames existentes dão o
  timing dos efeitos; não foi criado outro cursor ou fila de regras.

Variantes alternam localmente sem repetir imediatamente e sem consumir o RNG
global ou o RNG da engine. A fila não cresce: ao saturar o pool, novos sons são
descartados sem interromper os existentes. Ganhos por evento são menores que o
volume do usuário; mute e sliders afetam buses imediatamente. Nenhum download ou
gerador de waveform ocorre durante gameplay.

## Assets e licença

Seleção dos packs oficiais [Interface Sounds](https://kenney.nl/assets/interface-sounds)
e [Impact Sounds](https://kenney.nl/assets/impact-sounds), Kenney, CC0. As licenças
originais acompanham os respectivos diretórios em `assets/audio`. Os clips
selecionados são os arquivos OGG originais, sem alteração. A distribuição completa
baixada fica somente em `.runtime/audio-packs`, não no jogo.

[Kenney confirma uso comercial e atribuição opcional](https://kenney.nl/support).

## Pesquisa aplicada

[Audiokinetic: Random Containers e limites de repetição](https://www.audiokinetic.com/en/public-library/2024.1.4_8780/?id=creating_random_container&source=Help)
orienta a variação sem repetição imediata; a demo usa alternância simples, sem middleware.
[Godot: AudioStreamPlayer](https://docs.godotengine.org/en/stable/classes/class_audiostreamplayer.html)
fundamenta players não posicionais e buses para interface. Eventos frequentes
têm cooldown e mix contido; não se toca um impacto para cada frame sem efeito.

## Verificação

`./tools/test-godot-ui.ps1 -Tests audio_contracts,settings_recovery,settings_interaction,card_play_selection,pause_layers`

Cobre importação/duração/loop, cobertura de eventos, pool, variação, rate limit,
mute, independência dos buses, silêncio em frames vazios/custos/capacidade,
recursos arbitrários e shutdown. A adequação artística ainda pode ser afinada
com escuta pelo jogador; testes automáticos não avaliam preferência sonora.

Certificação em 2026-10-12: importação sem erros; áudio 59, recuperação de
configurações 64, configurações interativas 82, seleção após play 22, pausa 24,
arquitetura gráfica 27, colaboradores de combate 12 e camadas 67 verificações
aprovadas. QA usa cópia e preferências isoladas; não altera partidas pessoais.
