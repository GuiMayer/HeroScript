# Validação — apresentação de cartas e itens modificadores

Data: 2026-10-14. Engine `30`. Implementação dividida em dez commits de etapas, além do commit separado da base anterior.

## Resultados

| Verificação | Resultado |
| --- | --- |
| Suíte completa Core | 1.720 testes aprovados, zero falhas, última rodada 26 s. |
| Suíte Godot padrão | 41 suítes, 999 verificações aprovadas. |
| Rodada Godot final de apresentação/inventário/mapa/settings | 6 suítes, 146 verificações aprovadas. |
| API: contratos, projeções HTTP e controllers | 9 testes focados aprovados. |
| API: execução de todos os efeitos das cartas e habilidades inimigas do Mundo 1 | 2 testes aprovados. |
| API geral | 209 testes aprovados, nenhuma falha observada, mas abortada pelo limite de 300 s; **não é certificação completa**. Essa rodada antecede as últimas mudanças. |
| Renderização nativa da coleção sintética | 1280×720 e 2560×1080, 7 verificações por resolução; imagens inspecionadas no caso 1280×720. |
| Consulta HTTP de fatos em cache | 9,2 ms na rodada medida, com host de teste local; não representa latência de todos os comandos do jogo. |

As rodadas compartilham casos; os totais não devem ser somados como testes únicos.

## Garantias exercitadas

- Oferta e instância têm os mesmos custos/efeitos-base; leitura de oferta não fabrica identidade de carta.
- Upgrades próprios alteram base composta; atributos/circunstâncias alteram origem; impacto não altera a face.
- Origem continua disponível com energia zero ou sem candidato executável/alvo selecionado. Os testes HTTP confirmam ausência de mutação e RNG consumido.
- Captura de origem coincide com o orçamento da execução real para strike/multihit; atributos elegíveis zero permanecem visíveis.
- Pilhas ocultas não vazam identidade; cache muda com o snapshot/revisão; cliente rejeita dados de coordenadas incompatíveis antes de publicar.
- Aquisição/compra cria posse sem alterar deck. Unidade tem identidade determinística e proveniência; retry não duplica estoque.
- Preview e aplicação usam o mesmo planner/compositor. Unidade e transformação entram no mesmo candidato.
- Falha de escrita injetada no store deixa carta, unidade e estado autoritativo inalterados.
- Reinício mantém o hash; retry após reinício retorna o recibo anterior. Três replays semânticos reproduzem o mesmo hash para Consume e Bind.
- Alterações na branch não editam a origem. Remoção de upgrade não contorna a posse de uma transformação item.
- Faces não são reconstruídas por mudanças invisíveis de versão/impacto; tooltip permanece passivo sem scrollbar inacessível; inspector persistente conserva rolagem/foco.
- Contribuições intrínsecas (como chance crítica) continuam apresentadas mesmo quando o dano-base não muda.

## Escopo e ressalvas

Não foram alteradas runs reais do usuário, nem reiniciada sua API. Testes HTTP usam hosts/persistência isolados. Capturas nativas usam fixtures, não comprovam uma campanha inteira jogada manualmente.

O Mundo 1 migrou afinidades/comportamentos ofertados para itens; os outros settings preservam suas regras existentes. Não há migração silenciosa de saves, transferência/devolução livre de itens nem novas regras de efeito especiais para modificadores.

Para testar no jogo, reiniciar a API pelo launcher e criar uma run de versão `30`. Histórico executável de outra versão precisa do runtime correspondente. Uma run antiga não deve ser convertida editando seu arquivo.

## Reprodução

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj --no-restore
dotnet test tests/API.Tests/API.Tests.csproj --filter 'FullyQualifiedName~ContractDocumentationTests|FullyQualifiedName~CardPresentationIntegrationTests|FullyQualifiedName~CombatCardControllerTests'
./tools/test-godot-ui.ps1
./tools/test-godot-ui.ps1 -Tests @() -VisualTest card_modifier_items -VisualResolution 1280x720 -VisualOutputDirectory .runtime/card-modifier-visual
```

A suíte completa API continua sujeita ao timeout configurado; executar grupos focados permite observar sua cobertura sem apresentar uma execução abortada como aprovação geral.
