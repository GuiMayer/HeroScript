import { apiClient } from './api';
import type { StatusEffect, CreateStatusEffectDto, UpdateStatusEffectDto } from '@/types/status';

const STATUS_BASE = '/api/status/definitions';

export const statusService = {
  // Get all status effects
  getAll: async (): Promise<StatusEffect[]> => {
    const response = await apiClient.get<StatusEffect[]>(STATUS_BASE);
    return response.data;
  },

  // Get status effect by ID
  getById: async (statusId: string): Promise<StatusEffect> => {
    const response = await apiClient.get<StatusEffect>(`${STATUS_BASE}/${statusId}`);
    return response.data;
  },

  // Create new status effect
  create: async (status: CreateStatusEffectDto): Promise<StatusEffect> => {
    const response = await apiClient.post<StatusEffect>(STATUS_BASE, status);
    return response.data;
  },

  // Update existing status effect
  update: async (statusId: string, status: UpdateStatusEffectDto): Promise<StatusEffect> => {
    const response = await apiClient.put<StatusEffect>(`${STATUS_BASE}/${statusId}`, status);
    return response.data;
  },

  // Delete status effect
  delete: async (statusId: string): Promise<void> => {
    await apiClient.delete(`${STATUS_BASE}/${statusId}`);
  },
};
