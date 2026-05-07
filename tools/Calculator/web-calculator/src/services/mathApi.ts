import axios, { AxiosError } from 'axios';
import type {
  MathExpressionRequest,
  MathExpressionResponse,
  ApiError,
} from '../types/api';

/**
 * API Service for HeroScript Math API
 */
class MathApiService {
  private baseUrl: string;

  constructor() {
    // Allow configuration via environment variable, fallback to localhost
    this.baseUrl =
      import.meta.env.VITE_API_URL || 'http://localhost:5260';
  }

  /**
   * Evaluate a math expression
   */
  async evaluate(
    request: MathExpressionRequest
  ): Promise<MathExpressionResponse> {
    try {
      const response = await axios.post<MathExpressionResponse>(
        `${this.baseUrl}/api/math/expression/evaluate`,
        request,
        {
          headers: {
            'Content-Type': 'application/json',
          },
          timeout: 10000, // 10 second timeout
        }
      );

      return response.data;
    } catch (error) {
      if (axios.isAxiosError(error)) {
        throw this.handleAxiosError(error);
      }
      throw new Error('An unexpected error occurred');
    }
  }

  /**
   * Handle Axios errors and convert to user-friendly messages
   */
  private handleAxiosError(error: AxiosError<ApiError>): Error {
    if (error.response) {
      // Server responded with error status
      const apiError = error.response.data;
      const message = apiError?.error || 'Server error occurred';
      const details = apiError?.details ? ` - ${apiError.details}` : '';
      return new Error(`${message}${details}`);
    } else if (error.request) {
      // Request made but no response received
      return new Error(
        'Cannot connect to API. Make sure the API server is running at ' +
          this.baseUrl
      );
    } else {
      // Error setting up the request
      return new Error('Failed to send request: ' + error.message);
    }
  }

  /**
   * Check if API is reachable
   */
  async healthCheck(): Promise<boolean> {
    try {
      const response = await axios.get(`${this.baseUrl}/api/health`, {
        timeout: 2000,
      });
      return response.data?.status === 'healthy';
    } catch {
      return false;
    }
  }

  /**
   * Get current API base URL
   */
  getBaseUrl(): string {
    return this.baseUrl;
  }
}

// Export singleton instance
export const mathApi = new MathApiService();
