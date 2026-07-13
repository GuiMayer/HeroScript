import { z } from 'zod';
import { ConditionType } from '@/types/gambit';

// Gambit condition schema
const gambitConditionSchema = z.object({
  conditionType: z.nativeEnum(ConditionType),
  parameters: z.record(z.string(), z.any()),
});

// Main gambit schema
export const gambitSchema = z.object({
  gambitId: z
    .string()
    .min(1, 'Gambit ID is required')
    .regex(/^[A-Z_0-9]+$/, 'Gambit ID must be uppercase with underscores'),
  displayName: z.string().min(1, 'Display name is required'),
  description: z.string().min(10, 'Description must be at least 10 characters'),
  priority: z.number().min(0, 'Priority must be non-negative'),
  conditions: z.array(gambitConditionSchema).min(1, 'At least one condition is required'),
  actionId: z.string().min(1, 'Action ID is required'),
  tags: z.array(z.string()),
});

export type GambitFormData = z.infer<typeof gambitSchema>;
