import { apiClient } from './api';
import type { Entity, CreateEntityDto, UpdateEntityDto, EntityType } from '@/types/entity';

const ENTITIES_BASE = '/api/entity/definitions';

export const entityService = {
  // Get all entities
  getAll: async (type?: EntityType): Promise<Entity[]> => {
    const params = type ? { type } : {};
    const response = await apiClient.get<Entity[]>(ENTITIES_BASE, { params });
    return response.data;
  },

  // Get entity by ID
  getById: async (entityId: string): Promise<Entity> => {
    const response = await apiClient.get<Entity>(`${ENTITIES_BASE}/${entityId}`);
    return response.data;
  },

  // Create new entity
  create: async (entity: CreateEntityDto): Promise<Entity> => {
    const response = await apiClient.post<Entity>(ENTITIES_BASE, entity);
    return response.data;
  },

  // Update existing entity
  update: async (entityId: string, entity: UpdateEntityDto): Promise<Entity> => {
    const response = await apiClient.put<Entity>(`${ENTITIES_BASE}/${entityId}`, entity);
    return response.data;
  },

  // Delete entity
  delete: async (entityId: string): Promise<void> => {
    await apiClient.delete(`${ENTITIES_BASE}/${entityId}`);
  },
};
