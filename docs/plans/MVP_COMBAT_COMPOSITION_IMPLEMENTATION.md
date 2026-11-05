# Composição de combate do MVP

Referência: [Conceito v2](../design/Conceito%20de%20interface%20de%20combate%20(v2).md).

## Escopo entregue

Composição exclusiva para runs do setting `prototype-test`, escolhida pela identidade da run. Outros settings continuam em `combat_screen.tscn`. A variante reutiliza o controlador de combate, os apresentadores, o campo de batalha, a mão, os controles e o painel flutuante de atributos existentes.

A cena editável é `examples/godot-engine-showcase/scenes/ui/screens/combat_mvp.tscn`; seus grupos ficam em `scenes/ui/components/combat_mvp_view.tscn`. O script `combat_mvp_view.gd` adapta apenas apresentação e geometria.

## Etapas

- [x] Seleção da composição pelo setting efetivo da run.
- [x] Cena própria e componentes compartilhados.
- [x] Cabeçalho no topo, mão e energia embaixo, coluna à direita e campo elástico.
- [x] Nome do herói no cabeçalho e botão de timeline oculto no MVP.
- [x] Slot editável de inscrições, oculto até revisão do contrato de dados.
- [x] Campo e indicadores existentes reutilizados, sem novas regras locais.
- [x] Painel flutuante de atributos existente preservado; slot de informação inimiga reservado.
- [x] Mão compartilhada e energia apresentada diretamente dos recursos do ator.
- [x] Pilhas à direita e separação adicional antes de Encerrar turno.
- [x] Slots de companion e previsão reservados, ocultos até revisão.
- [ ] Certificação final e registro de resultados.

## TODO — revisão dos componentes conforme v2

- Registro: revisar a projeção de eventos antes de oferecer acesso ao log. Não reutilizar timeline como substituto de log.
- Turno: revisar a indicação de lado ativo com o fluxo configurável; não reinterpretar rodada na Godot.
- Inscrições: projeção, ícones, overflow de 12, tooltip e realce cruzado.
- Status: overflow de seis e detalhes acessíveis por foco.
- Intenção e painel inimigo: respeitar níveis de conhecimento e informações publicadas pela engine.
- Atributos: bônus bruto/efetivo por tag e capacidade informativa.
- Recursos: revisar apresentação genérica e limites opcionais; não supor teto de energia.
- Mão: faixa exposta e empilhamento para 10/12 cartas; ampliação no hover.
- Carta: custo à esquerda, tags e pesos, valor calculado e marcadores exigem revisão própria do componente compartilhado.
- Pilhas: comunicar fluxo sem codificar semântica para zonas arbitrárias.
- Encerrar turno: aviso por pulso necessita revisão específica de animação/acessibilidade.
- Companion: contrato de cooldown, gatilho e decisão de ser atacável continuam em aberto.
- Previsão: revisar o contrato da engine; não calcular resultado no cliente.
- Fontes, limites de texto, paleta/glifos, quantidade de tags e aparência de aura/resistências continuam pendentes conforme seção 14.

## Proporções

Prioridade 16:9. As reservas laterais equivalentes centralizam a mão enquanto os controles permanecem à direita. O campo absorve a variação de espaço. Adaptação para 16:10 e ultrawide é provisória. Nenhuma alteração de paleta ou da duração de animações integra esta entrega.
