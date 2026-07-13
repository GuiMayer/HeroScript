// Entity types
export enum EntityType {
  PLAYER = 'PLAYER',
  ENEMY = 'ENEMY',
  BOSS = 'BOSS',
  NPC = 'NPC',
}

export interface ResourceDefinition {
  resourceId: string;
  displayName: string;
  currentValue: number;
  maxValue: number;
}

export interface Resources {
  [key: string]: ResourceDefinition;
}

export interface Stats {
  strength: number;
  dexterity: number;
  constitution: number;
  intelligence: number;
  wisdom: number;
  charisma: number;
}

export interface InventoryItem {
  itemId: string;
  quantity: number;
}

export interface Inventory {
  items: InventoryItem[];
  maxSlots: number;
}

export interface AIConfig {
  aggressionLevel: number;
  preferredTargeting: string;
  gambitIds: string[];
}

export interface Entity {
  entityId: string;
  displayName: string;
  description: string;
  entityType: EntityType;
  level: number;
  resources: Resources;
  stats: Stats;
  inventory?: Inventory;
  aiConfig?: AIConfig;
  tags: string[];
}

export type CreateEntityDto = Entity;
export type UpdateEntityDto = Entity;
