# Inscrições: revisões, propriedade e certificação

## Ativação de conteúdo

`loadout-policies.revisionMigration` é explícito: ausente ou `RejectRevisionChange` rejeita a troca; `RebindCompatible` permite revalidar tudo antes da ativação, fora do combate. O Ascendant usa a segunda opção. Publicar uma revisão não altera uma run automaticamente.

O preflight verifica todos os inventários, inclusive peças não equipadas, e os equipamentos finais de todos os personagens. Definição removida, aumento de custo acima do orçamento, redução incompatível de slots/capacidade, tags ou duplicatas inválidas rejeitam a operação inteira. Feche a oferta de inscrições do nó atual antes de trocar a revisão. Não há descarte, conversão ou desequipamento silencioso.

O comando canônico de ativação continua responsável por revisão, modo, tempo lógico e persistência. `acquisitionRevision`, origem e sequência de aquisição permanecem históricos; `definitionRevision` registra a revisão efetiva após um rebind aceito. Provider, consulta e planner leem a mesma revisão efetiva. Snapshots anteriores e branches existentes não são reescritos.

Trocar o setting, o ID da política, habilitar equipamento numa run que não o possuía ou trocar `inventoryScope` exige uma nova run. Redistribuição automática Actor ↔ Run não está implementada: a política é rejeitar, não adivinhar donos.

## Transferência e ciclo de vida

`allowTransfers: true` autoriza `TRANSFER_INSCRIPTION` pelo gateway, somente em escopo Actor e numa janela de equipamento permitida. Payload:

```json
{"fromActorId":"alice","toActorId":"bob","instanceId":"6fa16543-181b-428d-8a4b-9285cba11c78"}
```

Ambos devem estar no registro persistente. A peça precisa pertencer à origem, estar desequipada e caber no inventário final do destinatário. Identidade e proveniência são preservadas, sem RNG nem alteração de cartas. O Ascendant inicial desabilita transferências, pois sua jornada registra apenas o herói.

Morte em combate não apaga o registro nem transfere equipamento. A saída confiável de um personagem só é aceita sem equipamento, inventário individual ou histórico de ofertas que ficaria órfão; o jogador principal e o último personagem não podem ser removidos por essa transição. Havendo histórico, preserve a identidade. Não existe um endpoint livre para criar membros de grupo ou conceder inscrições. Suporte de domínio a dois donos não significa que o jogo publicado já tenha um sistema de party.

Branches compartilham valores imutáveis, não autoridade de escrita; o inventário Run é reidentificado para a run filha. A consulta usa o ator solicitado, nunca a seleção visual. Atributos de um ator ainda não materializado são indisponíveis com explicação, não valores inventados.

## Verificação e medição

Cobertura inclui escopos Actor/Run, posse estrangeira, disputa de instância compartilhada, equipamento atômico, preview sem RNG, aquisição/preços/recusa, elegibilidade capturada, migração compatível/incompatível, transferência e branches. Testes REST cobrem conflito de versão, retry, reinício com o mesmo armazenamento e três verificações semânticas de replay. O laboratório testa drafts e respostas atrasadas, troca de personagem e inventário, arrasto e projeções canônicas; exemplos offline continuam identificados.

Em 2026-10-15, `tools/measure-inscription-rest.ps1` fez 12 alternâncias equipar/remover contra Kestrel isolado, com persistência real:

| Operação | Amostras | p50 (ms) | p95/máximo (ms) |
| --- | ---: | ---: | ---: |
| Preview | 12 | 16,83 | 49,04 |
| Aplicar | 12 | 38,93 | 148,89 |
| Consultar loadout | 13 | 12,00 | 122,38 |
| Sessão | 12 | 24,80 | 180,49 |

Criação dessa run: 694,65 ms; catálogo: 237,33 ms, uma amostra cada. Não houve baseline comparativa; estes números não comprovam ganho nem garantem todas as ações de runs longas abaixo de 1 segundo. Não incluem clique-a-tela nem animação. O teste cria uma run real: use servidor e armazenamento isolados, não o servidor do jogador.

Certificação visual: EN/PT-BR em 1280×720, 1920×1080 e 2560×1080, com exemplos offline; não é aprovação humana da experiência final. Armadura, esquiva e outros atributos só afetam ações que tenham consumidores configurados. Não se implementa uma nova regra de gameplay apenas exibindo seu atributo.
