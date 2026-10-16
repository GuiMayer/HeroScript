# Números no contrato Godot → REST

## Diagnóstico

`JSON.parse_string` representa números como `float`, inclusive contadores e IDs
originalmente inteiros. Reenviá-los como `1.0` causa falha de desserialização nos
campos `int`/`ulong` da API. Os mocks que construíam esses campos diretamente
como inteiros em GDScript não reproduziam a falha.

## Correção na demo

`scripts/engine/request_numbers.gd` adapta somente caminhos declarados no
contrato, sobre uma cópia profunda. Não muda regras da engine nem converte todos
os números de um documento.

| Chamada/campo | Tratamento |
| --- | --- |
| Loadout: `expectedSequence`, `expectedStep` | Normalização segura compartilhada. |
| Remover/substituir transformação: `payload.transformationId` | Normalização antes de preparar o comando. |
| Prévia de transformação: query `transformationId` | Token inteiro quando representável com segurança. |
| Restaurar histórico: `payload.sourceSequence` | Normalização por tipo de comando. |
| Fluxo de cartas: `payload.requestedCount` | Normalização por tipo de comando. |
| Simulações: payloads dos comandos | Mesma política usada nos comandos normais. |
| Cenário: stacks iniciais, prioridade das influências e enums numéricos declarados | Normalização nos caminhos do cenário; não nos dicionários de recursos. |
| Enums numéricos de ações, operações de recurso e participantes | Normalização nos caminhos tipados; nomes em texto permanecem iguais. |
| Seed, schemaVersion construídos pelo gateway; sequência/step do envelope; paginação e sequências de consultas | Já construídos com parâmetros inteiros; cobertura de regressão mantida. |
| Recursos, atributos, valores de influências e metadados arbitrários | Preservados, inclusive valores fracionários. |

Somente floats finitos, sem fração e na faixa segura de inteiros em double
(até `2^53 − 1` em magnitude) viram tokens inteiros. Frações, strings, nulos e
floats fora dessa faixa não são truncados ou interpretados como outro ID.
Isso não recupera precisão já perdida no parse de um inteiro muito grande;
um futuro suporte integral a `ulong` nessa faixa exigirá outro contrato de IDs.

A normalização do payload ocorre em `prepare_command`, antes do primeiro envio.
`send_prepared` não reinterpreta o envelope: retries preservam o mesmo conteúdo e
`commandId`. Comandos desconhecidos de mods não recebem conversões por heurística.
Novos campos inteiros usados pelo cliente precisam de caminho explícito e teste.

## Validação

- `tests/api_request_numbers.gd`: documentos realmente passados por parse JSON,
  campos aninhados, imutabilidade, decimais legítimos, casing e retry.
- `tests/inscription_rest_workspace.gd`: regressão do loadout com números JSON.
- `tests/api_request_numbers_online.gd`: API isolada na porta 61987; criação de
  cenário, fluxo de cartas, simulação, recurso fracionário e replay.
- `tests/loadout_input_online.gd`: gravação e remoção de equipamento pela API real.

Os testes online usam armazenamento próprio; não devem rodar contra o servidor
ou os saves do jogador.

## Achado separado, não alterado

Na validação, simular `INVOKE_CARD_ZONE_GAMEPLAY_FLOW` com um encontro ativo
produziu `VERSION_CONFLICT` entre step da run e step do combate. A causa está na
escolha do step pelo serviço de simulação, não na conversão de números. Essa
correção não modifica a engine para contornar esse conflito; o teste de números
da simulação usa um comando de combate válido (`END_TURN`).
