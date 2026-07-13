import { z } from 'zod';
import { ActionType } from '@/types/action';

// Resource cost schema
const resourceCostSchema = z.object({
  resourceId: z.string().min(1, 'Resource ID is required'),
  amount: z.number().min(0, 'Amount must be positive'),
});

// Effect definition schema
const effectDefinitionSchema = z.object({
  effectId: z.string().min(1, 'Effect ID is required'),
  type: z.string().min(1, 'Effect type is required'),
  target: z.string().min(1, 'Target is required'),
  value: z.number(),
  timing: z.string().min(1, 'Timing is required'),
});

// Action costs schema
const actionCostsSchema = z.object({
  resources: z.array(resourceCostSchema),
});

// Main action schema
export const actionSchema = z.object({
  actionId: z
    .string()
    .min(1, 'Action ID is required')
    .regex(/^[A-Z_0-9]+$/, 'Action ID must be uppercase with underscores'),
  displayName: z.string().min(1, 'Display name is required'),
  description: z.string().min(10, 'Description must be at least 10 characters'),
  actionType: z.nativeEnum(ActionType),
  costs: actionCostsSchema,
  effects: z.array(effectDefinitionSchema).min(1, 'At least one effect is required'),
  requiresTarget: z.boolean(),
  multiTarget: z.boolean(),
  cooldown: z.number().min(0, 'Cooldown must be non-negative'),
  tags: z.array(z.string()),
});

export type ActionFormData = z.infer<typeof actionSchema>;
