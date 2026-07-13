import { apiClient } from './api';
import type { Action, CreateActionDto, UpdateActionDto } from '@/types/action';

const ACTIONS_BASE = '/api/action/definitions';

export const actionService = {
  // Get all actions
  getAll: async (): Promise<Action[]> => {
    const response = await apiClient.get<Action[]>(ACTIONS_BASE);
    return response.data;
  },

  // Get action by ID
  getById: async (actionId: string): Promise<Action> => {
    const response = await apiClient.get<Action>(`${ACTIONS_BASE}/${actionId}`);
    return response.data;
  },

  // Create new action
  create: async (action: CreateActionDto): Promise<Action> => {
    const response = await apiClient.post<Action>(ACTIONS_BASE, action);
    return response.data;
  },

  // Update existing action
  update: async (actionId: string, action: UpdateActionDto): Promise<Action> => {
    const response = await apiClient.put<Action>(`${ACTIONS_BASE}/${actionId}`, action);
    return response.data;
  },

  // Delete action
  delete: async (actionId: string): Promise<void> => {
    await apiClient.delete(`${ACTIONS_BASE}/${actionId}`);
  },
};
