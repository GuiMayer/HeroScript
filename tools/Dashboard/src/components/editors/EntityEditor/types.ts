import type { Entity } from '@/types/entity';
import type { EntityFormData } from './entitySchema';

export interface EntityEditorProps {
  onClose?: () => void;
}

export interface EntityListProps {
  onEdit: (entity: Entity) => void;
  onDelete: (entityId: string) => void;
  onCreate: () => void;
}

export interface EntityDialogProps {
  open: boolean;
  onClose: () => void;
  entity?: Entity;
  mode: 'create' | 'edit';
}

export interface EntityFormProps {
  entity?: Entity;
  onSubmit: (data: EntityFormData) => void;
  onCancel: () => void;
  isLoading?: boolean;
}
