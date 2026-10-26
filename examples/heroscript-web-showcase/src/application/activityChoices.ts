import { deepClone, isPlainObject, JsonObject } from '../contracts/types';

/**
 * Maps advertised choices to command payloads. Never invents prices, rules or IDs.
 * Port of scripts/application/activity_choices.gd
 */
export class ActivityChoices {
  static readonly TRANSFORMATION_TYPES = [
    'UPGRADE_CARD',
    'REMOVE_CARD_TRANSFORMATION',
    'REPLACE_CARD_TRANSFORMATION',
  ];

  static isInteractive(choices: JsonObject[]): boolean {
    const gameplay = choices.filter(
      (c) => !['ABANDON_RUN', 'APPLY_CARD_MODIFIER_ITEM'].includes(String(c.type ?? ''))
    );
    if (gameplay.length === 0) return false;
    if (
      Object.keys(ActivityChoices.singleEntry(choices)).length > 0 ||
      Object.keys(ActivityChoices.onlyCompletion(choices)).length > 0
    ) {
      return false;
    }
    return true;
  }
  static build(run: JsonObject, commands: any[]): JsonObject[] {
    const choices: JsonObject[] = [];
    for (const command of commands || []) {
      const type = String(command?.type ?? '');
      const valid: JsonObject = isPlainObject(command?.validPayload) ? command.validPayload : {};
      switch (type) {
        case 'APPLY_LOADOUT':
        case 'TRANSFER_INSCRIPTION':
        case 'APPLY_DECK_COMPARTMENTS':
          // Dedicated workspaces; never invent a build payload.
          continue;
        case 'OPEN_INSCRIPTION_OFFER':
          choices.push(
            ActivityChoices.choice(
              type,
              {
                actorId: run.playerEntityId ?? '',
                stationId: valid.stationId ?? '',
              },
              String(valid.stationId ?? ''),
              'inscription'
            )
          );
          break;
        case 'CHOOSE_INSCRIPTION_OFFER':
          for (const option of valid.options ?? []) {
            const c = ActivityChoices.choice(
              type,
              { offerId: valid.offerId, inscriptionId: option.inscriptionId },
              String(option.inscriptionId),
              'inscription'
            );
            c.presentation = deepClone(option.presentation ?? {});
            c.costs = deepClone(valid.costs ?? []);
            choices.push(c);
          }
          if (valid.canDecline) {
            choices.push(
              ActivityChoices.choice(
                type,
                { offerId: valid.offerId, inscriptionId: null },
                '',
                'inscription',
                true
              )
            );
          }
          break;
        case 'ACQUIRE_CARD_MODIFIER_ITEM':
          for (const id of valid.itemDefinitionIds ?? []) {
            const c = ActivityChoices.choice(
              type,
              { itemDefinitionId: id },
              String(id),
              'modifier_item'
            );
            c.costs = deepClone(valid.costs ?? []);
            choices.push(c);
          }
          break;
        case 'APPLY_CARD_MODIFIER_ITEM':
          for (const option of valid.options ?? []) {
            choices.push(
              ActivityChoices.choice(
                type,
                option,
                String(option.itemInstanceId ?? ''),
                'modifier_item',
                true
              )
            );
          }
          break;
        case 'CHOOSE_DIALOGUE_OPTION':
          for (const id of valid.choiceIds ?? []) {
            choices.push(
              ActivityChoices.choice(
                type,
                {
                  dialogueInstanceId: valid.dialogueInstanceId,
                  nodeId: valid.nodeId,
                  choiceId: id,
                },
                String(id),
                'dialogue'
              )
            );
          }
          break;
        case 'ADVANCE_NODE':
          for (const id of command?.targetNodeIds ?? []) {
            choices.push(
              ActivityChoices.choice(type, { targetNodeId: id }, String(id), 'node')
            );
          }
          break;
        case 'PICK_CARD_REWARD':
        case 'DECOMPOSE_CARD_REWARD':
          for (const id of valid.cardIds ?? []) {
            const payload: JsonObject = { selectionInstanceId: valid.selectionInstanceId };
            if (type === 'PICK_CARD_REWARD') {
              payload.cardIds = [id];
            } else {
              payload.cardId = id;
            }
            choices.push(
              ActivityChoices.choice(
                type,
                payload,
                String(id),
                'card',
                type === 'DECOMPOSE_CARD_REWARD'
              )
            );
          }
          break;
        case 'BUY_SHOP_ITEM':
        case 'APPLY_PREPARATION_OPTION': {
          const shop = type === 'BUY_SHOP_ITEM';
          const ids = valid[shop ? 'itemIds' : 'optionIds'] ?? [];
          for (const id of ids) {
            const instanceKey = shop ? 'shopInstanceId' : 'preparationInstanceId';
            const payload: JsonObject = {
              [instanceKey]: valid[instanceKey],
              [shop ? 'itemId' : 'optionId']: id,
            };
            choices.push(ActivityChoices.choice(type, payload, String(id), 'content'));
          }
          break;
        }
        case 'UPGRADE_CARD':
        case 'REMOVE_CARD_TRANSFORMATION':
        case 'REPLACE_CARD_TRANSFORMATION':
          for (const option of valid.options ?? []) {
            const cardId = option.cardInstanceId;
            const upgradeId = option.upgradeId;
            const definition = String(option.cardDefinitionId ?? cardId);
            const payload: JsonObject = { cardInstanceId: cardId, upgradeId };
            if (type === 'REMOVE_CARD_TRANSFORMATION') {
              delete payload.upgradeId;
            }
            if (
              option.targetTransformationId !== null &&
              option.targetTransformationId !== undefined
            ) {
              payload.transformationId = option.targetTransformationId;
            }
            const c = ActivityChoices.choice(type, payload, definition, 'card');
            c.variantId = upgradeId;
            c.costs = deepClone(option.costs ?? []);
            choices.push(c);
          }
          break;
        case 'REROLL_CARD_REWARD':
          choices.push(
            ActivityChoices.choice(
              type,
              { selectionInstanceId: valid.selectionInstanceId, lockedCardIds: [] },
              '',
              '',
              true
            )
          );
          break;
        case 'REROLL_SHOP':
          choices.push(
            ActivityChoices.choice(type, { shopInstanceId: valid.shopInstanceId }, '', '', true)
          );
          break;
        default:
          choices.push(ActivityChoices.choice(type, valid));
          break;
      }
    }
    return choices;
  }

