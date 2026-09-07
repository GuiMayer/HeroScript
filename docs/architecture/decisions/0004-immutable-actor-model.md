# ADR 0004 — Modelo imutável de ator

**Status:** aceito

## Contexto

`Entity` mutável e `CombatEntity` imutável modelam a mesma identidade com ciclos
de vida diferentes. `IsHero` também transforma uma escolha de conteúdo em regra
da engine.

## Decisão

- Uma definição declara componentes de dados; uma instância guarda component
  states imutáveis.
- O runtime usa um único `CombatActorState` no roster do combate.
- Lado e relações definem aliados/oponentes; `ControllerBinding` define quem
  escolhe ações.
- Hero, enemy e companion podem ser tags ou projeções, nunca branches de regra.
- Componentes não guardam owner, callbacks ou métodos de mutação.
- Materialização é unidirecional: definição + cenário -> estado.
- Controllers são políticas puras referenciadas por ID, não objetos dentro do
  ator.

