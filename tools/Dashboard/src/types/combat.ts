// Combat types
export interface CombatParticipant {
  entityId: string;
  displayName: string;
  isPlayer: boolean;
  currentHP: number;
  maxHP: number;
  currentEnergy: number;
  maxEnergy: number;
  statusEffects: string[];
}

export interface CombatAction {
  actionId: string;
  displayName: string;
  actorId: string;
  targetIds: string[];
  timestamp: string;
}

export interface CombatState {
  combatId: string;
  isActive: boolean;
  currentTurn: number;
  participants: CombatParticipant[];
  actionLog: CombatAction[];
  winner?: string;
}

export interface StartCombatRequest {
  heroId: string;
  enemyIds: string[];
  config?: {
    startingEnergy?: number;
    energyPerTurn?: number;
  };
}

export interface StartCombatResponse {
  combatId: string;
  initialState: CombatState;
}

export interface AvailableAction {
  actionId: string;
  displayName: string;
  canUse: boolean;
  reason?: string;
}

export interface ExecuteActionRequest {
  actionId: string;
  targetIds?: string[];
}
