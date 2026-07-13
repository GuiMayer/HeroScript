import { apiClient } from './api';
import type { 
  EvaluateExpressionRequest,
  EvaluateExpressionResponse 
} from '@/types/api';

const MATH_BASE = '/api/math';

export const mathService = {
  // Evaluate a math expression
  evaluate: async (request: EvaluateExpressionRequest): Promise<EvaluateExpressionResponse> => {
    const response = await apiClient.post<EvaluateExpressionResponse>(
      `${MATH_BASE}/evaluate`,
      request
    );
    return response.data;
  },
};
