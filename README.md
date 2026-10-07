# HeroScript

Engine headless e data-driven para jogos de cartas e roguelikes, com regras
autoritativas expostas por uma API REST.

HeroScript recebe comandos, executa as regras de forma determinística e devolve
snapshots, comandos legais, traces e frames de apresentação. O cliente — Godot,
Unity, web ou outro — cuida somente de input, interface, áudio e animações.

> **Estado:** pré-produção, com engine funcional e uma campanha demonstrativa
> jogável em Godot. O projeto prova o loop completo e a arquitetura, mas ainda
> não representa um jogo final com conteúdo, balanceamento e arte de produção.

## Comece pela demo jogável

O setting **Volatile Crucible** demonstra o novo core: cartas de identidade
persistente, afinidades, multi-hit com orçamento compartilhado, continuação por
abate, condensação de stacks em um proc e progressão de atributos. Regras e
conteúdo ficam em `data/configs/volatile-core`; a Godot somente apresenta os
resultados da API. Consulte o [guia do core](docs/guides/volatile-core-godot.md)
e os [contratos de conteúdo](docs/content/volatile-core-setting.md).

Engine version **20**: snapshots antigos não são convertidos silenciosamente.
Guarde o conteúdo/revisão e a versão executável compatível para reproduzi-los.

A demonstração Godot é a forma mais direta de conhecer o projeto. No menu,
**Ember Archive** percorre mapa, encontros, combate, recompensas, loja,
preparação, upgrades, chefe, histórico e replay. **Ascendant Matrix** reutiliza
o mesmo cliente com outro pacote de configuração e conteúdo para demonstrar
scaling em camadas. Ambos usam a HeroScript exclusivamente pela API REST.

### Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) — a versão
  selecionada pelo repositório é `10.0.301`;
