"""
Script de exportação automática: cartas_prototipo.ods -> JSON para HeroScript
Lê todas as 5 abas estruturadas da planilha ODS e gera o JSON completo do MVP.
"""

import os
import json
import pandas as pd

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ODS_PATH = os.path.join(SCRIPT_DIR, "cartas_prototipo.ods")
JSON_OUTPUT_PATH = os.path.join(SCRIPT_DIR, "cartas_mvp_export.json")

def export_all():
    if not os.path.exists(ODS_PATH):
        print(f"Erro: Arquivo não encontrado em {ODS_PATH}")
        return

    print(f"Carregando abas de {ODS_PATH}...")
    excel_file = pd.ExcelFile(ODS_PATH, engine="odf")
    sheet_names = excel_file.sheet_names
    print(f"Abas encontradas: {sheet_names}")

    export_payload = {}

    # 1. Cartas_MVP
    if "Cartas_MVP" in sheet_names:
        df_cards = pd.read_excel(ODS_PATH, sheet_name="Cartas_MVP", engine="odf")
        cards = []
        for _, row in df_cards.iterrows():
            cards.append({
                "id": str(row.get("ID", "")).strip(),
                "name": str(row.get("Nome", "")).strip(),
                "module": str(row.get("Baralho", "")).strip(),
                "function_type": str(row.get("Tipo_Funcao", "")).strip(),
                "element_tag": str(row.get("Tag_Elemento", "")).strip(),
                "orthogonal_tags": [t.strip() for t in str(row.get("Tags_Ortogonais", "")).split(",") if t.strip()],
                "energy_cost": int(row.get("Custo_Energia", 1)),
                "rarity": str(row.get("Raridade", "")).strip(),
                "targeting": str(row.get("Alvo", "")).strip(),
                "base_damage": float(row.get("Dano_Base", 0)),
                "mitigation_formula": str(row.get("Formula_Mitigacao", "")).strip(),
                "status_applied": str(row.get("Status_Aplicado", "Nenhum")).strip(),
                "status_amount": int(row.get("Qtd_Status", 0)),
                "shield_amount": float(row.get("Escudo_Gerado", 0)),
                "starting_deck_copies": int(row.get("Copias_Iniciais", 1)),
                "rule_text": str(row.get("Texto_Regra", "")).strip(),
                "brasa_interaction": str(row.get("Interacao_Brasa", "")).strip(),
                "designer_notes": str(row.get("Notas_Design", "")).strip()
            })
        export_payload["cards"] = cards
        print(f"-> {len(cards)} cartas processadas.")

    # 2. Inscricoes_MVP
    if "Inscricoes_MVP" in sheet_names:
        df_insc = pd.read_excel(ODS_PATH, sheet_name="Inscricoes_MVP", engine="odf")
        inscriptions = []
        for _, row in df_insc.iterrows():
            inscriptions.append({
                "id": str(row.get("ID", "")).strip(),
                "name": str(row.get("Nome", "")).strip(),
                "type": str(row.get("Tipo", "")).strip(),
                "capacity_cost": float(row.get("Custo_Capacidade", 0)),
                "tags": [t.strip() for t in str(row.get("Tags", "")).split(",") if t.strip()],
                "calculation_channel": str(row.get("Canal_Calculo", "")).strip(),
                "bucket": str(row.get("Bucket", "")).strip(),
                "operation_value": str(row.get("Valor_Operacao", "")).strip(),
                "description": str(row.get("Descricao", "")).strip(),
                "tactical_notes": str(row.get("Notas_Taticas", "")).strip()
            })
        export_payload["inscriptions"] = inscriptions
        print(f"-> {len(inscriptions)} inscrições processadas.")

    # 3. Deltas_MVP
    if "Deltas_MVP" in sheet_names:
        df_deltas = pd.read_excel(ODS_PATH, sheet_name="Deltas_MVP", engine="odf")
        deltas = []
        for _, row in df_deltas.iterrows():
            deltas.append({
                "id": str(row.get("ID", "")).strip(),
                "name": str(row.get("Nome", "")).strip(),
                "category": str(row.get("Categoria", "")).strip(),
                "slot": str(row.get("Slot", "")).strip(),
                "compatible_cards": [c.strip() for c in str(row.get("Cartas_Compativeis", "")).split(",") if c.strip()],
                "script_modification": str(row.get("Modificacao_Script", "")).strip(),
                "description": str(row.get("Descricao", "")).strip()
            })
        export_payload["deltas"] = deltas
        print(f"-> {len(deltas)} deltas processados.")

    # 4. Inimigos_MVP
    if "Inimigos_MVP" in sheet_names:
        df_enemies = pd.read_excel(ODS_PATH, sheet_name="Inimigos_MVP", engine="odf")
        enemies = []
        for _, row in df_enemies.iterrows():
            enemies.append({
                "id": str(row.get("ID", "")).strip(),
                "name": str(row.get("Nome", "")).strip(),
                "type": str(row.get("Tipo", "")).strip(),
                "max_health": float(row.get("Vida_Max", 0)),
                "armor_k": str(row.get("Armadura_K", "")).strip(),
                "fire_resistance_k": str(row.get("Resist_Fogo_K", "")).strip(),
                "energy_per_turn": int(row.get("Energia_Turno", 3)),
                "deck_cards": str(row.get("Baralho_Cartas", "")).strip(),
                "gambit_patterns": str(row.get("Padrao_Intencoes_Gambits", "")).strip(),
                "special_rules": str(row.get("Regra_Especial", "")).strip()
            })
        export_payload["enemies"] = enemies
        print(f"-> {len(enemies)} inimigos/chefes processados.")

    # 5. Regras_e_Metricas_MVP
    if "Regras_e_Metricas_MVP" in sheet_names:
        df_rules = pd.read_excel(ODS_PATH, sheet_name="Regras_e_Metricas_MVP", engine="odf")
        rules_metrics = []
        for _, row in df_rules.iterrows():
            rules_metrics.append({
                "category": str(row.get("Categoria", "")).strip(),
                "parameter": str(row.get("Parametro", "")).strip(),
                "value": str(row.get("Valor_Configurado", "")).strip(),
                "description": str(row.get("Descricao_Mecanica", "")).strip(),
                "remedy_if_failed": str(row.get("Acao_Remedio_Se_Falhar", "")).strip()
            })
        export_payload["rules_and_metrics"] = rules_metrics
        print(f"-> {len(rules_metrics)} regras e métricas processadas.")

    with open(JSON_OUTPUT_PATH, "w", encoding="utf-8") as f:
        json.dump(export_payload, f, ensure_ascii=False, indent=2)

    print(f"\nSucesso total! Exportação gerada em:")
    print(f"-> {JSON_OUTPUT_PATH}")

if __name__ == "__main__":
    export_all()
