import { apiClient } from './api';
import type { 
  StartCombatRequest,
  StartCombatResponse,
  CombatState,
  AvailableAction,
  ExecuteActionRequest
} from '@/types/combat';

const COMBAT_BASE = '/api/combat';

export const combatService = {
  // Start a new combat
  start: async (request: StartCombatRequest): Promise<StartCombatResponse> => {
    const response = await apiClient.post<StartCombatResponse>(`${COMBAT_BASE}/start`, request);
    return response.data;
  },

  // Get current combat state
  getState: async (combatId: string): Promise<CombatState> => {
    const response = await apiClient.get<CombatState>(`${COMBAT_BASE}/${combatId}/state`);
    return response.data;
  },

  // Get available actions for actor
  getAvailableActions: async (combatId: string, actorId: string): Promise<AvailableAction[]> => {
    const response = await apiClient.get<AvailableAction[]>(
      `${COMBAT_BASE}/${combatId}/actor/${actorId}/available-actions`
    );
    return response.data;
  },

  // Execute action
  executeAction: async (
    combatId: string, 
    actorId: string, 
    request: ExecuteActionRequest
  ): Promise<CombatState> => {
    const response = await apiClient.post<CombatState>(
      `${COMBAT_BASE}/${combatId}/actor/${actorId}/execute`,
      request
    );
    return response.data;
  },

  // End turn
  endTurn: async (combatId: string, actorId: string): Promise<CombatState> => {
    const response = await apiClient.post<CombatState>(
      `${COMBAT_BASE}/${combatId}/actor/${actorId}/end-turn`
    );
    return response.data;
  },

  // End combat
  end: async (combatId: string): Promise<void> => {
    await apiClient.post(`${COMBAT_BASE}/${combatId}/end`);
  },
};
