# HeroScript Web Calculator

A modern React-based calculator that tests the HeroScript MathExpression API with all 15 mathematical operations and 2 API modes.

## Features

- **15 Mathematical Operations**: ADD, SUBTRACT, MULTIPLY, DIVIDE, POW, SQRT, ABS, NEGATE, MIN, MAX, CLAMP, ROUND, FLOOR, CEIL, SET
- **2 API Modes**: 
  - **Mode 1 (Simple)**: Values with implicit accumulator
  - **Mode 2 (Explicit)**: Numeric operands or dynamic references with $current, $initial, params.X
- **Visual Timeline**: See your expression build step-by-step with a beautiful timeline visualization
- **Real-time API Testing**: Direct integration with HeroScript Math API
- **Parameter Management**: Define and use dynamic parameters in explicit mode
- **Undo/Redo**: Manage your expression steps with undo functionality
- **Multi-value Support**: Handle operations that require multiple values (MIN, MAX, CLAMP)
- **Execution Metrics**: View execution time and operation count
- **Dark Mode**: Automatic dark mode support based on system preferences
- **Responsive Design**: Works on desktop and mobile devices

## Prerequisites

- Node.js 18+ and npm
- HeroScript API running (see below)

## Quick Start

### 1. Start the HeroScript API

First, make sure the API is running:

```bash
# From the HeroScript root directory
cd src/API
dotnet run
```

The API will start at `http://localhost:5260` (HTTP) or `https://localhost:7260` (HTTPS).

### 2. Start the Web Calculator

```bash
# Navigate to the web calculator directory
cd tools/Calculator/web-calculator

# Install dependencies (first time only)
npm install

# Start the development server
npm run dev
```

The calculator will open at `http://localhost:5173` (or the next available port).

## Configuration

You can configure the API URL by creating a `.env` file:

```bash
cp .env.example .env
```

Then edit `.env` to set your API URL:

```
VITE_API_URL=http://localhost:5260
```

## API Modes

The calculator supports two definition modes for evaluating expressions:

### Mode 1: Simple (Values)
The simplest mode - values modify an implicit accumulator.
- **Use case**: Quick calculations, simple expressions
- **Limitation**: Values do not resolve `params.NAME`, `$current`, or `$initial`; use Operands for dynamic references
- **Example**: Initial: `10`, ADD `5`, MULTIPLY `2` → Result: `30`

### Mode 2: Explicit (Operands)
Operands are provided as numeric strings or symbolic references.
- **Use case**: When you need explicit control, parameters, or dynamic references
- **Example**: Initial: `10`, ADD `"5"`, `"3"` → Result: `18`
- **Symbols**:
  - `$current` - Current accumulated value
  - `$initial` - Initial value
  - `params.NAME` - User-defined parameters
- **Example**: LERP from 0 to 100 at 50%
  - Parameters: `START=0`, `TARGET=100`, `T=0.5`
  - Initial: `0`, SET `params.START`, ADD with operands `params.TARGET`, `params.T`

## Usage

### Basic Operations

1. **Select API Mode** (Mode 1 is default)
2. Enter an **Initial Value** (e.g., `10`)
3. **(Operands with symbols only)** Define parameters if needed
4. Add operations by entering values/operands and clicking operation buttons:
   - **Add (+)**: Add values to the current result
   - **Subtract (−)**: Subtract values from the current result
   - **Multiply (×)**: Multiply the current result by values
   - **Divide (÷)**: Divide the current result by values
5. Click **Calculate** to execute the expression
6. View the result and execution metrics

### Advanced Operations

- **Power (^)**: Raise to a power (e.g., `2` for square)
- **Square Root (√)**: Calculate square root (no input needed)
- **Absolute (|x|)**: Get absolute value (no input needed)
- **Negate (−x)**: Negate the value (no input needed)
- **Round (≈)**: Round to decimals (e.g., `2` for 2 decimal places)
- **Floor (⌊x⌋)**: Round down (no input needed)
- **Ceiling (⌈x⌉)**: Round up (no input needed)
- **Set (=)**: Set to a specific value (e.g., `100`)

### Multi-Value Operations

- **Minimum (min)**: Find minimum among values (e.g., `5, 10, 15`)
- **Maximum (max)**: Find maximum among values (e.g., `5, 10, 15`)
- **Clamp**: Clamp between min and max (e.g., `0, 100`)

### Tips

