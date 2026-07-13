import { useState } from 'react';
import { ActionList } from './ActionList';
import { ActionDialog } from './ActionDialog';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { useDeleteAction } from '@/hooks/useActions';
import type { Action } from '@/types/action';

export function ActionEditor() {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedAction, setSelectedAction] = useState<Action | undefined>(undefined);
  const [dialogMode, setDialogMode] = useState<'create' | 'edit'>('create');
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [actionToDelete, setActionToDelete] = useState<string | null>(null);

  const deleteMutation = useDeleteAction();

  const handleCreate = () => {
    setSelectedAction(undefined);
    setDialogMode('create');
    setDialogOpen(true);
  };

  const handleEdit = (action: Action) => {
    setSelectedAction(action);
    setDialogMode('edit');
    setDialogOpen(true);
  };

  const handleDelete = (actionId: string) => {
    setActionToDelete(actionId);
    setDeleteDialogOpen(true);
  };

  const confirmDelete = () => {
    if (actionToDelete) {
      deleteMutation.mutate(actionToDelete, {
        onSuccess: () => {
          setDeleteDialogOpen(false);
          setActionToDelete(null);
        },
      });
    }
  };

  const handleCloseDialog = () => {
    setDialogOpen(false);
    setSelectedAction(undefined);
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold">Actions</h1>
        <p className="text-muted-foreground mt-2">
          Manage combat actions, abilities, and spells
        </p>
      </div>

      <ActionList
        onEdit={handleEdit}
        onDelete={handleDelete}
        onCreate={handleCreate}
      />

      <ActionDialog
        open={dialogOpen}
        onClose={handleCloseDialog}
        action={selectedAction}
        mode={dialogMode}
      />

      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Action</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete this action? This action cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <div className="flex justify-end gap-3">
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Delete
            </AlertDialogAction>
          </div>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
