import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { EntityForm } from './EntityForm';
import { useCreateEntity, useUpdateEntity } from '@/hooks/useEntities';
import type { EntityDialogProps } from './types';

export function EntityDialog({ open, onClose, entity, mode }: EntityDialogProps) {
  const createMutation = useCreateEntity();
  const updateMutation = useUpdateEntity();

  const handleSubmit = (data: any) => {
    if (mode === 'create') {
      createMutation.mutate(data, {
        onSuccess: () => {
          onClose();
        },
      });
    } else {
      updateMutation.mutate(
        { entityId: entity!.entityId, entity: data },
        {
          onSuccess: () => {
            onClose();
          },
        }
      );
    }
  };

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-4xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {mode === 'create' ? 'Create New Entity' : 'Edit Entity'}
          </DialogTitle>
        </DialogHeader>
        <EntityForm
          entity={entity}
          onSubmit={handleSubmit}
          onCancel={onClose}
          isLoading={createMutation.isPending || updateMutation.isPending}
        />
      </DialogContent>
    </Dialog>
  );
}