- Use **Undo Last Step** to remove the most recent operation
- Click **Continue from Result** to start a new expression using the previous result
- The timeline shows each step with its operation and values/operands
- Mode badges (M1, M2) indicate which mode was used for each step
- Execution time is displayed in milliseconds
- Switch modes to explore different ways of building expressions

## Example Expressions

### Mode 1: Calculate (10 + 5) × 2 − 3

1. Select **Mode 1: Simple**
2. Initial Value: `10`
3. Add: `5` → Result: 15
4. Multiply: `2` → Result: 30
5. Subtract: `3` → Result: 27

### Mode 1: Find minimum of multiple values

1. Select **Mode 1: Simple**
2. Initial Value: `100`
3. Min: `50, 75, 25, 90` → Result: 25

### Mode 1: Clamp a value between bounds

1. Select **Mode 1: Simple**
2. Initial Value: `150`
3. Clamp: `0, 100` → Result: 100

### Mode 2: Explicit calculation

1. Select **Mode 2: Explicit**
2. Initial Value: `0`
3. Add: `"10", "20"` → Result: 30
4. Multiply: `"2"` → Result: 60

### Mode 2: LERP from 0 to 100 at 50%

1. Select **Mode 2: Explicit**
2. Add Parameters:
   - `START = 0`
   - `TARGET = 100`
   - `T = 0.5`
3. Initial Value: `0`
4. SET: `params.START` → Result: 0
5. ADD: `params.TARGET, params.T` (simulating LERP logic)

### Mode 2: Dynamic damage calculation

1. Select **Mode 2: Explicit**
2. Add Parameters:
   - `BASE_DAMAGE = 50`
   - `MULTIPLIER = 1.5`
3. Initial Value: `0`
4. SET: `params.BASE_DAMAGE` → Result: 50
5. MULTIPLY: `params.MULTIPLIER` → Result: 75

## Technology Stack

- **React 18** with TypeScript
- **Vite** for fast development and building
- **Tailwind CSS** for styling
- **Axios** for API communication
- **Lucide React** for icons

## Project Structure

```
web-calculator/
├── src/
│   ├── components/
│   │   ├── ui/              # Reusable UI components
│   │   ├── OperationButtons.tsx
│   │   └── StepHistory.tsx
│   ├── services/
│   │   └── mathApi.ts       # API service layer
│   ├── types/
│   │   └── api.ts           # TypeScript types
│   ├── lib/
│   │   └── utils.ts         # Utility functions
│   ├── App.tsx              # Main application
│   ├── main.tsx             # Entry point
│   └── index.css            # Global styles
├── public/
├── index.html
├── package.json
├── tsconfig.json
├── vite.config.ts
└── tailwind.config.js
```

## Building for Production

```bash
npm run build
```

The production build will be in the `dist/` directory. You can serve it with any static file server.

## Troubleshooting

### API Connection Failed

- Make sure the HeroScript API is running at `http://localhost:5260`
- Check the API status indicator at the top of the page
- Verify CORS is enabled in the API (it should be by default)

### Port Already in Use

If port 5173 is already in use, Vite will automatically use the next available port. Check the terminal output for the actual URL.

### Type Errors

If you encounter TypeScript errors, try:

```bash
npm run build
```

This will show any type errors that need to be fixed.

## API Documentation

The HeroScript API provides a Swagger UI at `http://localhost:5260` when running in development mode.

### Endpoint

```
POST /api/math/expression/evaluate
```

### Request Body Examples

**Mode 1: Simple (Values)**
```json
{
  "initialValue": 10,
  "steps": [
    { "operation": "ADD", "values": [5] },
    { "operation": "MULTIPLY", "values": [2] }
  ]
}
```

**Mode 2: Explicit (Operands with literals)**
```json
{
  "initialValue": 10,
  "steps": [
    { "operation": "ADD", "operands": ["5", "3"] },
    { "operation": "MULTIPLY", "operands": ["2"] }
  ]
}
```

**Mode 2: Explicit (Operands with parameters)**
```json
{
  "initialValue": 0,
  "parameters": {
    "START": 0,
    "TARGET": 100,
    "T": 0.5
  },
  "steps": [
    { "operation": "SET", "operands": ["params.START"] },
    { "operation": "ADD", "operands": ["params.TARGET", "params.T"] }
  ]
}
```

### Response

```json
{
  "result": 30,
  "initialValue": 10,
  "steps": [
    { "operation": "ADD", "values": [5] },
    { "operation": "MULTIPLY", "values": [2] }
  ],
  "executionTimeMs": 1.234
}
```

## License

Part of the HeroScript project.
