import { useForm, useFieldArray } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { entitySchema, type EntityFormData } from './entitySchema';
import type { EntityFormProps } from './types';
import { EntityType } from '@/types/entity';

export function EntityForm({ entity, onSubmit, onCancel, isLoading }: EntityFormProps) {
  const [tagInput, setTagInput] = useState('');

  const {
    register,
    control,
    handleSubmit,
    watch,
    setValue,
    formState: { errors },
  } = useForm<EntityFormData>({
    resolver: zodResolver(entitySchema),
    defaultValues: (entity as EntityFormData) || {
      entityId: '',
      displayName: '',
      description: '',
      entityType: EntityType.PLAYER,
      level: 1,
      resources: {},
      stats: {
        strength: 10,
        dexterity: 10,
        constitution: 10,
        intelligence: 10,
        wisdom: 10,
        charisma: 10,
      },
      inventory: {
        items: [],
        maxSlots: 20,
      },
      tags: [],
    },
  });

  const {
    fields: itemFields,
    append: appendItem,
    remove: removeItem,
  } = useFieldArray({
    control,
    name: 'inventory.items',
  });

  const watchedValues = watch();
  const tags = watch('tags') || [];
  const entityType = watch('entityType');

  const handleAddItem = () => {
    appendItem({
      itemId: '',
      quantity: 1,
    });
  };

  const handleAddTag = () => {
    if (tagInput.trim() && !tags.includes(tagInput.trim())) {
      setValue('tags', [...tags, tagInput.trim()]);
      setTagInput('');
    }
  };

  const handleRemoveTag = (tagToRemove: string) => {
    setValue(
      'tags',
      tags.filter((tag: string) => tag !== tagToRemove)
    );
  };

  const handleFormSubmit = (data: EntityFormData) => {
    onSubmit(data);
  };

  return (
    <form onSubmit={handleSubmit(handleFormSubmit)} className="space-y-6">
      {/* Basic Fields */}
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="entityId">Entity ID *</Label>
          <Input
            id="entityId"
            {...register('entityId')}
            placeholder="ENTITY_NAME"
            disabled={!!entity}
          />
          {errors.entityId && (
            <p className="text-sm text-red-500">{errors.entityId.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="displayName">Display Name *</Label>
          <Input
            id="displayName"
            {...register('displayName')}
            placeholder="Entity Name"
          />
          {errors.displayName && (
            <p className="text-sm text-red-500">{errors.displayName.message}</p>
          )}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="description">Description *</Label>
        <Textarea
          id="description"
          {...register('description')}
          placeholder="Describe this entity..."
          rows={3}
        />
        {errors.description && (
          <p className="text-sm text-red-500">{errors.description.message}</p>
        )}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="entityType">Entity Type *</Label>
          <Select
            value={watchedValues.entityType}
            onValueChange={(value) => setValue('entityType', value as EntityType)}
          >
            <SelectTrigger>
              <SelectValue placeholder="Select type" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={EntityType.PLAYER}>Player</SelectItem>
              <SelectItem value={EntityType.ENEMY}>Enemy</SelectItem>
              <SelectItem value={EntityType.BOSS}>Boss</SelectItem>
              <SelectItem value={EntityType.NPC}>NPC</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-2">
          <Label htmlFor="level">Level *</Label>
          <Input
            id="level"
            type="number"
            {...register('level', { valueAsNumber: true })}
            placeholder="1"
          />
          {errors.level && (
            <p className="text-sm text-red-500">{errors.level.message}</p>
          )}
        </div>
      </div>

      {/* Stats */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Stats</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-3 gap-4">
            <div className="space-y-2">
              <Label htmlFor="stats.strength">Strength</Label>
              <Input
                id="stats.strength"
                type="number"
                {...register('stats.strength', { valueAsNumber: true })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="stats.dexterity">Dexterity</Label>
              <Input
                id="stats.dexterity"
                type="number"
                {...register('stats.dexterity', { valueAsNumber: true })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="stats.constitution">Constitution</Label>
              <Input
                id="stats.constitution"
                type="number"
                {...register('stats.constitution', { valueAsNumber: true })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="stats.intelligence">Intelligence</Label>
              <Input
                id="stats.intelligence"
                type="number"
                {...register('stats.intelligence', { valueAsNumber: true })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="stats.wisdom">Wisdom</Label>
              <Input
                id="stats.wisdom"
                type="number"
                {...register('stats.wisdom', { valueAsNumber: true })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="stats.charisma">Charisma</Label>
              <Input
                id="stats.charisma"
                type="number"
                {...register('stats.charisma', { valueAsNumber: true })}
              />
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Inventory */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="text-lg">Inventory</CardTitle>
            <Button type="button" onClick={handleAddItem} size="sm">
              <Plus className="h-4 w-4 mr-1" />
              Add Item
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="inventory.maxSlots">Max Slots</Label>
            <Input
              id="inventory.maxSlots"
              type="number"
              {...register('inventory.maxSlots', { valueAsNumber: true })}
            />
          </div>

          {itemFields.length === 0 && (
            <p className="text-sm text-gray-500 text-center py-4">
              No items. Click "Add Item" to add one.
            </p>
          )}

          {itemFields.map((field, index) => (
            <Card key={field.id}>
              <CardContent className="pt-4">
                <div className="grid grid-cols-12 gap-4">
                  <div className="col-span-8 space-y-2">
                    <Label htmlFor={`inventory.items.${index}.itemId`}>Item ID *</Label>
                    <Input
                      {...register(`inventory.items.${index}.itemId`)}
                      placeholder="ITEM_ID"
                    />
                  </div>
                  <div className="col-span-2 space-y-2">
                    <Label htmlFor={`inventory.items.${index}.quantity`}>Quantity *</Label>
                    <Input
                      type="number"
                      {...register(`inventory.items.${index}.quantity`, {
                        valueAsNumber: true,
                      })}
                    />
                  </div>
                  <div className="col-span-2 flex items-end justify-end">
                    <Button
                      type="button"
                      variant="destructive"
                      size="sm"
                      onClick={() => removeItem(index)}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </CardContent>
      </Card>

      {/* Tags */}
      <div className="space-y-2">
        <Label>Tags</Label>
        <div className="flex gap-2">
          <Input
            value={tagInput}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setTagInput(e.target.value)}
            placeholder="Enter tag"
            onKeyDown={(e: React.KeyboardEvent<HTMLInputElement>) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                handleAddTag();
              }
            }}
          />
          <Button type="button" onClick={handleAddTag}>
            Add
          </Button>
        </div>
        <div className="flex flex-wrap gap-2 mt-2">
          {tags.map((tag: string) => (
            <span
              key={tag}
              className="inline-flex items-center gap-1 px-2 py-1 rounded-md bg-gray-100 text-sm"
            >
              {tag}
              <button
                type="button"
                onClick={() => handleRemoveTag(tag)}
                className="text-red-500 hover:text-red-700"
              >
                ×
              </button>
            </span>
          ))}
        </div>
      </div>

      {/* Form Actions */}
      <div className="flex gap-2 justify-end">
        <Button type="button" variant="outline" onClick={onCancel} disabled={isLoading}>
          Cancel
        </Button>
        <Button type="submit" disabled={isLoading}>
          {isLoading ? 'Saving...' : entity ? 'Update Entity' : 'Create Entity'}
        </Button>
      </div>
    </form>
  );
}
