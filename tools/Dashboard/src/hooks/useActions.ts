import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { actionService } from '@/services/actionService';
import type { Action, CreateActionDto, UpdateActionDto } from '@/types/action';

const QUERY_KEY = 'actions';

// Get all actions
export const useActions = () => {
  return useQuery({
    queryKey: [QUERY_KEY],
    queryFn: actionService.getAll,
  });
};

// Get single action
export const useAction = (actionId: string) => {
  return useQuery({
    queryKey: [QUERY_KEY, actionId],
    queryFn: () => actionService.getById(actionId),
    enabled: !!actionId,
  });
};

// Create action
export const useCreateAction = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (action: CreateActionDto) => actionService.create(action),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Update action
export const useUpdateAction = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ actionId, action }: { actionId: string; action: UpdateActionDto }) =>
      actionService.update(actionId, action),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};

// Delete action
export const useDeleteAction = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (actionId: string) => actionService.delete(actionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [QUERY_KEY] });
    },
  });
};
