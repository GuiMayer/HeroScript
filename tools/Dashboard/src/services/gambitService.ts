import { apiClient } from './api';
import type { 
  Gambit, 
  CreateGambitDto, 
  UpdateGambitDto,
  GambitDecisionRequest,
  GambitDecisionResponse 
} from '@/types/gambit';

const GAMBIT_BASE = '/api/gambit/definitions';
const GAMBIT_DECISION_BASE = '/api/gambit/decision';

export const gambitService = {
  // Get all gambits
  getAll: async (): Promise<Gambit[]> => {
    const response = await apiClient.get<Gambit[]>(GAMBIT_BASE);
    return response.data;
  },

  // Get gambit by ID
  getById: async (gambitId: string): Promise<Gambit> => {
    const response = await apiClient.get<Gambit>(`${GAMBIT_BASE}/${gambitId}`);
    return response.data;
  },

  // Create new gambit
  create: async (gambit: CreateGambitDto): Promise<Gambit> => {
    const response = await apiClient.post<Gambit>(GAMBIT_BASE, gambit);
    return response.data;
  },

  // Update existing gambit
  update: async (gambitId: string, gambit: UpdateGambitDto): Promise<Gambit> => {
    const response = await apiClient.put<Gambit>(`${GAMBIT_BASE}/${gambitId}`, gambit);
    return response.data;
  },

  // Delete gambit
  delete: async (gambitId: string): Promise<void> => {
    await apiClient.delete(`${GAMBIT_BASE}/${gambitId}`);
  },

  // Get decision for entity based on gambits
  getDecision: async (request: GambitDecisionRequest): Promise<GambitDecisionResponse> => {
    const response = await apiClient.post<GambitDecisionResponse>(GAMBIT_DECISION_BASE, request);
    return response.data;
  },
};
