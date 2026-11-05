# 🃏 Prototipagem de Conteúdo do MVP — HeroScript

Pasta dedicada à prototipagem rápida, balanceamento e especificação matemática de cartas, inscrições, modificadores (deltas), inimigos e regras de combate para o **MVP do HeroScript**.

---

## 📁 Arquivos desta Pasta

* **[`cartas_prototipo.ods`](cartas_prototipo.ods):** Planilha mestre de design do MVP compatível com **LibreOffice Calc**, **Microsoft Excel** e **Google Planilhas**. Contém 5 abas completas com todo o conteúdo de teste e balanceamento.
* **[`export_ods_to_json.py`](export_ods_to_json.py):** Script auxiliar em Python para converter todas as 5 abas da planilha diretamente em `cartas_mvp_export.json`.
* **[`cartas_mvp_export.json`](cartas_mvp_export.json):** JSON gerado automaticamente com cartas, inscrições, deltas, inimigos/chefes e regras de combate.
* **[`update_cartas_ods.py`](update_cartas_ods.py):** Script de recriação programática e atualização em lote da planilha `.ods`.

---

## 🔑 Pilares Mecânicos do MVP

1. **Energia Universal (3 por turno):** Orçamento tático simétrico para Herói e Inimigos.
2. **Mão Retida (Draw 2-3 / Cap 8-10):** Cartas não jogadas permanecem na mão para o próximo turno.
3. **Escudo Persistente (Teto = 100% Vida Máxima):** Não decai no fim do turno, permitindo planejamento defensivo sem risco de "parede infinita".
4. **Mitigação por Retornos Decrescentes ($K=100$):**
   $$\text{Dano Final} = \text{Dano Base} \times \frac{K}{R + K}$$
5. **Mod de Assinatura (Forja de Brasas):**
   No fim do turno, cada carta de `[Fogo, Ataque]` retida na mão ganha $+1$ marcador de Brasa (máx 3). Cada Brasa confere $+25\%$ de dano base da carta (até $+75\%$), consumida ao jogar.
6. **Tags Ortogonais:** Classificação dupla independente de **Elemento** (`Fogo`) $\times$ **Função** (`Ataque`, `Defesa`, `Buff`, `Payoff`).

---

## 📊 Estrutura das 5 Abas do `.ods`

### 1. Aba: `Cartas_MVP` (20 Cartas do Piromaníaco)
* **8 Ataques de Fogo:** *Golpe de Faísca*, *Labareda*, *Chama Perfurante*, *Combustão Dupla*, *Golpe da Forja*, *Lança Solar*, *Varredura Térmica*, *Impacto Cíndico*.
* **5 Defesas:** *Barreira de Cinzas*, *Muralha Ígnea*, *Postura da Brasa*, *Escudo Reativo*, *Escudo Solar*.
* **3 Buffs:** *Catalisador Ígnito*, *Oxigenação*, *Foco Piromântico*.
* **4 Payoffs:** *Condensar Chamas*, *Batida de Escudo Flamejante*, *Supernova*, *Forja Eterna*.

### 2. Aba: `Inscricoes_MVP` (1 Mod de Assinatura + 11 Inscrições)
* **Assinatura:** `mod_forja_de_brasas` (Custo 0 de Capacidade).
* **Inscrições de Dano & Fogo:** *Chama Ardente* (+20% increased Fire), *Precisão Térmica* (+25% Crit Chance), *Impacto Devastador* (+30% Crit Damage).
* **Inscrições Defensivas & Mitigação:** *Ferro Temperado* (+20% Block), *Baluarte Adamantino* (Teto de Escudo para 150% HP), *Couraça Pesada* (40 Armadura, $K=100$), *Resistência Ígnea* (30 Resistência a Fogo, $K=100$).
* **Inscrições de Queimadura & Payoff:** *Combustão Prolongada* (+1 Turno DoT), *Condutividade Térmica* (+2 Potência), *Cinzas Recicladas* (+1 Energia na Condensação), *Martelo da Forja* (3 Brasas quebram 50% de Armadura).

### 3. Aba: `Deltas_MVP` (6 Modificadores / Deltas de Script)
* `delta_rachador_chamas`: Multihit 3x (40% dano cada).
* `delta_elo_defensivo`: Concede 5 de Escudo Persistente após o ataque.
* `delta_infusao_comburente`: Adiciona cláusula de 2 Queimaduras ao ataque.
* `delta_otimizacao_energia`: Reduz o custo de energia em -1 (mínimo 1).
* `delta_ressonancia_brasa`: Aumenta bônus de Brasa de +25% para +40%.
* `delta_estabilidade_ignea`: Remove crítico em troca de +30% de dano base flat.

### 4. Aba: `Inimigos_MVP` (3 Comuns + 1 Elite + 1 Chefe em 2 Fases)
* **Autômato Batedor (Comum, 55 HP):** Ataques rápidos telegrafados com 1 turno de antecedência.
* **Drone de Lâminas Duplas (Comum, 45 HP):** Dano multihit frequente que desgasta escudos.
* **Sentinela Pesada (Comum, 75 HP, 35 Armadura):** Ciclo alternado de 14 de escudo e golpe de 14 dano.
* **Colosso de Ferrofundido (Elite, 120 HP, 50 Armadura, 35 Resist. Fogo):** Barreira de 18 escudo e Carga Brutal de 24 dano.
* **Guardião da Forja (Chefe em 2 Fases):**
  * **Fase 1 (O Martelo - 160 HP):** Teto de Dano por Turno = 35. Transiciona aos 50% HP (80 HP).
  * **Fase 2 (A Fornalha Desperta - 80 HP):** Limpa debuffs, ganha 30 de escudo, reduz armadura física, eleva resistência a fogo e telegrafa Erupção de 22 dano. Teto de dano por turno = 35.

### 5. Aba: `Regras_e_Metricas_MVP`
* **Especificação de Regras:** Energia 3, Mão Retida, Ciclo de Baralho, Escudo Persistente 100% HP, Fórmula de Mitigação $K=100$, Mod de Assinatura Forja de Brasas.
* **Métricas de Sucesso:** Decisão Real (Delta MCTS > 15%), Domínio de Defesa (< 40% turnos de pura defesa), Trivialidade (TTK 4-7 turnos), Relevância de Habilidade (Win Rate Bot Heurístico > 80% vs Ingênuo < 35%), Variedade de Builds (>= 3 builds vencem o Chefe), Impacto da Sorte (Variância entre seeds < 15%).

---

## 🚀 Como Sincronizar após Editar a Planilha

Após salvar qualquer alteração no LibreOffice Calc ou Excel:

```bash
python docs/card-prototyping/export_ods_to_json.py
```
