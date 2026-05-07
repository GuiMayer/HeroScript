# Test Configs

Esta pasta contém configurações de exemplo e teste para o Hero-Engine.

## Propósito

As configs aqui são usadas para:
- **Testes automatizados** - Validar o sistema de herança delta
- **Exemplos de referência** - Demonstrar como criar mods
- **Debugging** - Testar novas funcionalidades do sistema de config

## Configs Disponíveis

### test-orc
Config base para testes com tema orc. Herda de `alisyum` e adiciona skills de exemplo.

**Estrutura:**
```
test-orc/
├── config.json                    # Metadados (herda de alisyum)
├── Resources/
│   ├── Pipelines/
│   │   └── MathFormulas.json      # Vazio (usa fórmulas de alisyum)
│   └── Skills/
│       └── Skills.json            # Skills base (FIREBALL, HEALING_WAVE, SHIELD_BASH)
```

### test-orc-mod
Mod que modifica a config test-orc usando sistema delta estruturado.

**Modificações:**
- Aumenta dano do FIREBALL (MERGE_DEEP)
- Remove HEALING_WAVE (DELETE)
- Modifica SHIELD_BASH para "Brutal Bash"
- Adiciona nova skill BLOOD_RAGE

**Estrutura:**
```
test-orc-mod/
├── config.json                    # Herda de test-orc
├── Resources/
│   ├── Pipelines/
│   │   └── MathFormulas.json      # Deltas de fórmulas
│   └── Skills/
│       └── Skills.json            # Deltas de skills
```

### test-orc-mod-hardcore
Mod hardcore que herda de test-orc-mod (cadeia de 3 níveis).

**Modificações:**
- Aumenta ainda mais o BLOOD_RAGE (MERGE_DEEP)
- Adiciona efeito EXPLOSION ao FIREBALL (ARRAY_APPEND)
- Modifica SHIELD_BASH para "Devastating Bash" com novos efeitos
- Adiciona skill exclusiva WARLORD_COMMAND

**Cadeia de herança:**
```
alisyum (base oficial)
  └─> test-orc (base de teste)
       └─> test-orc-mod (mod orc)
            └─> test-orc-mod-hardcore (mod hardcore)
```

## Como Usar

### Para Testes Automatizados

Os testes em `Core/Tests/` referenciam estas configs automaticamente:

```bash
dotnet test Core
```

### Para Testes Manuais

Use os scripts em `scripts/`:

```bash
# Listar configs disponíveis
scripts\list-configs.bat

# Executar com test-orc-mod
scripts\run-orc-mod.bat
```

### Para Desenvolvimento de Mods

1. **Copie uma config de exemplo** para `%APPDATA%\HeroScript\`
2. **Modifique** os arquivos delta conforme necessário
3. **Teste** usando `dotnet run --project Core -- --config seu-mod`

## Estrutura de uma Config

Toda config deve ter:

```
my-config/
├── config.json                    # Obrigatório - metadados
├── Resources/
│   └── Pipelines/
│       └── MathFormulas.json      # Obrigatório - fórmulas (pode ser vazio {})
├── runs/                          # Opcional - logs de execução
└── saves/                         # Opcional - saves do jogo
```

## Sistema Delta

As configs aqui demonstram todas as 9 operações delta:

| Operação | Exemplo | Config |
|----------|---------|--------|
| REPLACE | Substituir recurso inteiro | test-orc-mod (BLOOD_RAGE) |
| MERGE_SHALLOW | Mesclar nível superior | - |
| MERGE_DEEP | Mesclar recursivamente | test-orc-mod (FIREBALL) |
| ARRAY_APPEND | Adicionar ao final | test-orc-mod-hardcore (FIREBALL effects) |
| ARRAY_PREPEND | Adicionar ao início | - |
| ARRAY_REMOVE_INDEX | Remover por índice | - |
| ARRAY_REPLACE_INDEX | Substituir por índice | - |
| FIELD_DELETE | Remover campo | test-orc-mod-hardcore (SHIELD_BASH) |
| DELETE | Remover recurso | test-orc-mod (HEALING_WAVE) |

## Documentação

Para mais informações sobre o sistema de configs e delta:
- `docs/CONFIG_SYSTEM.md` - Documentação completa do sistema
- `docs/DELTA_REFERENCE.md` - Referência rápida de operações delta

## Notas

- **Não use estas configs em produção** - São apenas para testes
- **Não modifique diretamente** - Crie suas próprias configs em `user://`
- **Mantenha sincronizado** - Ao adicionar novos testes, atualize este README
