# HeroScript Engine

Sistema de engine modular com suporte a herança delta para configurações e mods.

## Estrutura do Projeto

```
HeroScript/
├── docs/                           # 📚 Documentação
│   ├── CONFIG_SYSTEM.md           # Sistema de configuração e herança delta
│   └── DELTA_REFERENCE.md         # Referência rápida de operações delta
│
├── scripts/                        # 🔧 Scripts de conveniência
│   ├── list-configs.bat           # Lista configs disponíveis
│   ├── run-alisyum.bat            # Executa com config padrão
│   ├── run-orc-mod.bat            # Executa com mod de teste
│   └── run-tests.bat              # Executa todos os testes
│
├── tests/                          # 🧪 Testes e configs de exemplo
│   └── configs/                   # Configs para testes automatizados
│       ├── test-orc/              # Config base de teste
│       ├── test-orc-mod/          # Mod com deltas estruturados
│       └── test-orc-mod-hardcore/ # Cadeia de 3 níveis
│
├── Core/                           # 💎 Engine principal
│   ├── Config/                    # Sistema de configuração
│   │   ├── Delta/                 # Sistema delta estruturado
│   │   ├── ConfigManager.cs
│   │   ├── ConfigValidator.cs
│   │   └── ResourceLoader.cs
│   ├── Math/                      # Engine matemático
│   ├── Tests/                     # Testes unitários
│   └── Resources/                 # Recursos base (dev mode)
│
├── API/                            # 🌐 API REST
├── Dashboard/                      # 📊 Dashboard web
└── Mods/                           # 🎮 Sistema de gerenciamento de mods
```

## Quick Start

### Executar com Config Padrão

```bash
dotnet run --project Core -- --config alisyum
```

### Executar Testes

```bash
dotnet test Core
# ou
scripts\run-tests.bat
```

### Listar Configs Disponíveis

```bash
dotnet run --project Core -- --list
# ou
scripts\list-configs.bat
```

## Sistema de Configuração

O HeroScript usa um sistema de **herança delta** que permite mods modificarem apenas as partes necessárias da configuração base.

### Localização de Configs

- **Produção**: `%APPDATA%\HeroScript\` (user://)
- **Desenvolvimento**: `Core/Resources/` (fallback)
- **Testes**: `tests/configs/` (apenas para testes automatizados)

### Criar um Mod

1. Crie uma pasta em `%APPDATA%\HeroScript\seu-mod\`
2. Adicione `config.json` com metadados
3. Crie `Resources/Pipelines/MathFormulas.json` com deltas
4. Execute: `dotnet run --project Core -- --config seu-mod`

### Sistema Delta Estruturado

O sistema suporta 9 operações delta para modificações granulares:

- **REPLACE** - Substitui recurso inteiro
- **MERGE_SHALLOW** - Mescla nível superior
- **MERGE_DEEP** - Mescla recursivamente
- **ARRAY_APPEND** - Adiciona ao final de array
- **ARRAY_PREPEND** - Adiciona ao início de array
- **ARRAY_REMOVE_INDEX** - Remove por índice
- **ARRAY_REPLACE_INDEX** - Substitui por índice
- **FIELD_DELETE** - Remove campo específico
- **DELETE** - Remove recurso inteiro

Exemplo de delta:

```json
{
  "FIREBALL": {
    "$delta": {
      "$op": "MERGE_DEEP",
      "$data": {
        "damage": { "base": 120 }
      }
    }
  }
}
```

## Documentação

- **[CONFIG_SYSTEM.md](docs/CONFIG_SYSTEM.md)** - Documentação completa do sistema de configuração
- **[DELTA_REFERENCE.md](docs/DELTA_REFERENCE.md)** - Referência rápida de operações delta
- **[tests/configs/README.md](tests/configs/README.md)** - Documentação das configs de teste

## Desenvolvimento

### Estrutura de uma Config

```
my-config/
├── config.json                    # Metadados (obrigatório)
├── Resources/
│   └── Pipelines/
│       └── MathFormulas.json      # Fórmulas (obrigatório, pode ser {})
├── runs/                          # Logs de execução
└── saves/                         # Saves do jogo
```

### Executar Testes

```bash
# Todos os testes
dotnet test Core

# Testes específicos
dotnet test Core --filter "FullyQualifiedName~ResourceLoaderTests"
```

### Build

```bash
dotnet build Core
```

## Arquitetura

### Core Components

- **ConfigManager** - Gerencia carregamento e herança de configs
- **ConfigValidator** - Valida estrutura e detecta ciclos
- **ResourceLoader<T>** - Carregador genérico com suporte a delta
- **DeltaMerger** - Aplica operações delta
- **MathEngine** - Engine de fórmulas matemáticas
- **FormulaLoader** - Carregador específico para fórmulas

### Fluxo de Carregamento

```
1. ConfigManager.LoadConfig("test-orc-mod")
2. ConfigValidator.ValidateConfig()
3. ConfigManager.ResolveInheritanceChain() → ["alisyum", "test-orc", "test-orc-mod"]
4. ResourceLoader.LoadResources()
   ├─> Carrega base (alisyum)
   ├─> Aplica deltas (test-orc)
   └─> Aplica deltas (test-orc-mod)
5. Resultado final mesclado
```

## Contribuindo

1. Crie uma branch para sua feature
2. Implemente com testes
3. Execute `dotnet test Core` para validar
4. Faça commit seguindo conventional commits
5. Abra um pull request

## Licença

[Adicionar licença aqui]

---

**Versão:** 2.0.0  
**Data:** 2026-05-07
