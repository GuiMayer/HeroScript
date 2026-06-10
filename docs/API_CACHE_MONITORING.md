# Cache Monitoring API Documentation

## Overview

The Cache Monitoring API provides real-time diagnostics and management for all cache services in the HeroScript system. It exposes metrics, health status, and invalidation controls through RESTful endpoints.

## Base URL

```
/api/diagnostics
```

## Authentication

Currently, no authentication is required. Future implementations should add authorization checks to prevent unauthorized cache manipulation.

## Response Formats

### Success Response
```json
{
  "timestamp": "2026-06-10T19:53:07Z",
  "data": {}
}
```

### Error Response
```json
{
  "error": "Error message describing what went wrong"
}
```

## Endpoints

### 1. Get All Cache Statistics

**Endpoint:** `GET /api/diagnostics/cache/stats`

**Description:** Retrieves comprehensive statistics for all registered cache services including hit rates, capacity, utilization, and performance metrics.

**Request:**
```bash
curl -X GET http://localhost:5000/api/diagnostics/cache/stats
```

**Response (200 OK):**
```json
{
  "timestamp": "2026-06-10T19:53:07Z",
  "services": [
    {
      "cacheName": "EntityDefinitions_default",
      "capacity": 1000,
      "count": 450,
      "hitRate": 0.85,
      "missRate": 0.15,
      "evictionCount": 12,
      "lastInvalidatedAt": "2026-06-10T19:50:00Z"
    },
    {
      "cacheName": "PhaseSequences",
      "capacity": 500,
      "count": 123,
      "hitRate": 0.92,
      "missRate": 0.08,
      "evictionCount": 5,
      "lastInvalidatedAt": "2026-06-10T19:51:30Z"
    }
  ],
  "totalServices": 2,
  "averageHitRate": 0.885
}
```

**Response Fields:**
- `timestamp`: ISO 8601 timestamp of when statistics were retrieved
- `services`: Array of cache service statistics
  - `cacheName`: Unique identifier for the cache service
  - `capacity`: Maximum number of entries the cache can hold
  - `count`: Current number of entries in the cache
  - `hitRate`: Percentage of cache hits (0.0 to 1.0)
  - `missRate`: Percentage of cache misses (0.0 to 1.0)
  - `evictionCount`: Total number of entries evicted due to capacity limits
  - `lastInvalidatedAt`: Timestamp of last cache invalidation
- `totalServices`: Number of registered cache services
- `averageHitRate`: Average hit rate across all services

**Status Codes:**
- `200 OK`: Successfully retrieved statistics
- `500 Internal Server Error`: Server error retrieving statistics

---

### 2. Get Cache Statistics by Name

**Endpoint:** `GET /api/diagnostics/cache/stats/{cacheName}`

**Description:** Retrieves statistics for a specific cache service by its name.

**Path Parameters:**
- `cacheName` (string, required): Name of the cache service (e.g., "EntityDefinitions_default", "PhaseSequences")

**Request:**
```bash
curl -X GET http://localhost:5000/api/diagnostics/cache/stats/EntityDefinitions_default
```

**Response (200 OK):**
```json
{
  "cacheName": "EntityDefinitions_default",
  "capacity": 1000,
  "count": 450,
  "hitRate": 0.85,
  "missRate": 0.15,
  "evictionCount": 12,
  "lastInvalidatedAt": "2026-06-10T19:50:00Z"
}
```

**Status Codes:**
- `200 OK`: Successfully retrieved cache statistics
- `400 Bad Request`: Cache name is empty or invalid
- `404 Not Found`: Cache service not found
- `500 Internal Server Error`: Server error retrieving statistics

---

### 3. Invalidate Entire Cache

**Endpoint:** `POST /api/diagnostics/cache/invalidate/{cacheName}`

**Description:** Clears all entries from a specific cache service. This operation cannot be undone and will force the cache to reload data on next access.

**Path Parameters:**
- `cacheName` (string, required): Name of the cache service to invalidate

**Request:**
```bash
curl -X POST http://localhost:5000/api/diagnostics/cache/invalidate/EntityDefinitions_default
```

**Response (200 OK):**
```json
{
  "message": "Successfully invalidated cache: EntityDefinitions_default",
  "timestamp": "2026-06-10T19:53:07Z"
}
```

**Status Codes:**
- `200 OK`: Successfully invalidated cache
- `400 Bad Request`: Cache name is empty or invalid
- `404 Not Found`: Cache service not found
- `500 Internal Server Error`: Server error invalidating cache

**Side Effects:**
- All entries in the specified cache are removed
- Next data access will trigger a reload from source
- Cache hit rate will temporarily decrease until cache is repopulated

---

### 4. Invalidate Specific Cache Key

**Endpoint:** `POST /api/diagnostics/cache/invalidate/{cacheName}/key`

**Description:** Removes a specific key from a cache service without affecting other cached entries.

**Path Parameters:**
- `cacheName` (string, required): Name of the cache service

