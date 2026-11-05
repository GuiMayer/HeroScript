# Operação, publicação e limites de produção

Atualizado em 2026-10-16. Este guia descreve o código atual; não certifica
distribuição comercial, Docker ou hospedagem pública.

## Escopo validado

HeroScript é uma máquina de regras headless, determinística e data-driven,
consumida pela API REST v1. O escopo exercitado é single-player com campanhas
e sandbox. A Godot apresenta os resultados e envia intenções; não executa regras.

O launcher de desenvolvimento e as jornadas automatizadas são utilizáveis.
Ainda é necessário certificar um pacote exportado Godot + API, instalação limpa,
atualização, hardware-alvo e sessões humanas prolongadas. Não há um requisito
mínimo de RAM/disco certificado nem garantia universal de latência.

## Build e execução local

Na raiz do repositório, usando o SDK definido em `global.json`:

```powershell
dotnet publish ./src/API/API.csproj --configuration Release --output ./artifacts/api
```

Em um terminal de operação, entre em `artifacts/api` e execute:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5260'
$env:Admin__Enabled = 'false'
$env:ToolAccess__Profile = 'normal'
$env:AllowConfigReload = 'false'
dotnet ./API.dll
```

Este comando usa o runtime .NET 10 instalado; não gera um executável standalone.
A publicação copia os packages de `data/configs` para `Resources/`. Preserve
o diretório publicado inteiro, não somente `API.dll`. Os caminhos relativos
de persistência dependem do diretório de trabalho; use caminhos absolutos
para uma instalação com armazenamento separado.

Em outro terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:5260/api/v1/health/live
Invoke-RestMethod http://127.0.0.1:5260/api/v1/health/ready
Invoke-RestMethod http://127.0.0.1:5260/api/v1/capabilities
Invoke-RestMethod http://127.0.0.1:5260/api/v1/content/settings
```

