// Generic API types
export interface ApiResponse<T> {
  data: T;
  success: boolean;
  error?: string;
}

export interface ApiError {
  message: string;
  statusCode: number;
  details?: any;
}

export interface PaginatedResponse<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ValidationError {
  field: string;
  message: string;
}

// Math Expression types
export interface EvaluateExpressionRequest {
  expression: string;
  variables?: { [key: string]: number };
}

export interface EvaluateExpressionResponse {
  result: number;
  success: boolean;
  error?: string;
}

// Event types
export interface GameEvent {
  eventId: string;
  eventType: string;
  timestamp: string;
  sourceId?: string;
  targetId?: string;
  data: any;
}

export interface EventFilter {
  eventType?: string;
  sourceId?: string;
  targetId?: string;
  startDate?: string;
  endDate?: string;
}