**Query Parameters:**
- `key` (string, required): The specific cache key to invalidate

**Request:**
```bash
curl -X POST "http://localhost:5000/api/diagnostics/cache/invalidate/EntityDefinitions_default/key?key=hero_001"
```

**Response (200 OK):**
```json
{
  "message": "Successfully invalidated cache entry: EntityDefinitions_default:hero_001",
  "timestamp": "2026-06-10T19:53:07Z"
}
```

**Status Codes:**
- `200 OK`: Successfully invalidated cache entry
- `400 Bad Request`: Cache name or key is empty or invalid
- `404 Not Found`: Cache service or key not found
- `500 Internal Server Error`: Server error invalidating cache entry

---

### 5. Get Cache Health Status

**Endpoint:** `GET /api/diagnostics/health/cache`

**Description:** Retrieves the overall health status of all cache services with individual service health indicators.

**Request:**
```bash
curl -X GET http://localhost:5000/api/diagnostics/health/cache
```

**Response (200 OK):**
```json
{
  "timestamp": "2026-06-10T19:53:07Z",
  "status": "Healthy",
  "services": [
    {
      "serviceName": "EntityDefinitions_default",
      "isHealthy": true,
      "utilization": 0.45,
      "hitRate": 0.85
    },
    {
      "serviceName": "PhaseSequences",
      "isHealthy": true,
      "utilization": 0.246,
      "hitRate": 0.92
    }
  ]
}
```

**Response Fields:**
- `timestamp`: ISO 8601 timestamp of when health status was determined
- `status`: Overall health status
  - `Healthy`: Average hit rate ≥ 70%
  - `Fair`: Average hit rate ≥ 50% and < 70%
  - `Poor`: Average hit rate < 50%
  - `Unknown`: No cache services registered
- `services`: Array of individual service health information
  - `serviceName`: Cache service identifier
  - `isHealthy`: Boolean indicating if hit rate is ≥ 50%
  - `utilization`: Cache utilization percentage (count / capacity)
  - `hitRate`: Cache hit rate (0.0 to 1.0)

**Status Codes:**
- `200 OK`: Successfully retrieved health status
- `500 Internal Server Error`: Server error retrieving health status

---

## Usage Examples

### Monitor Cache Performance

```bash
# Check overall cache statistics
curl http://localhost:5000/api/diagnostics/cache/stats | jq '.'

# Check specific cache performance
curl http://localhost:5000/api/diagnostics/cache/stats/EntityDefinitions_default | jq '.hitRate'
```

### Troubleshooting Poor Cache Performance

```bash
# Check cache health
curl http://localhost:5000/api/diagnostics/health/cache | jq '.status'

# If health is Poor, invalidate and reload
curl -X POST http://localhost:5000/api/diagnostics/cache/invalidate/EntityDefinitions_default
```

### Refresh Specific Cached Item

```bash
# Invalidate a specific key to force reload on next access
curl -X POST "http://localhost:5000/api/diagnostics/cache/invalidate/EntityDefinitions_default/key?key=hero_001"
```

---

## Performance Considerations

### Cache Hit Rate Interpretation

- **Above 80%**: Excellent cache efficiency
- **60-80%**: Good cache efficiency
- **40-60%**: Acceptable but consider optimization
- **Below 40%**: Cache may be too small or access patterns are random

### Utilization Guidelines

- **Below 70%**: Cache is underutilized, capacity could be reduced
- **70-90%**: Optimal utilization
- **Above 90%**: Cache may be too small, consider increasing capacity

### Invalidation Impact

- Full cache invalidation clears all entries and may cause temporary performance degradation
- Use targeted key invalidation when possible to minimize impact
- Monitor hit rates after invalidation to track recovery

---

## Error Handling

### Common Error Scenarios

#### 404 Not Found: Cache Service

```json
{
  "error": "Cache service not found: InvalidCacheName"
}
```

**Resolution:** Use GET /api/diagnostics/cache/stats to see available cache services.

#### 400 Bad Request: Empty Parameter

```json
{
  "error": "Cache name cannot be empty"
}
```

**Resolution:** Ensure all required parameters are provided and non-empty.

#### 500 Internal Server Error

```json
{
  "error": "Failed to get cache statistics: Connection timeout"
}
```

**Resolution:** Check server logs and ensure cache services are running properly.

---

## Monitoring Best Practices

1. **Regular Health Checks**: Poll the health endpoint every 60 seconds to monitor cache performance
2. **Alert Thresholds**: Set alerts if hit rate drops below 60% or utilization exceeds 95%
3. **Scheduled Maintenance**: Consider invalidating caches during off-peak hours if they show poor hit rates
4. **Capacity Planning**: Monitor growth in cache utilization to plan capacity increases
5. **Key-Level Monitoring**: Track specific high-traffic keys for targeted optimization

---

## Future Enhancements

- Authentication and authorization
- Rate limiting for invalidation endpoints
- Webhook notifications for health status changes
- Historical metrics collection and trending
- Cache warming strategies
- Advanced filtering and querying
