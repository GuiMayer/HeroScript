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
import { gambitSchema, type GambitFormData } from './gambitSchema';
import type { GambitFormProps } from './types';
import { ConditionType } from '@/types/gambit';

export function GambitForm({ gambit, onSubmit, onCancel, isLoading }: GambitFormProps) {
  const [tagInput, setTagInput] = useState('');
  const [paramKey, setParamKey] = useState('');
  const [paramValue, setParamValue] = useState('');

  const {
    register,
    control,
    handleSubmit,
    watch,
    setValue,
    formState: { errors },
  } = useForm<GambitFormData>({
    resolver: zodResolver(gambitSchema),
    defaultValues: (gambit as GambitFormData) || {
      gambitId: '',
      displayName: '',
      description: '',
      priority: 0,
      conditions: [],
      actionId: '',
      tags: [],
    },
  });

  const {
    fields: conditionFields,
    append: appendCondition,
    remove: removeCondition,
  } = useFieldArray({
    control,
    name: 'conditions',
  });

  const tags = watch('tags') || [];

  const handleAddCondition = () => {
    appendCondition({
      conditionType: ConditionType.ALWAYS,
      parameters: {},
    });
  };

  const handleAddParameter = (conditionIndex: number) => {
    if (paramKey.trim()) {
      const conditions = watch('conditions');
      const currentParams = conditions[conditionIndex]?.parameters || {};
      setValue(`conditions.${conditionIndex}.parameters`, {
        ...currentParams,
        [paramKey]: paramValue,
      });
      setParamKey('');
      setParamValue('');
    }
  };

  const handleRemoveParameter = (conditionIndex: number, key: string) => {
    const conditions = watch('conditions');
    const currentParams = conditions[conditionIndex]?.parameters || {};
    const newParams = { ...currentParams };
    delete newParams[key];
    setValue(`conditions.${conditionIndex}.parameters`, newParams);
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

  const handleFormSubmit = (data: GambitFormData) => {
    onSubmit(data);
  };

  return (
    <form onSubmit={handleSubmit(handleFormSubmit)} className="space-y-6">
      {/* Basic Fields */}
      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="gambitId">Gambit ID *</Label>
          <Input
            id="gambitId"
            {...register('gambitId')}
            placeholder="GAMBIT_NAME"
            disabled={!!gambit}
          />
          {errors.gambitId && (
            <p className="text-sm text-red-500">{errors.gambitId.message}</p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="displayName">Display Name *</Label>
          <Input
            id="displayName"
            {...register('displayName')}
            placeholder="Gambit Name"
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
          placeholder="Describe this gambit..."
          rows={3}
        />
        {errors.description && (
          <p className="text-sm text-red-500">{errors.description.message}</p>
        )}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="priority">Priority *</Label>
          <Input
            id="priority"
            type="number"
            {...register('priority', { valueAsNumber: true })}
            placeholder="0"
          />
          {errors.priority && (
            <p className="text-sm text-red-500">{errors.priority.message}</p>
          )}
          <p className="text-xs text-gray-500">Higher priority gambits are evaluated first</p>
        </div>

        <div className="space-y-2">
          <Label htmlFor="actionId">Action ID *</Label>
          <Input
            id="actionId"
            {...register('actionId')}
            placeholder="ACTION_ID"
          />
          {errors.actionId && (
            <p className="text-sm text-red-500">{errors.actionId.message}</p>
          )}
        </div>
      </div>

      {/* Conditions */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="text-lg">Conditions</CardTitle>
            <Button type="button" onClick={handleAddCondition} size="sm">
              <Plus className="h-4 w-4 mr-1" />
              Add Condition
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {conditionFields.length === 0 && (
            <p className="text-sm text-gray-500 text-center py-4">
              No conditions. Click "Add Condition" to add one.
            </p>
          )}
          {errors.conditions && (
            <p className="text-sm text-red-500">{errors.conditions.message}</p>
          )}

          {conditionFields.map((field, index) => {
            const conditionParams = watch(`conditions.${index}.parameters`) || {};
            return (
              <Card key={field.id}>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <h4 className="text-sm font-medium">Condition {index + 1}</h4>
                    <Button
                      type="button"
                      variant="destructive"
                      size="sm"
                      onClick={() => removeCondition(index)}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="space-y-2">
                    <Label htmlFor={`conditions.${index}.conditionType`}>Condition Type *</Label>
                    <Select
                      value={watch(`conditions.${index}.conditionType`)}
                      onValueChange={(value) =>
                        setValue(`conditions.${index}.conditionType`, value as ConditionType)
                      }
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="Select condition type" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={ConditionType.ALWAYS}>Always</SelectItem>
                        <SelectItem value={ConditionType.HP_BELOW}>HP Below</SelectItem>
                        <SelectItem value={ConditionType.HP_ABOVE}>HP Above</SelectItem>
                        <SelectItem value={ConditionType.ENEMY_COUNT}>Enemy Count</SelectItem>
                        <SelectItem value={ConditionType.HAS_STATUS}>Has Status</SelectItem>
                        <SelectItem value={ConditionType.RESOURCE_BELOW}>Resource Below</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <div className="space-y-2">
                    <Label>Parameters</Label>
                    <div className="flex gap-2">
                      <Input
                        value={paramKey}
                        onChange={(e) => setParamKey(e.target.value)}
                        placeholder="Key"
                        className="flex-1"
                      />
                      <Input
                        value={paramValue}
                        onChange={(e) => setParamValue(e.target.value)}
                        placeholder="Value"
                        className="flex-1"
                      />
                      <Button
                        type="button"
                        onClick={() => handleAddParameter(index)}
                        size="sm"
                      >
                        Add
                      </Button>
                    </div>

                    {Object.keys(conditionParams).length > 0 && (
                      <div className="mt-2 space-y-1">
                        {Object.entries(conditionParams).map(([key, value]) => (
                          <div
                            key={key}
                            className="flex items-center justify-between p-2 bg-gray-50 rounded"
                          >
                            <span className="text-sm">
                              <span className="font-medium">{key}:</span>{' '}
                              {String(value)}
                            </span>
                            <button
                              type="button"
                              onClick={() => handleRemoveParameter(index, key)}
                              className="text-red-500 hover:text-red-700"
                            >
                              ×
                            </button>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </CardContent>
              </Card>
            );
          })}
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
          {isLoading ? 'Saving...' : gambit ? 'Update Gambit' : 'Create Gambit'}
        </Button>
      </div>
    </form>
  );
}