  static onlyForcedAdvance(choices: JsonObject[]): JsonObject {
    const gameplay = choices.filter(
      (c) => !['ABANDON_RUN', 'APPLY_CARD_MODIFIER_ITEM'].includes(String(c.type ?? ''))
    );
    if (gameplay.length !== 1 || String(gameplay[0].type ?? '') !== 'ADVANCE_NODE') {
      return {};
    }
    return deepClone(gameplay[0]);
  }

  static singleEntry(choices: JsonObject[]): JsonObject {
    const gameplay = choices.filter(
      (c) => !['ABANDON_RUN', 'APPLY_CARD_MODIFIER_ITEM'].includes(String(c.type ?? ''))
    );
    if (gameplay.length !== 1) return {};
    if (
      ![
        'START_ENCOUNTER',
        'CREATE_CARD_SELECTION',
        'CREATE_SHOP',
        'CREATE_PREPARATION',
      ].includes(String(gameplay[0].type ?? ''))
    ) {
      return {};
    }
    return deepClone(gameplay[0]);
  }

  static onlyCompletion(choices: JsonObject[]): JsonObject {
    const gameplay = choices.filter(
      (c) => !['ABANDON_RUN', 'APPLY_CARD_MODIFIER_ITEM'].includes(String(c.type ?? ''))
    );
    if (gameplay.length !== 1 || String(gameplay[0].type ?? '') !== 'RESOLVE_NODE') {
      return {};
    }
    return deepClone(gameplay[0]);
  }

  private static choice(
    type: string,
    payload: JsonObject,
    subjectId = '',
    subjectType = '',
    secondary = false
  ): JsonObject {
    return {
      type,
      payload: deepClone(payload),
      subjectId,
      subjectType,
      secondary,
    };
  }
}
