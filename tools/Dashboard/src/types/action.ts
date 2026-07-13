// Action types
export enum ActionType {
  ATTACK = 'ATTACK',
  DEFENSE = 'DEFENSE',
  UTILITY = 'UTILITY',
  SPELL = 'SPELL',
}

export interface ResourceCost {
  resourceId: string;
  amount: number;
}

export interface ActionCosts {
  resources: ResourceCost[];
}

export interface EffectDefinition {
  effectId: string;
  type: string;
  target: string;
  value: number;
  timing: string;
}

export interface Action {
  actionId: string;
  displayName: string;
  description: string;
  actionType: ActionType;
  costs: ActionCosts;
  effects: EffectDefinition[];
  requiresTarget: boolean;
  multiTarget: boolean;
  cooldown: number;
  tags: string[];
}

export type CreateActionDto = Action;
export type UpdateActionDto = Action;
