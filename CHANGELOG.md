# Changelog

All notable changes to the HeroScript project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Phase 1: ActionManager API (2026-05-09)
- **ActionController** - Complete REST API for action management
  - `GET /api/action` - List all actions
  - `GET /api/action/{actionId}` - Get action details
  - `GET /api/action/by-type/{type}` - Filter actions by type
  - `GET /api/action/by-tag/{tag}` - Filter actions by tag
  - `POST /api/action/validate` - Validate action definitions
  - `POST /api/action/reload` - Reload action configurations

- **CombatController Extensions** - Integration with ActionManager
  - `GET /api/combat/{combatId}/cost-options` - Get real cost options for actions
  - `GET /api/combat/{combatId}/available-actions` - Get available actions in combat
  - `POST /api/combat/{combatId}/actions/{actionId}/can-afford` - Check action affordability

#### Phase 2: ResourceManager API (2026-05-09)
- **GameResourceController** - Complete REST API for resource management
  - `GET /api/game-resources` - List all resources
  - `GET /api/game-resources/{resourceId}` - Get resource details
  - `GET /api/game-resources/by-category/{category}` - Filter by category
  - `GET /api/game-resources/by-tag/{tag}` - Filter by tag
  - `POST /api/game-resources/validate` - Validate resource definitions
  - `POST /api/game-resources/reload` - Reload resource configurations
  - `POST /api/game-resources/create-pool` - Create resource pool instances
  - `POST /api/game-resources/validate-cost` - Validate resource costs

#### Phase 3: Documentation (2026-05-09)
- XML documentation generation enabled for all API endpoints
- Swagger/OpenAPI documentation available at root URL
- Comprehensive API documentation with examples in multiple languages (C#, JavaScript, Python)
- Usage examples for common scenarios (combat turns, character sheets, modding)

### Changed
- Test infrastructure updated to support new controller dependencies
- ConfigController and ResourceController tests fixed with proper mocking

### Technical Details
- 14 new REST endpoints implemented
- Full integration with existing ActionManager and ResourceManager systems
- Support for runtime configuration reloading (development only)
- Comprehensive error handling and validation
- Multi-language code examples for developer and modder consumption

---

## [Previous Versions]

_Version history before this changelog was established._
