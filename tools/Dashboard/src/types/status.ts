// Status Effect types
export enum StatusEffectType {
  BUFF = 'BUFF',
  DEBUFF = 'DEBUFF',
  DOT = 'DOT',
  HOT = 'HOT',
  STUN = 'STUN',
  SHIELD = 'SHIELD',
}

export interface EffectPerTick {
  effectId: string;
  type: string;
  value: number;
}

export interface StatusEffect {
  statusId: string;
  displayName: string;
  description: string;
  statusType: StatusEffectType;
  duration: number;
  stackable: boolean;
  maxStacks: number;
  effectsPerTick: EffectPerTick[];
  tags: string[];
}

export type CreateStatusEffectDto = StatusEffect;
export type UpdateStatusEffectDto = StatusEffect;
