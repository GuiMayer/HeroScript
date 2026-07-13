import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { statusService } from '@/services/statusService';
import type { StatusEffect, CreateStatusEffectDto, UpdateStatusEffectDto } from '@/types/status';

const QUERY_KEY = 'status-effects';

// Get all status effects
export const useStatusEffects = () => {
  return useQuery({
    queryKey: [QUERY_KEY],
    queryFn: statusService.getAll,
  });
};

// Get single status effect
export const useStatusEffect = (statusId: string) => {
  return useQuery({
    queryKey: [QUERY_KEY, statusId],
    queryFn: () => statusService.getById(statusId),
    enabled: !!statusId,
  });
};

// Create status effect
export const useCreateStatusEffect = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (status: CreateStatusEffectDto) => statusService.create(status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Update status effect
export const useUpdateStatusEffect = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ statusId, status }: { statusId: string; status: UpdateStatusEffectDto }) =>
      statusService.update(statusId, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Delete status effect
export const useDeleteStatusEffect = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (statusId: string) => statusService.delete(statusId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};
