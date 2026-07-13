import { useState } from 'react';
import { EntityList } from './EntityList';
import { EntityDialog } from './EntityDialog';
import type { Entity } from '@/types/entity';
import type { EntityEditorProps } from './types';

export function EntityEditor({ onClose }: EntityEditorProps) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedEntity, setSelectedEntity] = useState<Entity | undefined>();
  const [mode, setMode] = useState<'create' | 'edit'>('create');

  const handleCreate = () => {
    setSelectedEntity(undefined);
    setMode('create');
    setDialogOpen(true);
  };

  const handleEdit = (entity: Entity) => {
    setSelectedEntity(entity);
    setMode('edit');
    setDialogOpen(true);
  };

  const handleDelete = (entityId: string) => {
    console.log('Deleted entity:', entityId);
  };

  const handleCloseDialog = () => {
    setDialogOpen(false);
    setSelectedEntity(undefined);
  };

  return (
    <div className="space-y-4">
      <EntityList
        onEdit={handleEdit}
        onDelete={handleDelete}
        onCreate={handleCreate}
      />
      <EntityDialog
        open={dialogOpen}
        onClose={handleCloseDialog}
        entity={selectedEntity}
        mode={mode}
      />
    </div>
  );
}
