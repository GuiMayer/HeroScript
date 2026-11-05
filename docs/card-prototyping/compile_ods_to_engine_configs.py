"""
compile_ods_to_engine_configs.py
Compilador automatizado que traduz diretamente cartas_mvp_export.json (gerado do ODS)
para os arquivos de configuracao oficiais da engine em data/configs/prototype-test/Resources/.
Garante que a planilha seja a unica fonte de verdade (Single Source of Truth), evitando qualquer divergencia manual.
"""

import json
import os
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent
EXPORT_JSON_PATH = BASE_DIR / "cartas_mvp_export.json"
CONFIGS_DIR = BASE_DIR.parent.parent / "data" / "configs" / "prototype-test" / "Resources"

def load_exported_data():
    if not EXPORT_JSON_PATH.exists():
        raise FileNotFoundError(f"Arquivo {EXPORT_JSON_PATH} nao encontrado. Rode export_ods_to_json.py primeiro.")
    with open(EXPORT_JSON_PATH, "r", encoding="utf-8") as f:
        return json.load(f)

def parse_tags(raw_tags):
    if isinstance(raw_tags, list):
        return [str(t).strip().lower() for t in raw_tags if str(t).strip()]
    elif isinstance(raw_tags, str):
        return [t.strip().lower() for t in raw_tags.split(",") if t.strip()]
    return []

def compile_cards(cards_data):
    cards_dict = {}
    for c in cards_data:
        cid = c["id"]
        tags = parse_tags(c.get("tags", []))
        element = c.get("element", "Fogo").strip().lower()
        if element not in tags:
            tags.append(element)
        tags.append("prototype")
        tags.append("pyromancer")

        components = []

        # 1. Componente de Custo de Energia
        cost_amount = int(c.get("energy_cost", 1))
        components.append({
            "type": "cost",
            "componentId": "cost",
            "order": 10,
            "costs": {
                "costs": [
                    {
                        "resourceId": "energy",
                        "amount": cost_amount
                    }
                ]
            }
        })

        # 2. Componente de Dano Principal (se houver)
        base_dmg = float(c.get("base_damage", 0))
        target_type = c.get("target", "Inimigo").strip()
        is_aoe = target_type == "Todos_Inimigos"
        is_self = target_type == "Self"

        dmg_tags = ["damage", "attack"]
        if "fogo" in tags:
            dmg_tags.append("fire")
        elif "fisico" in tags or "físico" in tags:
            dmg_tags.append("physical")

        if base_dmg > 0:
            components.append({
                "type": "effect",
                "componentId": "main",
                "order": 20,
                "effect": {
                    "effectId": f"{cid}.main",
                    "type": "DAMAGE",
                    "target": "ALL_ENEMIES" if is_aoe else "TARGET",
                    "targetResource": "health",
                    "repeat": 1,
                    "tags": dmg_tags,
                    "parameters": [
                        {
                            "parameter": "Amount",
                            "flatValue": int(base_dmg),
                            "unitId": "points",
                            "channel": "effect_amount",
                            "pipelineId": "prototype_direct_amount",
                            "stageIds": ["w1_target"],
                            "distribution": {
                                "sourceStageIds": ["w1_origin"],
                                "allocation": {
                                    "mode": "Quantized",
                                    "quantum": 1
                                }
                            }
                        }
                    ],
                    "targetLoss": {
                        "policy": "Retarget",
                        "retarget": "LOWEST_RESOURCE_ENEMY"
                    },
                    "selectionResourceId": "health"
                }
            })

        # 3. Componente de Escudo (se houver)
        shield = float(c.get("shield_generated", 0))
        if shield > 0:
            components.append({
                "type": "effect",
                "componentId": "defense" if base_dmg > 0 else "main",
                "order": 25 if base_dmg > 0 else 20,
                "effect": {
                    "effectId": f"{cid}.defense",
                    "type": "MODIFY_RESOURCE",
                    "target": "SELF",
                    "targetResource": "block",
                    "operation": "ADD",
                    "flatValue": int(shield),
                    "tags": ["defense", "block"]
                }
            })

        # 4. Componente de Status / Queimadura (se houver)
        status_applied = c.get("status_applied", "Nenhum")
        status_stacks = int(c.get("status_stacks", 0))
        if status_applied == "Queimadura" and status_stacks > 0:
            components.append({
                "type": "effect",
                "componentId": "burn_application",
                "order": 30,
                "effect": {
                    "effectId": f"{cid}.burn",
                    "type": "APPLY_STATUS",
                    "target": "ALL_ENEMIES" if is_aoe else "TARGET",
                    "statusId": "prototype_burn",
                    "statusStacks": status_stacks,
                    "statusDuration": 3,
                    "targetLoss": {
                        "policy": "Skip"
                    },
                    "tags": ["status", "residual", "fire"],
                    "payloadBindings": [
                        {
                            "parameterId": "potency",
                            "flatValue": 2
                        }
                    ]
                }
            })

        # 5. Componente de Alvejamento
        if is_self:
            components.append({
                "type": "targeting",
                "componentId": "targeting",
                "order": 80,
                "target": "SELF",
                "minimumTargets": 0,
                "maximumTargets": 0,
                "allowSelf": True
            })
        elif is_aoe:
            components.append({
                "type": "targeting",
                "componentId": "targeting",
                "order": 80,
                "target": "ALL_ENEMIES",
                "minimumTargets": 1,
                "maximumTargets": 99
            })
        else:
            components.append({
                "type": "targeting",
                "componentId": "targeting",
                "order": 80,
                "target": "TARGET",
                "minimumTargets": 1,
                "maximumTargets": 1
            })

        # 6. Componente de Destino pós-resolucao
        components.append({
            "type": "disposition",
            "componentId": "disposition",
            "order": 90,
            "cardZoneResolutionFlowId": "card.played.to-discard"
        })

        card_entry = {
            "cardId": cid,
            "rarity": c.get("rarity", "Common"),
            "basePrices": [],
            "tags": tags,
            "metadata": {
                "displayName": c.get("name", cid),
                "nameKey": f"card.{cid}",
                "artKey": f"placeholder.{cid}",
                "ruleText": c.get("rule_text", ""),
                "signatureInteraction": c.get("signature_interaction", "")
            },
            "transformationSlots": [
                {
                    "slotId": "affinity",
                    "capacity": 1,
                    "allowedCategories": ["Affinity"]
                },
                {
                    "slotId": "behavior",
                    "capacity": 2,
                    "allowedCategories": ["Behavior"]
                }
            ],
            "components": components,
            "decomposeRewards": [
                {
                    "resourceId": "parts",
                    "amount": 3
                }
            ]
        }
        cards_dict[cid] = card_entry

    return cards_dict

