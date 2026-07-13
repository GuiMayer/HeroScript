import { useState } from 'react';
import { Search, Plus, Edit, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { useEntities, useDeleteEntity } from '@/hooks/useEntities';
import type { EntityListProps } from './types';
import type { Entity } from '@/types/entity';
import { EntityType } from '@/types/entity';

export function EntityList({ onEdit, onDelete, onCreate }: EntityListProps) {
  const [searchTerm, setSearchTerm] = useState('');
  const [typeFilter, setTypeFilter] = useState<string>('all');
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [entityToDelete, setEntityToDelete] = useState<Entity | null>(null);

  const { data: entities, isLoading, error } = useEntities();
  const deleteMutation = useDeleteEntity();

  const handleDeleteClick = (entity: Entity) => {
    setEntityToDelete(entity);
    setDeleteDialogOpen(true);
  };

  const handleDeleteConfirm = () => {
    if (entityToDelete) {
      deleteMutation.mutate(entityToDelete.entityId, {
        onSuccess: () => {
          setDeleteDialogOpen(false);
          setEntityToDelete(null);
          onDelete(entityToDelete.entityId);
        },
      });
    }
  };

  const filteredEntities = entities?.filter((entity) => {
    const matchesSearch =
      entity.displayName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      entity.entityId.toLowerCase().includes(searchTerm.toLowerCase()) ||
      entity.description.toLowerCase().includes(searchTerm.toLowerCase());

    const matchesType =
      typeFilter === 'all' || entity.entityType === typeFilter;

    return matchesSearch && matchesType;
  });

  if (error) {
    return (
      <Card>
        <CardContent className="pt-6">
          <p className="text-red-500">Error loading entities: {error.message}</p>
        </CardContent>
      </Card>
    );
  }

  return (
    <>
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>Entities</CardTitle>
            <Button onClick={onCreate}>
              <Plus className="h-4 w-4 mr-2" />
              New Entity
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="flex gap-4 mb-4">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
              <Input
                placeholder="Search entities..."
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
                <SelectItem value={EntityType.PLAYER}>Player</SelectItem>
                <SelectItem value={EntityType.ENEMY}>Enemy</SelectItem>
                <SelectItem value={EntityType.BOSS}>Boss</SelectItem>
                <SelectItem value={EntityType.NPC}>NPC</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {isLoading ? (
            <div className="text-center py-8">Loading entities...</div>
          ) : filteredEntities && filteredEntities.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Entity ID</TableHead>
                  <TableHead>Display Name</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Level</TableHead>
                  <TableHead>Resources</TableHead>
                  <TableHead>Tags</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredEntities.map((entity) => (
                  <TableRow key={entity.entityId}>
                    <TableCell className="font-mono text-sm">
                      {entity.entityId}
                    </TableCell>
                    <TableCell className="font-medium">{entity.displayName}</TableCell>
                    <TableCell>
                      <Badge
                        variant={
                          entity.entityType === EntityType.PLAYER
                            ? 'default'
                            : entity.entityType === EntityType.BOSS
                            ? 'destructive'
                            : 'secondary'
                        }
                      >
                        {entity.entityType}
                      </Badge>
                    </TableCell>
                    <TableCell>Level {entity.level}</TableCell>
                    <TableCell>
                      {Object.keys(entity.resources).length > 0 ? (
                        <div className="text-sm">
                          {Object.entries(entity.resources)
                            .slice(0, 2)
                            .map(([key, res]) => (
                              <div key={key}>
                                {res.displayName}: {res.currentValue}/{res.maxValue}
                              </div>
                            ))}
                          {Object.keys(entity.resources).length > 2 && (
                            <div className="text-gray-500">
                              +{Object.keys(entity.resources).length - 2} more
                            </div>
                          )}
                        </div>
                      ) : (
                        <span className="text-gray-400">None</span>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        {entity.tags.slice(0, 2).map((tag) => (
                          <Badge key={tag} variant="outline" className="text-xs">
                            {tag}
                          </Badge>
                        ))}
                        {entity.tags.length > 2 && (
                          <Badge variant="outline" className="text-xs">
                            +{entity.tags.length - 2}
                          </Badge>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex gap-2 justify-end">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => onEdit(entity)}
                        >
                          <Edit className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleDeleteClick(entity)}
                        >
                          <Trash2 className="h-4 w-4 text-red-500" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <div className="text-center py-8 text-gray-500">
              No entities found. Create your first entity to get started.
            </div>
          )}
        </CardContent>
      </Card>

      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Entity</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete "{entityToDelete?.displayName}"? This action
              cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleDeleteConfirm}
              className="bg-red-500 hover:bg-red-600"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
