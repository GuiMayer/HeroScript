import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { useCreateAction, useUpdateAction } from '@/hooks/useActions';
import type { ActionDialogProps } from './types';
import { ActionForm } from './ActionForm';

export function ActionDialog({ open, onClose, action, mode }: ActionDialogProps) {
  const createMutation = useCreateAction();
  const updateMutation = useUpdateAction();

  const handleSubmit = (data: any) => {
    if (mode === 'create') {
      createMutation.mutate(data, {
        onSuccess: () => {
          onClose();
        },
      });
    } else if (action) {
      updateMutation.mutate(
        { actionId: action.actionId, action: data },
        {
          onSuccess: () => {
            onClose();
          },
        }
      );
    }
  };

  const isLoading = createMutation.isPending || updateMutation.isPending;

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-4xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {mode === 'create' ? 'Create New Action' : `Edit Action: ${action?.displayName}`}
          </DialogTitle>
        </DialogHeader>
        <ActionForm
          action={action}
          onSubmit={handleSubmit}
          onCancel={onClose}
          isLoading={isLoading}
        />
      </DialogContent>
    </Dialog>
  );
}
