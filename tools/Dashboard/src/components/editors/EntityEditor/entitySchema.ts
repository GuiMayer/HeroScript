import { z } from 'zod';
import { EntityType } from '@/types/entity';

// Resource definition schema
const resourceDefinitionSchema = z.object({
  resourceId: z.string().min(1, 'Resource ID is required'),
  displayName: z.string().min(1, 'Display name is required'),
  currentValue: z.number().min(0, 'Current value must be non-negative'),
  maxValue: z.number().min(1, 'Max value must be positive'),
});

// Stats schema
const statsSchema = z.object({
  strength: z.number().min(0),
  dexterity: z.number().min(0),
  constitution: z.number().min(0),
  intelligence: z.number().min(0),
  wisdom: z.number().min(0),
  charisma: z.number().min(0),
});

// Inventory item schema
const inventoryItemSchema = z.object({
  itemId: z.string().min(1, 'Item ID is required'),
  quantity: z.number().min(1, 'Quantity must be at least 1'),
});

// Inventory schema
const inventorySchema = z.object({
  items: z.array(inventoryItemSchema),
  maxSlots: z.number().min(1, 'Max slots must be at least 1'),
});

// AI config schema
const aiConfigSchema = z.object({
  aggressionLevel: z.number().min(0).max(10),
  preferredTargeting: z.string(),
  gambitIds: z.array(z.string()),
});

// Main entity schema
export const entitySchema = z.object({
  entityId: z
    .string()
    .min(1, 'Entity ID is required')
    .regex(/^[A-Z_0-9]+$/, 'Entity ID must be uppercase with underscores'),
  displayName: z.string().min(1, 'Display name is required'),
  description: z.string().min(10, 'Description must be at least 10 characters'),
  entityType: z.nativeEnum(EntityType),
  level: z.number().min(1, 'Level must be at least 1'),
  resources: z.record(z.string(), resourceDefinitionSchema),
  stats: statsSchema,
  inventory: inventorySchema.optional(),
  aiConfig: aiConfigSchema.optional(),
  tags: z.array(z.string()),
});

export type EntityFormData = z.infer<typeof entitySchema>;
