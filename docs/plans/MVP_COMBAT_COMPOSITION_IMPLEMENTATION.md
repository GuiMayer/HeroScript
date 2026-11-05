# ComposiÃ§Ã£o de combate do MVP

ReferÃªncia: [Conceito v2](../design/Conceito%20de%20interface%20de%20combate%20(v2).md).

## Escopo entregue

ComposiÃ§Ã£o exclusiva para runs do setting `prototype-test`, escolhida pela identidade da run. Outros settings continuam em `combat_screen.tscn`. A variante reutiliza o controlador de combate, os apresentadores, o campo de batalha, a mÃ£o, os controles e o painel flutuante de atributos existentes.

A cena editÃ¡vel Ã© `examples/godot-engine-showcase/scenes/ui/screens/combat_mvp.tscn`; seus grupos ficam em `scenes/ui/components/combat_mvp_view.tscn`. O script `combat_mvp_view.gd` adapta apenas apresentaÃ§Ã£o e geometria.

## Etapas

- [x] SeleÃ§Ã£o da composiÃ§Ã£o pelo setting efetivo da run.
- [x] Cena prÃ³pria e componentes compartilhados.
- [x] CabeÃ§alho no topo, mÃ£o e energia embaixo, coluna Ã  direita e campo elÃ¡stico.
- [x] Nome do herÃ³i no cabeÃ§alho e botÃ£o de timeline oculto no MVP.
- [x] Slot editÃ¡vel de inscriÃ§Ãµes, oculto atÃ© revisÃ£o do contrato de dados.
- [x] Campo e indicadores existentes reutilizados, sem novas regras locais.
- [x] Painel flutuante de atributos existente preservado; slot de informaÃ§Ã£o inimiga reservado.
- [x] MÃ£o compartilhada e energia apresentada diretamente dos recursos do ator.
- [x] Pilhas Ã  direita e separaÃ§Ã£o adicional antes de Encerrar turno.
- [x] Slots de companion e previsÃ£o reservados, ocultos atÃ© revisÃ£o.
- [ ] CertificaÃ§Ã£o final e registro de resultados.

## TODO â€” revisÃ£o dos componentes conforme v2

- Registro: revisar a projeÃ§Ã£o de eventos antes de oferecer acesso ao log. NÃ£o reutilizar timeline como substituto de log.
- Turno: revisar a indicaÃ§Ã£o de lado ativo com o fluxo configurÃ¡vel; nÃ£o reinterpretar rodada na Godot.
- InscriÃ§Ãµes: projeÃ§Ã£o, Ã­cones, overflow de 12, tooltip e realce cruzado.
- Status: overflow de seis e detalhes acessÃ­veis por foco.
- IntenÃ§Ã£o e painel inimigo: respeitar nÃ­veis de conhecimento e informaÃ§Ãµes publicadas pela engine.
- Atributos: bÃ´nus bruto/efetivo por tag e capacidade informativa.
- Recursos: revisar apresentaÃ§Ã£o genÃ©rica e limites opcionais; nÃ£o supor teto de energia.
- MÃ£o: faixa exposta e empilhamento para 10/12 cartas; ampliaÃ§Ã£o no hover.
- Carta: custo Ã  esquerda, tags e pesos, valor calculado e marcadores exigem revisÃ£o prÃ³pria do componente compartilhado.
- Pilhas: comunicar fluxo sem codificar semÃ¢ntica para zonas arbitrÃ¡rias.
- Encerrar turno: aviso por pulso necessita revisÃ£o especÃ­fica de animaÃ§Ã£o/acessibilidade.
- Companion: contrato de cooldown, gatilho e decisÃ£o de ser atacÃ¡vel continuam em aberto.
- PrevisÃ£o: revisar o contrato da engine; nÃ£o calcular resultado no cliente.
- Fontes, limites de texto, paleta/glifos, quantidade de tags e aparÃªncia de aura/resistÃªncias continuam pendentes conforme seÃ§Ã£o 14.

## ProporÃ§Ãµes

Prioridade 16:9. As reservas laterais equivalentes centralizam a mÃ£o enquanto os controles permanecem Ã  direita. O campo absorve a variaÃ§Ã£o de espaÃ§o. AdaptaÃ§Ã£o para 16:10 e ultrawide Ã© provisÃ³ria. Nenhuma alteraÃ§Ã£o de paleta ou da duraÃ§Ã£o de animaÃ§Ãµes integra esta entrega.

## Resultados da validação

- `combat_mvp_layout`: 15 verificações aprovadas.
- `combat_stage_layout`: 515 verificações aprovadas na composição anterior.
- Renderização da variante em 1920×1080, 1280×800 e 2560×1080 aprovada pelo runner; captura 1920×1080 inspecionada visualmente.
- Mão de 12 cartas utiliza a rolagem do componente atual. A revisão da faixa visível/empilhamento continua no backlog acima.
- O teste usa projeções locais; não certifica comandos HTTP ou o fluxo completo de uma run com a API.