O launcher Godot usa outra porta por padrão (`5271`) e armazenamento próprio
em `%APPDATA%\HeroScript\engine`. Preferências usam `%APPDATA%\HeroScript`.
Na primeira abertura migra o antigo `.runtime` por cópia verificada sem removê-lo;
o [README da demo](../examples/godot-engine-showcase/README.md#dados-locais-e-migração)
descreve caminhos, backup e isolamento por setting/porta. Suas permissões de desenvolvimento
não devem ser copiadas para uma distribuição de jogador.

## Configuração efetivamente lida

| Variável de ambiente | Papel / padrão |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Ambiente do host; usar `Production` na publicação |
| `ASPNETCORE_URLS` | Endereço de escuta; manter loopback para uso local |
| `Persistence__RunStatePath` | Commits autoritativos; `data/runs` |
| `Persistence__ContentStorePath` | Revisões publicadas; `data/content` |
| `Persistence__OperationalTelemetryPath` | Telemetria não autoritativa; `data/telemetry` |
| `Mods__PackageRoots__0` | Raiz alternativa de packages; sem configuração, usa `Resources` ao lado do executável |
| `Content__StartupSettings__0` | Seleção explícita de setting para carregamento inicial |
| `Admin__Enabled` | Endpoints administrativos; `false` no appsettings base e Production |
| `Admin__ApiKey` | Chave comparada com `X-Admin-Key` quando administração está habilitada |
| `ToolAccess__Profile` | `normal`, `experimental`, `sandbox`, `dev_modder` ou `custom`; padrão `normal` |
| `AllowConfigReload` | Flag de recarga; padrão `false`, não substitui os demais controles |
| `AllowedOrigins__0` | Origem permitida para clientes web em produção; não é autenticação |

Índices adicionais (`__1`, etc.) configuram os demais itens das listas.
`HERESCRIPT_ADMIN_KEY` e `Persistence__EventStorePath`, citados no guia antigo,
não são as chaves lidas por esses serviços. `.env` não é carregado automaticamente
pelo processo .NET; o ambiente precisa ser fornecido pelo host/launcher.

As três fronteiras administrativas são independentes: habilitação, chave e
capability do perfil. Consulte [autenticação](api/authentication.md).
Hot reload publica outra revisão; não reescreve silenciosamente as regras
de uma run já iniciada. Veja [perfis e recarga](content/hot-reload-and-tool-profiles.md).

## Persistência e backup

`FileRunCommitStore` é a autoridade: comandos aceitos geram commits duráveis
com envelopes, estado e resultados. Telemetria não é um journal de gameplay.
Snapshots/projeções substituíveis não dispensam os commits nem o conteúdo fixado.

Para backup consistente, interrompa escritas e copie juntos:

1. Todo o armazenamento de runs configurado, incluindo commits e metadados.
2. Todo o armazenamento de revisões publicadas de conteúdo.
3. Packages de origem e configuração da instalação, sem publicar segredos.
4. A versão completa do executável compatível; telemetria pode ser preservada
   separadamente para diagnóstico.

Não comprima, remova ou rotacione arquivos autoritativos como se fossem logs.
Não presuma que restaurar somente o JSON mais recente ou os packages atuais
reconstrói uma run histórica. Para restaurar, pare o processo, recupere o conjunto
coerente e a versão compatível, confira readiness e teste retomada/replay.
Detalhes em [persistência](persistence.md).

## Atualização, rollback e múltiplas instâncias

- Planeje parada controlada e backup antes da atualização.
- Preserve a publicação anterior inteira e suas revisões de conteúdo.
- A versão determinística atual é **32**, definida em
  `src/Core/Determinism/DeterministicContext.cs`. Não há migração silenciosa
  garantida de snapshots de versões anteriores.
- Rollback exige binários e dados compatíveis, não apenas trocar `API.dll`.
- Não inicie dois escritores contra o mesmo diretório para simular atualização
  sem downtime. Compartilhar arquivos, usar afinidade de sessão ou colocar um
  balanceador não certifica coordenação distribuída do store atual.

O caminho atual não está certificado para escala horizontal ou multiplayer.
Uma implementação alternativa de persistência precisa preservar os invariantes
de compare-and-append, idempotência e recuperação descritos em `persistence.md`.

## Docker e serviços: estado dos artefatos

O `Dockerfile`, `docker-compose.yml` e `.env.example` ainda contêm instruções
antigas e **não são uma receita certificada de publicação**. A revisão local
encontrou healthcheck `/api/health` em vez de `/api/v1/health/live`, uso de
`HERESCRIPT_ADMIN_KEY`, volume/configuração de eventos antigos e ausência de
volume explícito para o conteúdo publicado no Compose. O Dockerfile também
referencia `/src/data/configs` no estágio de build sem copiá-lo para esse caminho.
Não foi validada a disponibilidade de `curl` na imagem runtime.

Esses artefatos não foram modificados nesta atualização documental. Antes de
adotá-los, corrigir build, healthcheck, configuração, permissões e persistência,
e executar testes de restart/restauração. Integração nativa com gerenciadores
de serviços e export standalone também precisa de validação própria.

## Segurança e exposição de rede

O gameplay não possui a chave administrativa como autenticação de jogador.
Não confunda CORS, um `runId` ou `X-Admin-Key` com ownership/autorização por
jogador. A implantação padrão local deve permanecer em loopback; hospedagem
pública exige uma fronteira de acesso e revisão de segurança próprias.

Não distribua a chave de administração no cliente. Não copie as credenciais
do launcher de desenvolvimento. Não aplique cache genérico às sessões, ações
legais ou ao catálogo mutável; qualquer cache precisa respeitar revisão,
setting, contexto e contrato do endpoint.

## Evidência de desempenho e gates restantes

A promessa antiga de **5–8 ms** não descreve o sistema atual. A
[matriz REST de 2026-10-15](plans/REST_LATENCY_REMAINING_BOTTLENECKS.md)
registrou 526 interações POST + sessão, em três seeds, abaixo de 1 s; a maior
foi 802 ms na máquina do ensaio. Essa medição não inclui a resposta visual
renderizada na Godot nem comprova qualquer mod, carga concorrente ou hardware.

Build/Core/API e jornadas longas estão configurados na CI. A bateria Godot
continua local. Não apresentar resultados locais ou relatórios de versões
anteriores como aprovação de uma build posterior ou execução remota da CI.
Para próximos gates, consulte o [roadmap atual](roadmap/README.md).

## Fontes verificadas nesta revisão

- `src/API/Program.cs` e `src/API/appsettings*.json`: configuração e stores.
- `src/API/API.csproj`: conteúdo copiado na publicação.
- `src/API/Middleware/AdminKeyMiddleware.cs`: habilitação e chave.
- `src/API/Services/ToolAccessPolicy.cs`: permissões por perfil.
- `src/API/Controllers/SystemController.cs`: healthchecks v1.
- `.github/workflows/ci.yml`: gates .NET existentes.
- [Observabilidade](observability.md), [API](api/README.md) e
  [integração](CLIENT_INTEGRATION.md).
