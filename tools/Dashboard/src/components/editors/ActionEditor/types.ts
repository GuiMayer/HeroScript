import type { Action } from '@/types/action';
import type { ActionFormData } from './actionSchema';

export interface ActionEditorProps {
  onClose?: () => void;
}

export interface ActionListProps {
  onEdit: (action: Action) => void;
  onDelete: (actionId: string) => void;
  onCreate: () => void;
}

export interface ActionDialogProps {
  open: boolean;
  onClose: () => void;
  action?: Action;
  mode: 'create' | 'edit';
}

export interface ActionFormProps {
  action?: Action;
  onSubmit: (data: ActionFormData) => void;
  onCancel: () => void;
  isLoading?: boolean;
}
