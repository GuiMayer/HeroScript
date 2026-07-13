import { z } from 'zod';
import { StatusEffectType } from '@/types/status';

// Effect per tick schema
const effectPerTickSchema = z.object({
  effectId: z.string().min(1, 'Effect ID is required'),
  type: z.string().min(1, 'Type is required'),
  value: z.number(),
});

// Main status effect schema
export const statusEffectSchema = z.object({
  statusId: z
    .string()
    .min(1, 'Status ID is required')
    .regex(/^[A-Z_0-9]+$/, 'Status ID must be uppercase with underscores'),
  displayName: z.string().min(1, 'Display name is required'),
  description: z.string().min(10, 'Description must be at least 10 characters'),
  statusType: z.nativeEnum(StatusEffectType),
  duration: z.number().min(-1, 'Duration must be -1 (permanent) or positive'),
  stackable: z.boolean(),
  maxStacks: z.number().min(1, 'Max stacks must be at least 1'),
  effectsPerTick: z.array(effectPerTickSchema),
  tags: z.array(z.string()),
});

export type StatusEffectFormData = z.infer<typeof statusEffectSchema>;
