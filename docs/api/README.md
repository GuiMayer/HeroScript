# HeroScript API v1

Esta área documenta a API headless para clientes Unity, Godot, web, automação e
ferramentas de QA. O contrato de máquina está em `/openapi/v1.json` e em
`openapi/heroscript-v1.json`.

| Documento | Quando usar |
| --- | --- |
| [Governança](governance.md) | Entender o que é estável, experimental ou administrativo. |
| [Quickstart](getting-started.md) | Executar uma run determinística. |
| [Contratos](contracts.md) | Implementar comandos idempotentes e tratar erros. |
| [Runs e combate](runs-and-combat.md) | Recuperação, legalidade e replay. |
| [Diálogos](../systems/dialogue-system.md) | Conversas ramificadas em JSON, condições, custos e integração Godot. |
| [Combat sandbox para Godot](combat-sandbox.md) | Cenários, snapshots, timeline, branches e simulações. |
| [Eventos](events.md) | Paginação e SSE. |
| [Conteúdo e plataforma](content-and-platform.md) | Revisões, administração e recursos futuros. |
| [Packages e settings](../content/packages-and-settings.md) | Criar jogos/mods data-only e publicar revisões. |
| [Progresso por setting](../content/profile-progress-policies.md) | Configurar condições, origens e provas de desbloqueios. |
| [Autenticação](authentication.md) | Usar administração sem expor a chave. |
| [Changelog](changelog.md) | Mudanças publicadas no contrato. |

Os arquivos em `examples/http/` são requests para extensões compatíveis com o
formato `.http`. Substitua IDs e a chave administrativa quando necessário.
Um cliente Godot reutilizável está em `examples/godot-combat-client`.
