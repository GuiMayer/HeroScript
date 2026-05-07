# API Endpoints Documentation

## Overview

The HeroScript API provides three main controllers for managing configurations, mathematical operations, and resources.

**Base URL:** `http://localhost:5260/api`

---

## ConfigController

Manages configuration system operations including listing, validating, and loading configs.

### GET /api/config

List all available configurations.

**Response:**
```json
[
  {
    "name": "Alisyum",
    "version": "1.0.0",
    "author": "core",
    "description": "Distribuição oficial do Hero-Engine",
    "parent": null,
    "createdAt": "2026-05-07",
    "isActive": true,
    "path": "alisyum"
  }
]
```

### GET /api/config/current

Get the currently active configuration with inheritance chain.

**Response:**
```json
{
  "currentConfig": "alisyum",
  "inheritanceChain": ["alisyum"],
  "chainDescription": "alisyum",
  "chainMetadata": [...]
}
```

### POST /api/config/{name}/validate

Validate a configuration file.

**Parameters:**
- `name` (path): Configuration name

**Response:**
```json
{
  "configName": "alisyum",
  "isValid": true,
  "errors": [],
  "warnings": ["Config has no parent - is this a base config?"]
}
```

### POST /api/config/{name}/load

Load a configuration (requires `ALLOW_CONFIG_RELOAD=true`).

**Parameters:**
- `name` (path): Configuration name

**Response (403 if disabled):**
```json
{
  "error": "Config reload is disabled",
  "details": "Set ALLOW_CONFIG_RELOAD=true in environment or appsettings.json to enable this operation"
}
```

**Response (200 if enabled):**
```json
{
  "message": "Configuration 'alisyum' loaded successfully",
  "timestamp": "2026-05-07T17:30:00Z"
}
```

### GET /api/config/{name}/chain

Get the inheritance chain for a specific configuration.

**Parameters:**
- `name` (path): Configuration name

**Response:**
```json
{
  "configName": "test-orc-mod",
  "chain": ["test-orc-mod", "alisyum"],
  "chainDescription": "test-orc-mod -> alisyum"
}
```

### GET /api/config/{name}/diff

Compare a configuration with its parent.

**Parameters:**
- `name` (path): Configuration name

**Response:**
```json
{
  "configName": "test-orc-mod",
  "parentName": "alisyum",
  "differences": {
    "added": ["newKey1", "newKey2"],
    "modified": ["existingKey1"],
    "removed": []
  }
}
```

### GET /api/config/tree

Get the complete configuration hierarchy tree.

**Response:**
```json
{
  "root": "alisyum",
  "tree": {
    "alisyum": {
      "metadata": {...},
      "children": ["test-orc-mod"]
    }
  }
}
```

---

## OperationController

Provides metadata about mathematical operations available in the MathExpression system.

### GET /api/operation

List all available mathematical operations.

**Response:**
```json
[
  {
    "name": "ADD",
    "symbol": "+",
    "description": "Add values to the current result (accumulator mode)",
    "minValues": 1,
    "maxValues": -1,
    "category": "basic",
    "behavior": "accumulator",
    "isUnary": false
  },
  ...
]
```

**Total operations:** 18

**Categories:**
- `basic`: ADD, SUBTRACT, MULTIPLY, DIVIDE, NEGATE, ABS, ROUND, FLOOR, CEIL
- `advanced`: DIVIDE_INVERSE, POW, POW_BASE, SQRT, LOG
- `multi-value`: MIN, MAX, CLAMP
- `special`: SET

### GET /api/operation/{name}

Get metadata for a specific operation.

**Parameters:**
- `name` (path): Operation name (case-insensitive)

**Response:**
```json
{
  "name": "ADD",
  "symbol": "+",
  "description": "Add values to the current result (accumulator mode)",
  "minValues": 1,
  "maxValues": -1,
  "category": "basic",
  "behavior": "accumulator",
  "isUnary": false
}
```

### GET /api/operation/categories

Get operations grouped by category.

**Response:**
```json
{
  "basic": [...],
  "advanced": [...],
  "multi-value": [...],
  "special": [...]
}
```

---

## ResourceController

Manages resource loading and cache operations.

### GET /api/resource/origins

Get origin information for formula resources.

**Query Parameters:**
- `path` (optional): Resource path (defaults to "Pipelines/MathFormulas.json")

**Response:**
```json
{
  "resourcePath": "Pipelines/MathFormulas.json",
  "origins": {
    "DAMAGE": "alisyum",
    "ARMOR_REDUCTION": "test-orc-mod"
  }
}
```

### POST /api/resource/reload

Reload formula resources (requires `ALLOW_CONFIG_RELOAD=true`).

**Query Parameters:**
- `path` (optional): Resource path to reload

**Response (403 if disabled):**
```json
{
  "error": "Resource reload is disabled",
  "details": "Set ALLOW_CONFIG_RELOAD=true in environment or appsettings.json to enable this operation"
}
```

**Response (200 if enabled):**
```json
{
  "message": "All formula resources reloaded successfully",
  "timestamp": "2026-05-07T17:30:00Z"
}
```

### GET /api/resource/stats

Get cache statistics (not implemented).

**Response (501):**
```json
{
  "error": "Cache statistics not implemented",
  "details": "The Core does not currently expose cache statistics"
}
```

---

## Security Configuration

### ALLOW_CONFIG_RELOAD Flag

The following endpoints require the `ALLOW_CONFIG_RELOAD` flag to be enabled:

- `POST /api/config/{name}/load`
- `POST /api/resource/reload`

**Enable via environment variable:**
```bash
export ALLOW_CONFIG_RELOAD=true
```

**Enable via appsettings.json:**
```json
{
  "AllowConfigReload": true
}
```

**Default:** `false` (disabled for security)

---

## Error Responses

All endpoints follow a consistent error response format:

```json
{
  "error": "Error message",
  "details": "Additional details about the error"
}
```

**Common HTTP Status Codes:**
- `200 OK`: Success
- `400 Bad Request`: Invalid input
- `403 Forbidden`: Operation disabled (security flag)
- `404 Not Found`: Resource not found
- `500 Internal Server Error`: Server error
- `501 Not Implemented`: Feature not implemented

---

## Testing Examples

### Using PowerShell

```powershell
# List all configs
Invoke-RestMethod -Uri "http://localhost:5260/api/config" -Method Get

# Get current config
Invoke-RestMethod -Uri "http://localhost:5260/api/config/current" -Method Get

# Validate a config
Invoke-RestMethod -Uri "http://localhost:5260/api/config/alisyum/validate" -Method Post

# List all operations
Invoke-RestMethod -Uri "http://localhost:5260/api/operation" -Method Get

# Get operation by category
Invoke-RestMethod -Uri "http://localhost:5260/api/operation/categories" -Method Get

# Get resource origins
Invoke-RestMethod -Uri "http://localhost:5260/api/resource/origins" -Method Get
```

### Using curl

```bash
# List all configs
curl http://localhost:5260/api/config

# Get current config
curl http://localhost:5260/api/config/current

# Validate a config
curl -X POST http://localhost:5260/api/config/alisyum/validate

# List all operations
curl http://localhost:5260/api/operation

# Get resource origins
curl http://localhost:5260/api/resource/origins
```