def compile_inscriptions(insc_data):
    inscriptions_dict = {}
    for insc in insc_data:
        iid = insc["id"]
        cost = int(insc.get("capacity_cost", 2))
        tag_list = parse_tags(insc.get("tags", []))

        bonuses = []
        channel = insc.get("calculation_channel", "effect_amount")
        bucket = insc.get("bucket", "increased")
        op_val_str = insc.get("operation_value", "0.20")
        
        # Extrair float se possivel
        val = 0.20
        try:
            val_clean = op_val_str.replace("%", "").replace("+", "").split()[0]
            val = float(val_clean)
            if "%" in op_val_str or val > 1.0:
                val = val / 100.0
        except Exception:
            val = 0.20

        bonuses.append({
            "attributeId": iid.replace("insc_", "").replace("mod_", ""),
            "influence": {
                "influenceId": f"prototype.{iid}",
                "channel": channel,
                "bucket": bucket,
                "value": val,
                "subjectKeyCondition": {
                    "keys": tag_list,
                    "requiredKeys": [tag_list[0]] if tag_list else ["fire"],
                    "minimumCount": 1
                }
            }
        })

        inscriptions_dict[iid] = {
            "inscriptionId": iid,
            "equipmentCost": cost,
            "polarity": "neutral",
            "rank": 1,
            "ranks": [
                {
                    "rank": 1,
                    "equipmentCost": cost,
                    "influenceValues": {
                        f"prototype.{iid}": val
                    }
                }
            ],
            "attributeBonuses": bonuses,
            "presentation": {
                "name": insc.get("name", iid),
                "description": insc.get("description", "")
            }
        }
    return inscriptions_dict

def compile_deltas(deltas_data):
    upgrades_dict = {}
    items_dict = {}

    for d in deltas_data:
        did = d["id"]
        cat = d.get("category", "Behavior")
        target_slot = d.get("target_slot", "behavior")

        upgrade_entry = {
            "upgradeId": did,
            "category": cat,
            "maxApplications": 1,
            "slotId": target_slot,
            "cardDefinitionIds": [], # Vazio significa compativel com qualquer carta valida
            "patches": [
                {
                    "type": "tags",
                    "add": [did.replace("delta_", "")]
                }
            ]
        }
        upgrades_dict[did] = upgrade_entry

        item_id = f"{did}_item"
        items_dict[item_id] = {
            "itemId": item_id,
            "upgradeId": did,
            "presentation": {
                "name": d.get("name", did),
                "description": d.get("description", "")
            }
        }

    return upgrades_dict, items_dict