- [Godot 4.7](https://godotengine.org/) disponível como `godot` no `PATH`;
- PowerShell para o launcher incluído no projeto.

No PowerShell, a partir da raiz do repositório:

```powershell
cd .\examples\godot-engine-showcase
.\Start-Demo.ps1
```

O launcher compila e inicia a API em `http://127.0.0.1:5271`, usa persistência
isolada em `.runtime/`, abre a Godot e encerra o processo da API ao sair.

Para validar a integração sem abrir uma janela:

```powershell
.\Start-Demo.ps1 -Headless
```

Para executar o fluxo automatizado de UI e gameplay:

```powershell
.\Start-Demo.ps1 -UiSmoke -Port 5272
```

Consulte o [guia completo da demo](examples/godot-engine-showcase/README.md)
para controles, idiomas, resoluções, laboratórios e testes visuais.

## Executar somente a API

```powershell
dotnet run --project .\src\API\API.csproj --launch-profile http
```

Com o ambiente de desenvolvimento ativo:

| Recurso | Endereço |
| --- | --- |
| Liveness | `http://localhost:5260/api/v1/health/live` |
| Readiness | `http://localhost:5260/api/v1/health/ready` |
| OpenAPI | `http://localhost:5260/openapi/v1.json` |
| Documentação interativa | `http://localhost:5260/docs/api` |

O contrato versionado também fica no repositório em
[`openapi/heroscript-v1.json`](openapi/heroscript-v1.json). Para iniciar uma run
por HTTP, siga o [quickstart da API](docs/api/getting-started.md).

## Princípios do projeto

- **Engine autoritativa:** clientes não calculam custo, dano, alvos, turno, IA
  ou progressão.
- **Determinismo:** seed, RNG, IDs, relógio lógico, revisão de conteúdo e versão
  da engine fazem parte do contexto reproduzível.
- **Estado imutável:** um comando aceito produz um commit atômico, com sequência
  e hash canônico. Falhas não publicam estado parcial.
- **Dados como conteúdo:** cartas, atores, resources, efeitos, status, relíquias,
  IA, fases, modos e progressão são compostos por JSON validado.
- **Sistemas genéricos:** dano é uma alteração de resource; efeitos e influências
  usam o mesmo processador independentemente de sua origem.
- **Conteúdo versionado:** cada run fixa uma revisão publicada. Hot reload cria
  outra revisão e a ativação em uma run existente precisa ser explícita.
- **API única:** clientes usam somente `/api/v1`; não há contrato legado mantido
  nesta fase do projeto.

## Arquitetura

```text
Arquivos JSON
    │ compilar, validar e publicar
    ▼
Revisão imutável de conteúdo
    │
Cliente ── comando REST ──> API v1 ──> coordenador de run/combate
                                                │
                                      transições e cálculos
                                                │
                                      commit atômico da run
                                                │
Cliente <── snapshot + ações legais + frames + traces
```

`RunState` é o agregado autoritativo. Combate, recursos, cartas, zonas, relíquias
e modifiers pertencem ao snapshot da run; serviços coordenam transições, mas não
mantêm uma segunda cópia de estado de gameplay.

Cada comando é versionado e idempotente. O journal sustenta recuperação,
timeline, branches e replay semântico. Eventos operacionais servem para
observação e não substituem o estado autoritativo.

Leituras recomendadas:

- [Visão geral da arquitetura](docs/architecture/overview.md)
- [Runs determinísticas e imutáveis](docs/architecture/deterministic-runs.md)
- [Integração de clientes](docs/CLIENT_INTEGRATION.md)
- [Contrato da API v1](docs/api/README.md)
- [Packages, settings e autoria de conteúdo](docs/content/packages-and-settings.md)

## O que está implementado

| Área | Capacidades disponíveis |
| --- | --- |
| Run | journal, snapshots, commits imutáveis, persistência, retomada e idempotência |
| Combate | atores, fases, ativações, IA/intents, cartas, custos, alvos e comandos legais |
| Regras | effects, resources genéricos, cálculos por buckets, status, relíquias e modifiers |
| Cartas | componentes, upgrades, influências, custos alternativos e zonas configuráveis |
| Progressão | mapa, encounters, diálogos, recompensas, loja, preparação e encerramento |
| Conteúdo | packages/settings JSON, validação, revisões, publicação e ativação explícita |
| Ferramentas | timeline por comando, branches, simulação sem commit, traces e replay semântico |
| Cliente Godot | seletor de settings, campanhas, sandboxes, localização, áudio, controles, resoluções e histórico |

Prioridade/stack e políticas avançadas dependem do modo que as habilita. Reações
continuam fora da campanha padrão. Multiplayer em rede, economia permanente,
tooling completo de mods e o dashboard não fazem parte do escopo validado atual.
Veja o [roadmap vigente](docs/roadmap/README.md) para as extensões deliberadas.

## Conteúdo data-driven

O setting principal está em [`data/configs/default`](data/configs/default). O
setting complementar [`data/configs/ascendant`](data/configs/ascendant) depende
dele e acrescenta uma campanha e pipeline de scaling próprias. O catálogo
`GET /api/v1/content/settings` anuncia os settings jogáveis, suas revisões e os
pontos de entrada que qualquer cliente pode usar sem IDs hardcoded.

O ciclo de autoria é:

1. editar um package/setting JSON;
2. validar o candidato;
3. publicar uma revisão imutável;
4. iniciar uma run fixada nessa revisão;
5. quando permitido pelo modo, pré-visualizar e ativar explicitamente uma nova
   revisão em uma run de desenvolvimento.

Os detalhes e endpoints ficam no guia de
[hot reload e perfis de ferramenta](docs/content/hot-reload-and-tool-profiles.md).

## Estrutura do repositório

```text
HeroScript/
├── src/
│   ├── Core/                       # Domínio, aplicação e infraestrutura da engine
│   ├── API/                        # API REST ASP.NET Core e contrato HTTP
│   └── Mods/                       # Compilador de packages/settings data-only
├── data/configs/                   # Settings JSON default e ascendant
├── examples/godot-engine-showcase/ # Demo Godot multi-setting
├── tests/
│   ├── Core.Tests/                 # Regras, determinismo e persistência
│   └── API.Tests/                  # Contrato, controllers e integração HTTP
├── openapi/                        # Contrato v1 versionado
├── docs/                           # Arquitetura, sistemas, API e roadmap
└── tools/                          # CLI, calculadora e benchmarks auxiliares
```

`src/Mods` compila packages/settings data-only; ele não carrega código executável
de terceiros. A extensibilidade disponível hoje é deliberadamente baseada em
dados validados e revisões imutáveis de conteúdo.

## Build e testes

O mesmo recorte executado pela integração contínua pode ser reproduzido assim:

```powershell
dotnet build .\HeroScript.slnx --configuration Release

dotnet test .\tests\Core.Tests\Core.Tests.csproj `
  --no-build --configuration Release

dotnet test .\tests\API.Tests\API.Tests.csproj `
  --filter "Category=Unit" --no-build --configuration Release

dotnet test .\tests\API.Tests\API.Tests.csproj `
  --filter "Category=Contract" --no-build --configuration Release

dotnet test .\tests\API.Tests\API.Tests.csproj `
  --filter "Category=Integration" --no-build --configuration Release
```

Encerre uma API que esteja usando a saída `Release` antes de recompilar essa
mesma saída; no Windows, o processo em execução mantém os assemblies bloqueados.

Os totais não são registrados manualmente no README porque mudam com frequência.
O resultado atual deve ser obtido executando as suítes. Testes específicos da
Godot estão documentados no [README da demo](examples/godot-engine-showcase/README.md).

## Segurança, persistência e operação

Endpoints administrativos exigem `X-Admin-Key` quando a administração está
habilitada. Nunca exponha essa chave ao cliente do jogo. Runs e conteúdo podem
ser persistidos em disco; correlation IDs, erros estruturados e telemetria
operacional auxiliam diagnóstico.

- [Autenticação e fronteiras administrativas](docs/api/authentication.md)
- [Persistência](docs/persistence.md)
- [Observabilidade](docs/observability.md)
- [Deploy de produção](docs/PRODUCTION.md)

## Documentação

| Quero... | Documento |
| --- | --- |
| Entender os conceitos e encontrar todos os guias | [Índice da documentação](docs/README.md) |
| Integrar Godot, Unity, web ou automação | [Integração de clientes](docs/CLIENT_INTEGRATION.md) |
| Implementar o fluxo REST | [API v1](docs/api/README.md) |
| Entender combate e cálculos | [Sistema de combate](docs/systems/combat/combat-system.md) |
| Criar conteúdo JSON | [Packages e settings](docs/content/packages-and-settings.md) |
| Usar timeline, replay e branches | [Timeline](docs/architecture/timeline-system.md) |
| Consultar prioridades e pendências reais | [Roadmap atual](docs/roadmap/README.md) |
| Acompanhar mudanças | [Changelog](CHANGELOG.md) |

Documentos em `docs/roadmap/phases/` preservam o histórico de implementação e
não representam o estado atual do projeto.

## Licença

Ainda não há uma licença de distribuição definida para o repositório.
