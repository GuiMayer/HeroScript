# Implementação do EventBus operacional

Este documento registra a fronteira atual. A implementação está em
`src/Core/Events/OperationalEventBus.cs` e implementa
`IOperationalEventBus`.

- ciclo de vida: singleton do host;
- entrega: síncrona e best-effort;
- concorrência: histórico e inscrições protegidos; handlers fora do lock;
- persistência opcional: `IOperationalEventStore`, somente para diagnóstico;
- contexto: `IGameEventContextAccessor` preenche IDs de correlação;
- falhas de subscriber: isoladas e registradas;
- replay de gameplay: deliberadamente fora deste componente.

O caminho autoritativo é `IGameplayCommandGateway → RunCommitStore`. Todo novo
evento necessário para reconstrução deve primeiro existir como frame/fact do
commit; uma cópia operacional pode ser publicada depois.
