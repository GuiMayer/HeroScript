import { deepClone, JsonObject } from "../contracts/types";

export interface Point2D {
  x: number;
  y: number;
}

export interface Rect2D {
  x: number;
  y: number;
  width: number;
  height: number;
  endX: number;
  endY: number;
  center: Point2D;
}

export interface MapEdge {
  from: string;
  to: string;
  return: boolean;
  path: Point2D[];
}

export interface MapLayoutResult {
  rects: Record<string, Rect2D>;
  markers: Record<string, Point2D>;
  edges: MapEdge[];
  bounds: Point2D;
  cyclic: boolean;
  diagnostics: string[];
}

export class MapLayoutProfile {
  nodeSize: Point2D = { x: 112, y: 104 };
  hitSize: Point2D = { x: 72, y: 64 };
  symbolSize = 42.0;
  compactAuxiliary = true;
  auxiliaryKinds = ["CardSelection", "RelicSelection", "CardUpgrade", "InscriptionSelection"];
  auxiliarySymbolSize = 30.0;
  padding: Point2D = { x: 24, y: 20 };
  laneGap = 16.0;
  layerGap = 32.0;
  maximumPanelWidth = 620.0;
  panelWidthRatio = 0.4;
  cameraTransitionSeconds = 0.28;

  presentationKey(): string {
    return JSON.stringify([
      this.nodeSize,
      this.hitSize,
      this.symbolSize,
      this.padding,
      this.laneGap,
      this.layerGap,
    ]);
  }
}

/**
 * Pure, stable geometry. Never derives travel permissions from edges.
 */
export class MapLayout {
  private _key = "";
  private _result: MapLayoutResult | null = null;
  calculationCount = 0;