def compile_entities(enemies_data):
    entities_dict = {}

    # Hero Definition
    entities_dict["prototype_hero"] = {
        "definitionId": "prototype_hero",
        "displayName": "Piromaníaco (Herói)",
        "components": [
            {
                "type": "resources",
                "componentId": "resources",
                "pools": {
                    "health": {
                        "current": 100,
                        "max": 100
                    },
                    "energy": {
                        "current": 3,
                        "max": 3
                    },
                    "block": {
                        "current": 0,
                        "max": 100,
                        "canExceedMax": False
                    }
                }
            },
            {
                "type": "stats",
                "componentId": "stats",
                "values": {
                    "armor": 0,
                    "fire_resistance": 0,
                    "strength": 0,
                    "critical_chance": 5,
                    "critical_damage": 150
                }
            }
        ]
    }

    # Enemies
    for e in enemies_data:
        eid = e["id"]
        hp = float(e.get("max_health", 50))
        
        # Parse armor
        armor_val = 0
        try:
            armor_str = str(e.get("armor_k", "0")).split()[0]
            armor_val = int(armor_str)
        except Exception:
            armor_val = 0

        # Parse fire res
        res_val = 0
        try:
            res_str = str(e.get("fire_resistance_k", "0")).split()[0]
            res_val = int(res_str)
        except Exception:
            res_val = 0

        entities_dict[eid] = {
            "definitionId": eid,
            "displayName": e.get("name", eid),
            "enemyType": e.get("type", "Common"),
            "components": [
                {
                    "type": "resources",
                    "componentId": "resources",
                    "pools": {
                        "health": {
                            "current": int(hp),
                            "max": int(hp)
                        },
                        "energy": {
                            "current": int(e.get("energy_per_turn", 3)),
                            "max": int(e.get("energy_per_turn", 3))
                        },
                        "block": {
                            "current": 0,
                            "max": 999
                        }
                    }
                },
                {
                    "type": "stats",
                    "componentId": "stats",
                    "values": {
                        "armor": armor_val,
                        "fire_resistance": res_val,
                        "critical_chance": 0,
                        "critical_damage": 100
                    }
                }
            ],
            "specialRules": {
                "ruleText": e.get("special_rules", ""),
                "deckSummary": e.get("deck_cards", ""),
                "gambitSummary": e.get("gambit_patterns", "")
            }
        }

    return entities_dict

def compile_all():
    print(f"Lendo dados de {EXPORT_JSON_PATH}...")
    data = load_exported_data()

    print(f"Diretório de destino: {CONFIGS_DIR}")
    CONFIGS_DIR.mkdir(parents=True, exist_ok=True)

    # 1. Cards
    cards_dict = compile_cards(data.get("cards", []))
    cards_file = CONFIGS_DIR / "cards_mvp.json"
    with open(cards_file, "w", encoding="utf-8") as f:
        json.dump(cards_dict, f, ensure_ascii=False, indent=2)
    print(f"-> {len(cards_dict)} cartas compiladas em {cards_file.name}")

    # 2. Inscriptions
    insc_dict = compile_inscriptions(data.get("inscriptions", []))
    insc_file = CONFIGS_DIR / "inscriptions_mvp.json"
    with open(insc_file, "w", encoding="utf-8") as f:
        json.dump(insc_dict, f, ensure_ascii=False, indent=2)
    print(f"-> {len(insc_dict)} inscrições compiladas em {insc_file.name}")

    # 3. Deltas & Upgrades
    upgrades_dict, items_dict = compile_deltas(data.get("deltas", []))
    upg_file = CONFIGS_DIR / "card_upgrades_mvp.json"
    items_file = CONFIGS_DIR / "card_modifier_items_mvp.json"
    with open(upg_file, "w", encoding="utf-8") as f:
        json.dump(upgrades_dict, f, ensure_ascii=False, indent=2)
    with open(items_file, "w", encoding="utf-8") as f:
        json.dump(items_dict, f, ensure_ascii=False, indent=2)
    print(f"-> {len(upgrades_dict)} deltas compilados em {upg_file.name} e {items_file.name}")

    # 4. Entities
    entities_dict = compile_entities(data.get("enemies", []))
    ent_file = CONFIGS_DIR / "entities_mvp.json"
    with open(ent_file, "w", encoding="utf-8") as f:
        json.dump(entities_dict, f, ensure_ascii=False, indent=2)
    print(f"-> {len(entities_dict)} entidades compiladas em {ent_file.name}")

    print("\n[OK] Compilacao automatica concluida com 100% de sucesso!")

if __name__ == "__main__":
    compile_all()
