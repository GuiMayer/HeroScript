import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { entityService } from '@/services/entityService';
import type { Entity, CreateEntityDto, UpdateEntityDto, EntityType } from '@/types/entity';

const QUERY_KEY = 'entities';

// Get all entities
export const useEntities = (type?: EntityType) => {
  return useQuery({
    queryKey: [QUERY_KEY, type],
    queryFn: () => entityService.getAll(type),
  });
};

// Get single entity
export const useEntity = (entityId: string) => {
  return useQuery({
    queryKey: [QUERY_KEY, entityId],
    queryFn: () => entityService.getById(entityId),
    enabled: !!entityId,
  });
};

// Create entity
export const useCreateEntity = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (entity: CreateEntityDto) => entityService.create(entity),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Update entity
export const useUpdateEntity = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ entityId, entity }: { entityId: string; entity: UpdateEntityDto }) =>
      entityService.update(entityId, entity),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Delete entity
export const useDeleteEntity = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (entityId: string) => entityService.delete(entityId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};
