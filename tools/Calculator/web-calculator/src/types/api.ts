/**
 * TypeScript types for HeroScript Math API
 */

/**
 * API operation modes
 */
export type ApiMode = 'implicit' | 'explicit-literal' | 'explicit-symbolic';

export interface MathStepDto {
  operation: string;
  values?: number[];
  operands?: string[];
}

export interface MathExpressionRequest {
  initialValue: number;
  steps: MathStepDto[];
  parameters?: Record<string, number>;
}

export interface MathExpressionResponse {
  result: number;
  initialValue: number;
  steps: MathStepDto[];
  executionTimeMs: number;
}

export interface ApiError {
  error: string;
  details?: string;
}

/**
 * Available math operations (matching API implementation)
 */
export enum MathOperation {
  ADD = 'ADD',
  SUBTRACT = 'SUBTRACT',
  MULTIPLY = 'MULTIPLY',
  DIVIDE = 'DIVIDE',
  POW = 'POW',
  SQRT = 'SQRT',
  ABS = 'ABS',
  NEGATE = 'NEGATE',
  MIN = 'MIN',
  MAX = 'MAX',
  CLAMP = 'CLAMP',
  ROUND = 'ROUND',
  FLOOR = 'FLOOR',
  CEIL = 'CEIL',
  SET = 'SET',
}

/**
 * Operation behavior types
 */
export type OperationBehavior = 
  | 'accumulator'      // Operates on accumulator with values (ADD, MULTIPLY)
  | 'unary'            // Operates on accumulator without values (NEGATE, SQRT, ABS)
  | 'unary-optional';  // Can operate with or without values (ROUND)

/**
 * Operation metadata for UI
 */
export interface OperationMetadata {
  operation: MathOperation;
  label: string;
  symbol: string;
  description: string;
  minValues: number;
  maxValues: number;
  category: 'basic' | 'advanced' | 'multi-value';
  behavior: OperationBehavior;
}

export const OPERATIONS: OperationMetadata[] = [
  // Basic Operations
  {
    operation: MathOperation.ADD,
    label: 'Add',
    symbol: '+',
    description: 'Add values',
    minValues: 1,
    maxValues: Infinity,
    category: 'basic',
    behavior: 'accumulator',
  },
  {
    operation: MathOperation.SUBTRACT,
    label: 'Subtract',
    symbol: '−',
    description: 'Subtract values',
    minValues: 1,
    maxValues: Infinity,
    category: 'basic',
    behavior: 'accumulator',
  },
  {
    operation: MathOperation.MULTIPLY,
    label: 'Multiply',
    symbol: '×',
    description: 'Multiply values',
    minValues: 1,
    maxValues: Infinity,
    category: 'basic',
    behavior: 'accumulator',
  },
  {
    operation: MathOperation.DIVIDE,
    label: 'Divide',
    symbol: '÷',
    description: 'Divide by values',
    minValues: 1,
    maxValues: Infinity,
    category: 'basic',
    behavior: 'accumulator',
  },
  
  // Advanced Operations
  {
    operation: MathOperation.POW,
    label: 'Power',
    symbol: '^',
    description: 'Raise to power',
    minValues: 1,
    maxValues: 1,
    category: 'advanced',
    behavior: 'accumulator',
  },
  {
    operation: MathOperation.SQRT,
    label: 'Square Root',
    symbol: '√',
    description: 'Square root',
    minValues: 0,
    maxValues: 0,
    category: 'advanced',
    behavior: 'unary',
  },
  {
    operation: MathOperation.ABS,
    label: 'Absolute',
    symbol: '|x|',
    description: 'Absolute value',
    minValues: 0,
    maxValues: 0,
    category: 'advanced',
    behavior: 'unary',
  },
  {
    operation: MathOperation.NEGATE,
    label: 'Negate',
    symbol: '−x',
    description: 'Negate value',
    minValues: 0,
    maxValues: 0,
    category: 'advanced',
    behavior: 'unary',
  },
  {
    operation: MathOperation.ROUND,
    label: 'Round',
    symbol: '≈',
    description: 'Round to decimals',
    minValues: 0,
    maxValues: 1,
    category: 'advanced',
    behavior: 'unary-optional',
  },
  {
    operation: MathOperation.FLOOR,
    label: 'Floor',
    symbol: '⌊x⌋',
    description: 'Round down',
    minValues: 0,
    maxValues: 0,
    category: 'advanced',
    behavior: 'unary',
  },
  {
    operation: MathOperation.CEIL,
    label: 'Ceiling',
    symbol: '⌈x⌉',
    description: 'Round up',
    minValues: 0,
    maxValues: 0,
    category: 'advanced',
    behavior: 'unary',
  },
  {
    operation: MathOperation.SET,
    label: 'Set',
    symbol: '=',
    description: 'Set to value',
    minValues: 1,
    maxValues: 1,
    category: 'advanced',
    behavior: 'accumulator',
  },
  
  // Multi-Value Operations
  {
    operation: MathOperation.MIN,
    label: 'Minimum',
    symbol: 'min',
    description: 'Find minimum value',
    minValues: 1,
    maxValues: Infinity,
    category: 'multi-value',
    behavior: 'accumulator',
  },
  {
    operation: MathOperation.MAX,
    label: 'Maximum',
    symbol: 'max',
    description: 'Find maximum value',
    minValues: 1,
    maxValues: Infinity,
    category: 'multi-value',
    behavior: 'accumulator',
  },
  {
    operation: MathOperation.CLAMP,
    label: 'Clamp',
    symbol: 'clamp',
    description: 'Clamp between min and max',
    minValues: 2,
    maxValues: 2,
    category: 'multi-value',
    behavior: 'accumulator',
  },
];
