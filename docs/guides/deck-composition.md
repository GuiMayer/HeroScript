# Composição de baralho e circulação de cartas

A composição é opcional. Um modo a habilita com `deckCompositionPolicyId`, apontando para um item de `deck-composition-policies`. A política define os grupos, suas cotas, catálogos, módulos e janelas de edição. As instâncias iniciais continuam vindo de `startingCards` da definição da run: a composição não cria cartas para preencher vagas.

O estado `DeckLoadout` referencia as identidades existentes; não é outro inventário. Compartimentos continuam concedendo componentes, independentemente desses grupos. Sem o vínculo no modo, a criação e os fluxos existentes permanecem iguais.

## Ligação com os fluxos JSON

O runtime deriva estes conjuntos da composição do jogador da campanha:

- `deck.active`: união das instâncias selecionadas.
- `deck.reserve`: instâncias persistentes possuídas que não estão selecionadas.
- `deck.group.<groupId>`: seleção do grupo especificado.

Uma seleção de zona pode restringir seus candidatos por `instanceSetId`. O processador de zonas recebe um conjunto de IDs e faz a interseção com a zona de origem; não interpreta o significado de ativo, reserva ou módulo. A propriedade da zona continua explícita. Os conjuntos do jogador não concedem propriedade sobre cartas de outros participantes.

Exemplo de etapa em um fluxo de início de encontro:

```json
{
  "stepId": "assemble-selected",
  "operation": "Move",
  "sourceZoneId": "collection",
  "targetZoneId": "library",
  "sourceOwner": "RunOwner",
  "targetOwner": "RunOwner",
  "selection": {
    "strategy": "All",
    "instanceSetId": "deck.active"
  }
}
```

`collection` e `library` são exemplos de IDs arbitrários. O conteúdo deve manter a reserva em uma localização que não participe da compra/reciclagem e retornar as cartas da tentativa pelo fluxo de encerramento/retry. Embaralhamento e compra continuam nas operações existentes. Não há distribuição implícita.

O filtro combina com os critérios existentes; `All` significa todas as cartas da origem que pertencem ao conjunto. Conjunto desconhecido falha, inclusive quando a origem está vazia. Nesta versão, `instanceSetId` é permitido somente em `Move` e `Destroy`; outras operações são rejeitadas na publicação, em vez de ignorarem o filtro. Os conjuntos são contexto derivado, não entrada arbitrária de comandos do cliente.

## Tentativa e conteúdo revisionado

A inicialização valida a seleção antes dos fluxos de startup. Ao abrir um encontro, a engine valida novamente a composição e persiste `DeckComposition` no encontro, com ator, política, revisão, sequência, passo, módulos e IDs. Durante o encontro, os conjuntos usam essa captura, mesmo se uma carta for destruída permanentemente. As referências persistentes são reconciliadas; uma composição abaixo da cota precisa ser reparada antes de outro encontro.

Ativar outra revisão exige ausência de encontro pendente, manutenção do vínculo da política e compatibilidade integral da seleção. A política precisa declarar `revisionMigration: "RebindCompatible"`; quando omitida, a troca é rejeitada. Habilitar a mecânica em uma run antiga sem composição exige nova run. Nenhum fluxo reescreve tentativas históricas ou inventa substituições.

## Estado da entrega

Contratos, planner e integração com criação/encontros/zonas implementados nas etapas 1–3. A edição pública por REST/Godot e o setting jogável do MVP pertencem às etapas seguintes; este documento não os apresenta como disponíveis.
