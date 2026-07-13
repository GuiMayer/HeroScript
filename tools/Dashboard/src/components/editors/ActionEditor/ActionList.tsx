import { useState } from 'react';
import { Pencil, Trash2, Plus, Search } from 'lucide-react';
import { useActions } from '@/hooks/useActions';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import type { Action } from '@/types/action';
import type { ActionListProps } from './types';

export function ActionList({ onEdit, onDelete, onCreate }: ActionListProps) {
  const [searchTerm, setSearchTerm] = useState('');
  const [typeFilter, setTypeFilter] = useState<string>('all');

  // Fetch actions from API using React Query hook
  const { data: actions = [], isLoading } = useActions();

  const filteredActions = actions.filter((action) => {
    const matchesSearch =
      action.actionId.toLowerCase().includes(searchTerm.toLowerCase()) ||
      action.displayName.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesType = typeFilter === 'all' || action.actionType === typeFilter;
    return matchesSearch && matchesType;
  });

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="h-10 bg-muted animate-pulse rounded" />
        <div className="h-64 bg-muted animate-pulse rounded" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-4 flex-1">
          <div className="relative flex-1 max-w-sm">
            <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Search actions..."
              value={searchTerm}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSearchTerm(e.target.value)}
              className="pl-10"
            />
          </div>
          <Select value={typeFilter} onValueChange={setTypeFilter}>
            <SelectTrigger className="w-[180px]">
              <SelectValue placeholder="Filter by type" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Types</SelectItem>
              <SelectItem value="ATTACK">Attack</SelectItem>
              <SelectItem value="DEFENSE">Defense</SelectItem>
              <SelectItem value="UTILITY">Utility</SelectItem>
              <SelectItem value="SPELL">Spell</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <Button onClick={onCreate}>
          <Plus className="h-4 w-4 mr-2" />
          New Action
        </Button>
      </div>

      {filteredActions.length === 0 ? (
        <div className="text-center py-12 border border-dashed rounded-lg">
          <p className="text-muted-foreground mb-4">
            {searchTerm || typeFilter !== 'all'
              ? 'No actions found matching your filters'
              : 'No actions yet. Create your first action to get started.'}
          </p>
          {!searchTerm && typeFilter === 'all' && (
            <Button onClick={onCreate}>
              <Plus className="h-4 w-4 mr-2" />
              Create First Action
            </Button>
          )}
        </div>
      ) : (
        <div className="border rounded-lg">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>ID</TableHead>
                <TableHead>Name</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>Tags</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredActions.map((action) => (
                <TableRow key={action.actionId}>
                  <TableCell className="font-mono text-sm">
                    {action.actionId}
                  </TableCell>
                  <TableCell className="font-medium">
                    {action.displayName}
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline">{action.actionType}</Badge>
                  </TableCell>
                  <TableCell>
                    <div className="flex gap-1 flex-wrap">
                      {action.tags.slice(0, 3).map((tag) => (
                        <Badge key={tag} variant="secondary" className="text-xs">
                          {tag}
                        </Badge>
                      ))}
                      {action.tags.length > 3 && (
                        <Badge variant="secondary" className="text-xs">
                          +{action.tags.length - 3}
                        </Badge>
                      )}
                    </div>
                  </TableCell>
                  <TableCell className="text-right">
                    <div className="flex justify-end gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => onEdit(action)}
                      >
                        <Pencil className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => onDelete(action.actionId)}
                      >
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
