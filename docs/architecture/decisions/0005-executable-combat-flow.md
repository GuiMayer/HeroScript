# ADR 0005 — Fluxo de combate executável

**Status:** aceito

## Contexto

O schema anuncia fases, prioridade, reações e stack que o runtime ainda não
executa integralmente. Configuração aceita mas ignorada é comportamento
indefinido.

## Decisão

- A sequência de fases é um grafo validado e executável.
- Os papéis `Start`, `Middle` e `End` são obrigatórios, mas podem possuir várias
  fases alcançáveis.
- Cursor, janela de prioridade e stack são estado imutável serializável.
- Ordem de ativação e seus boundaries vêm do game mode fixado.
- Player e IA consultam o mesmo `LegalActionResolver`.
- Toda opção publicável deve ter semântica runtime; opções futuras não podem ser
  aceitas pelo validador.
- A engine resolve uma transação inteira e entrega frames; Godot apenas coleta
  input e reproduz apresentação.