  calculate(nodes: JsonObject[], profile: MapLayoutProfile = new MapLayoutProfile()): MapLayoutResult {
    const graph: Record<string, string[]> = {};
    const diagnostics: string[] = [];

    for (const node of nodes) {
      const id = String(node.id ?? "");
      if (!id) {
        diagnostics.push("Visible map node has no ID.");
        continue;
      }
      if (id in graph) {
        diagnostics.push(`Duplicate visible map ID: ${id}`);
        continue;
      }
      const nextList = Array.isArray(node.next) ? node.next.map(String) : [];
      nextList.sort();
      graph[id] = nextList;
    }

    const ids = Object.keys(graph).sort();
    const topology = ids.map((id) => [id, graph[id]]);
    diagnostics.sort();
    const key = JSON.stringify([topology, profile.presentationKey(), diagnostics]);
    if (key === this._key && this._result) {
      return deepClone(this._result);
    }
    this.calculationCount += 1;

    const components = this._strongComponents(graph, ids);
    const owner: Record<string, string> = {};
    const condensed: Record<string, string[]> = {};
    const incoming: Record<string, number> = {};
    const componentLayers: Record<string, number> = {};
    let cyclic = false;

    for (const component of components) {
      const keyId = component[0];
      condensed[keyId] = [];
      incoming[keyId] = 0;
      componentLayers[keyId] = 0;
      for (const id of component) {
        owner[id] = keyId;
      }
      if (component.length > 1) {
        cyclic = true;
      }
    }

    for (const id of ids) {
      for (const target of graph[id]) {
        if (!(target in owner)) continue;
        if (id === target) cyclic = true;
        const fromOwner = owner[id];
        const toOwner = owner[target];
        if (fromOwner !== toOwner && !condensed[fromOwner].includes(toOwner)) {
          condensed[fromOwner].push(toOwner);
          incoming[toOwner] += 1;
        }
      }
    }

    const queue = Object.keys(condensed).filter((id) => incoming[id] === 0);
    while (queue.length > 0) {
      queue.sort();
      const id = queue.shift()!;
      for (const target of condensed[id]) {
        componentLayers[target] = Math.max(
          componentLayers[target],
          componentLayers[id] + 1
        );
        incoming[target] -= 1;
        if (incoming[target] === 0) {
          queue.push(target);
        }
      }
    }

    const layers: Record<string, number> = {};
    for (const id of ids) {
      layers[id] = componentLayers[owner[id]];
    }

    const rows: Record<number, string[]> = {};
    for (const id of ids) {
      const layer = layers[id];
      if (!rows[layer]) {
        rows[layer] = [];
      }
      rows[layer].push(id);
    }

    const rects: Record<string, Rect2D> = {};
    const markers: Record<string, Point2D> = {};
    const bounds: Point2D = { x: 0, y: 0 };

    const rowIds = Object.keys(rows)
      .map(Number)
      .sort((a, b) => a - b);

    for (let sweep = 0; sweep < 4; sweep++) {
      const positions: Record<string, number> = {};
      for (const layer of rowIds) {
        const row = rows[layer];
        for (let lane = 0; lane < row.length; lane++) {
          positions[row[lane]] = lane - (row.length - 1) * 0.5;
        }
      }
      for (const layer of rowIds) {
        const scores: Record<string, number> = {};
        for (const id of rows[layer]) {
          const adjacent: number[] = [];
          for (const other of ids) {
            if (
              (sweep % 2 === 0 && graph[other].includes(id) && layers[other] < layer) ||
              (sweep % 2 === 1 && graph[id].includes(other) && layers[other] > layer)
            ) {
              adjacent.push(positions[other]);
            }
          }
          const total = adjacent.reduce((acc, v) => acc + v, 0);
          scores[id] = adjacent.length > 0 ? total / adjacent.length : positions[id];
        }
        rows[layer].sort((a, b) =>
          Math.abs(scores[a] - scores[b]) < 1e-6
            ? a.localeCompare(b)
            : scores[a] - scores[b]
        );
      }
    }

    let widest = 1;
    for (const layer of rowIds) {
      widest = Math.max(widest, rows[layer].length);
    }

    for (const layer of rowIds) {
      const row = rows[layer];
      for (let lane = 0; lane < row.length; lane++) {
        const id = row[lane];
        const x =
          profile.padding.x +
          (lane + (widest - row.length) * 0.5) * (profile.nodeSize.x + profile.laneGap);
        const y =
          profile.padding.y + layer * (profile.nodeSize.y + profile.layerGap);
        const rect: Rect2D = {
          x,
          y,
          width: profile.nodeSize.x,
          height: profile.nodeSize.y,
          endX: x + profile.nodeSize.x,
          endY: y + profile.nodeSize.y,
          center: {
            x: x + profile.nodeSize.x * 0.5,
            y: y + profile.nodeSize.y * 0.5,
          },
        };
        rects[id] = rect;
        markers[id] = { ...rect.center };
        bounds.x = Math.max(bounds.x, rect.endX + profile.padding.x);
        bounds.y = Math.max(bounds.y, rect.endY + profile.padding.y);
      }
    }

    const edges: MapEdge[] = [];
    for (const id of ids) {
      for (const target of graph[id]) {
        if (!(target in markers)) continue;
        const origin: Point2D = {
          x: rects[id].x + profile.nodeSize.x * 0.5,
          y: rects[id].y + profile.nodeSize.y,
        };
        const destination: Point2D = {
          x: rects[target].x + profile.nodeSize.x * 0.5,
          y: rects[target].y,
        };
        const returning = layers[target] <= layers[id];
        let path: Point2D[];
        if (id === target) {
          const right = rects[id].endX + 8;
          path = [
            origin,
            { x: origin.x, y: origin.y + 12 },
            { x: right, y: origin.y + 12 },
            { x: right, y: destination.y - 12 },
            { x: destination.x, y: destination.y - 12 },
            destination,
          ];
        } else if (returning) {
          const outside = profile.padding.x * 0.5;
          path = [
            origin,
            { x: origin.x, y: origin.y + 12 },
            { x: outside, y: origin.y + 12 },
            { x: outside, y: destination.y - 12 },
            { x: destination.x, y: destination.y - 12 },
            destination,
          ];
        } else {
          path = [
            origin,
            { x: origin.x, y: origin.y + profile.layerGap * 0.5 },
            { x: destination.x, y: destination.y - profile.layerGap * 0.5 },
            destination,
          ];
        }
        edges.push({ from: id, to: target, return: returning, path });
      }
    }

    this._key = key;
    this._result = {
      rects,
      markers,
      edges,
      bounds,
      cyclic,
      diagnostics,
    };
    return deepClone(this._result);
  }

  private _strongComponents(
    graph: Record<string, string[]>,
    ids: string[]
  ): string[][] {
    const seen = new Set<string>();
    const order: string[] = [];
    const reverse: Record<string, string[]> = {};
    for (const id of ids) {
      reverse[id] = [];
    }
    for (const id of ids) {
      for (const target of graph[id]) {
        if (target in reverse) {
          reverse[target].push(id);
        }
      }
    }

    for (const rootId of ids) {
      if (seen.has(rootId)) continue;
      seen.add(rootId);
      const stack: [string, number][] = [[rootId, 0]];
      while (stack.length > 0) {
        const frame = stack[stack.length - 1];
        const edges = graph[frame[0]];
        if (frame[1] >= edges.length) {
          order.push(frame[0]);
          stack.pop();
          continue;
        }
        const target = edges[frame[1]];
        frame[1] += 1;
        if (target in graph && !seen.has(target)) {
          seen.add(target);
          stack.push([target, 0]);
        }
      }
    }

    seen.clear();
    const result: string[][] = [];
    order.reverse();
    for (const rootId of order) {
      if (seen.has(rootId)) continue;
      const component: string[] = [];
      const stack: string[] = [rootId];
      seen.add(rootId);
      while (stack.length > 0) {
        const id = stack.pop()!;
        component.push(id);
        for (const target of reverse[id]) {
          if (!seen.has(target)) {
            seen.add(target);
            stack.push(target);
          }
        }
      }
      component.sort();
      result.push(component);
    }
    result.sort((a, b) => a[0].localeCompare(b[0]));
    return result;
  }
}
