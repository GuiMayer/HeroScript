{
    "BASIC_OPERATORS_EXAMPLE": {
            "description": "Exemplo com todos os operadores básicos",
            "params": {
            "BASE_VALUE": 100,
            "MULTIPLIER": 2.5,
            "DIVISOR": 4,
            "EXPONENT": 2,
            "MIN_CLAMP": 10,
            "MAX_CLAMP": 500
        },
        "operations": [
            { "op": "ADD", "operands": ["$input", "params.BASE_VALUE"] },
            { "op": "SUBTRACT", "operands": ["$result", 15] },
            { "op": "MULTIPLY", "operands": ["$result", "params.MULTIPLIER"] },
            { "op": "DIVIDE", "operands": ["$result", "params.DIVISOR"] },
            { "op": "POW", "operands": ["$result", "params.EXPONENT"] },
            { "op": "SQRT", "operands": ["$result"] },
            { "op": "LOG", "operands": ["$result"] },
            { "op": "CLAMP", "operands": ["$result", "params.MIN_CLAMP", "params.MAX_CLAMP"] }
        ]
    },

    "ADD_EXAMPLE": {
        "params": { "BONUS": 15 },
        "operations": [
            { "op": "ADD", "operands": ["$input", "params.BONUS"] }
        ]
    },

    "SUBTRACT_EXAMPLE": {
        "params": { "PENALTY": 10 },
        "operations": [
            { "op": "SUBTRACT", "operands": ["$input", "params.PENALTY"] }
        ]
    },

    "MULTIPLY_EXAMPLE": {
        "params": { "FACTOR": 1.5 },
        "operations": [
            { "op": "MULTIPLY", "operands": ["$input", "params.FACTOR"] }
        ]
    },

    "DIVIDE_EXAMPLE": {
        "params": { "DIVISOR": 2 },
        "operations": [
            { "op": "DIVIDE", "operands": ["$input", "params.DIVISOR"] }
        ]
    },

    "POW_EXAMPLE": {
        "params": { "EXPONENT": 3 },
        "operations": [
            { "op": "POW", "operands": ["$input", "params.EXPONENT"] }
        ]
    },

    "SQRT_EXAMPLE": {
        "operations": [
            { "op": "SQRT", "operands": ["$input"] }
        ]
    },

    "LOG_EXAMPLE": {
        "operations": [
            { "op": "LOG", "operands": ["$input"] }
        ]
    },

    "CLAMP_EXAMPLE": {
        "params": { "MIN": 5, "MAX": 100 },
        "operations": [
            { "op": "CLAMP", "operands": ["$input", "params.MIN", "params.MAX"] }
        ]
    },

    "LERP_EXAMPLE": {
        "description": "Linear interpolation for smooth transitions",
        "params": { "TARGET": 100, "T": 0.5 },
        "operations": [
            { "op": "SUBTRACT", "value": "params.TARGET" },
            { "op": "MULTIPLY", "value": "params.T" },
            { "op": "ADD", "value": "params.TARGET" }
        ]
    }
}