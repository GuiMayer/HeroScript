// Gambit types
export enum ConditionType {
  HP_BELOW = 'HP_BELOW',
  HP_ABOVE = 'HP_ABOVE',
  ENEMY_COUNT = 'ENEMY_COUNT',
  HAS_STATUS = 'HAS_STATUS',
  RESOURCE_BELOW = 'RESOURCE_BELOW',
  ALWAYS = 'ALWAYS',
}

export interface GambitCondition {
  conditionType: ConditionType;
  parameters: { [key: string]: any };
}

export interface Gambit {
  gambitId: string;
  displayName: string;
  description: string;
  priority: number;
  conditions: GambitCondition[];
  actionId: string;
  tags: string[];
}

export type CreateGambitDto = Gambit;
export type UpdateGambitDto = Gambit;

// Gambit decision API types
export interface GambitDecisionRequest {
  entityId: string;
  availableActions: string[];
  gameState?: any;
}

export interface GambitDecisionResponse {
  selectedActionId: string;
  gambitId: string;
  reasoning: string;
}
