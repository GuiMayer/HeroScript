# Core volátil na Godot

Selecione **Volatile Crucible** no menu principal e inicie uma jornada. O setting
publica `core_volatile_run` e `core_volatile`; não use o modo do setting default.
Se a API já estava aberta antes da atualização, encerre essa instância e inicie
novamente a demo para carregar o build e conteúdo atuais. Confirme versão 21 em
`GET /api/v1/version` e `volatile-core` no catálogo de settings. Saves antigos
são preservados, não atualizados à força.

As cartas têm identidade persistente. Prepare cargas e use Release, Recover ou
Empower para observar condensação ofensiva, recuperação e modifier temporário.
Nas forjas, a confirmação consulta a engine e mostra a carta antes/depois. A
operação só é enviada depois da confirmação e com a versão atual da run.

O tooltip/inspector separa componentes base, upgrades, influências contextuais e
procs previstos. Consumo mostra stacks antes/depois; saltos mostram a ordem dos
alvos. A previsão vale apenas para o snapshot e input capturados pela API.

`data/volatile_core_presentation.json` contém apresentação, não regras. Troque
as artes pelos slots `core_*` no manifesto visual. O placeholder mantém nome e
geometria; recursos, tags e custos continuam definidos pelo conteúdo da engine.
As categorias `effects` e `behaviors` possuem slots independentes para Scorch,
Afterglow, Charge, Multihit e Cascade; IDs desconhecidos usam seu fallback de arte.

`Playback.presentation_frames` é uma fila descartável. Agrupa resultados de um
proc condensado sem perder aplicações ou alterar os frames do journal. Pausa,
animação e confirmação de avanço não mudam o cálculo já concluído pela engine.

Validação offline: `tests/volatile_core.gd`, `layers.gd`, `resolutions.gd`.
Com engine ligada: `volatile_core_online.gd`, smoke, UI smoke e gameplay polish. Layout headless verifica
limites e textos; não substitui inspeção visual e playtest de compreensão.
