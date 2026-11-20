// Smoke test for the LetsAgents bridge. Requires an open showcase editor.
// Usage: node tools/test-godot-mcp.mjs <absolute server.js> <screenshot.jpg>
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const [serverPath, screenshotPath, apiOrigin, runId, engineDll] = process.argv.slice(2);
assert(serverPath && screenshotPath, 'Specify server.js and screenshot destination');
assert(!apiOrigin || (runId && engineDll), 'Integration mode requires sandbox API, run ID and engine MCP DLL');
const child = spawn(process.execPath, [serverPath], { stdio: ['pipe', 'pipe', 'inherit'] });
const pending = new Map();
let nextId = 0;
let started = false;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

createInterface({ input: child.stdout }).on('line', line => {
  const message = JSON.parse(line);
  const request = pending.get(message.id);
  if (!request) return;
  pending.delete(message.id);
  clearTimeout(request.timer);
  if (message.error) request.reject(new Error(JSON.stringify(message.error)));
  else request.resolve(message.result);
});
function request(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++nextId;
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`Timeout: ${method}`));
    }, 30000);
    pending.set(id, { resolve, reject, timer });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
}
async function tool(name, args = {}) {
  const result = await request('tools/call', { name, arguments: args });
  assert(!result.isError, `${name}: ${JSON.stringify(result.content)}`);
  return result;
}
function flatten(node) {
  return node ? [node, ...(node.children ?? []).flatMap(flatten)] : [];
}

try {
  const init = await request('initialize', {
    protocolVersion: '2025-06-18', capabilities: {},
    clientInfo: { name: 'heroscript-smoke', version: '1' },
  });
  console.log('Server:', init.serverInfo);
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const { tools } = await request('tools/list');
  console.log('Tools:', tools.length);
  const before = (await tool('get_play_status')).structuredContent;
  assert(!before.is_playing, 'Game already running; refusing to replace it');
  const play = (await tool('play_scene', apiOrigin ? { mode: 'custom', scene_path: 'res://tests/mcp_sandbox_preview.tscn' } : { mode: 'main' })).structuredContent;
  assert.equal(play.status, 'playing');
  started = true;
  await sleep(7000);
  let pong;
  for (let attempt = 0; attempt < 4 && !pong; attempt++) {
    try {
      pong = (await tool('ping_game', { message: 'heroscript-smoke' })).structuredContent;
    } catch (error) {
      console.log('Waiting for debugger:', error.message);
      await sleep(1500);
    }
  }
  assert.equal(pong?.pong, 'pong: heroscript-smoke');
  console.log('Ping:', pong);
  let scene = (await tool('get_scene_tree')).structuredContent;
  if (apiOrigin) {
    for (let i = 0; i < 15 && !flatten(scene.tree).some(n => n.name === 'HeroScriptShowcase'); i++) {
      await sleep(1000);
      scene = (await tool('get_scene_tree')).structuredContent;
    }
  }
  assert(scene.available);
  let nodes = flatten(scene.tree);
  if (apiOrigin) {
    const preview = nodes.find(n => n.name === 'McpSandboxPreview');
    assert(preview, 'Expected guarded test scene');
    // Router construction can take several frames during import or concurrent builds.
    // Wait for the actual combat projection, not just the first instantiated control.
    let ready = false;
    for (let i = 0; i < 45; i++) {
      const evidence = (await tool('eval', { object_id: preview.id, code: 'qa_evidence' })).structuredContent.return_value;
      if (JSON.stringify(evidence).includes(runId)) { ready = true; break; }
      await sleep(1000);
    }
    assert(ready, 'QA combat projection did not become ready');
    scene = (await tool('get_scene_tree')).structuredContent;
    nodes = flatten(scene.tree);
  }
  const showcase = nodes.find(node => node.name === 'HeroScriptShowcase');
  assert(showcase, 'Expected actual HeroScript main scene');
  console.log('Scene nodes:', nodes.length);
  const inspection = await tool('inspect_node', { object_id: showcase.id });
  assert(inspection.structuredContent?.found, 'Expected live node properties');
  console.log('Inspected:', showcase.name, JSON.stringify(inspection.structuredContent).slice(0, 400));
  if (apiOrigin) {
    const preview = nodes.find(n => n.name === 'McpSandboxPreview');
    assert(preview, 'Expected guarded test scene');
    const probe = await tool('eval', { object_id: preview.id, code: 'qa_evidence' });
    const evidence = probe.structuredContent.return_value;
    const expectedRun = await (await fetch(`${apiOrigin}/api/v1/runs/${runId}`)).json();
    const expectedCombat = await (await fetch(`${apiOrigin}/api/v1/combats/${expectedRun.activeEncounterId}`)).json();
    const serialized = JSON.stringify(evidence);
    assert(serialized.includes(runId) && serialized.includes(expectedCombat.combatId) && serialized.includes(expectedCombat.stateHash), 'Godot view does not match expected engine state');
    // Independent MCP stdio handshake against the exact same authoritative combat.
    const engine = spawn('dotnet', [engineDll], { stdio: ['pipe','pipe','inherit'], env: {...process.env, HEROSCRIPT_MCP_API_URL: apiOrigin, Logging__LogLevel__Default: 'Warning'} });
    const lines = createInterface({ input: engine.stdout })[Symbol.asyncIterator]();
    const send = async (id, method, params) => {
      engine.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params}) + '\n');
      for (;;) {
        let timer;
        const next = await Promise.race([lines.next(), new Promise((_, reject) => { timer = setTimeout(() => reject(Error('Engine MCP response timeout')), 30000); })]).finally(() => clearTimeout(timer));
        assert(!next.done, 'Engine MCP exited');
        const msg = JSON.parse(next.value); if (msg.id === id) { assert(!msg.error, JSON.stringify(msg.error)); return msg.result; }
      }
    };
    try {
      await send(1, 'initialize', {protocolVersion:'2025-06-18',capabilities:{},clientInfo:{name:'godot-engine-crosscheck',version:'1'}});
      engine.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
      const result = await send(2, 'tools/call', {name:'inspect_combat',arguments:{combatId:expectedCombat.combatId}});
      assert(!result.isError, JSON.stringify(result.content));
      const response = result.structuredContent ?? JSON.parse(result.content[0].text);
      assert.equal(response.state.stateHash, expectedCombat.stateHash);
      assert.equal(response.state.contentRevision, expectedCombat.contentRevision);
      console.log('PASS both MCPs agree on combat, revision and exact state hash', expectedCombat.combatId, expectedCombat.stateHash);
    } finally { engine.kill(); }
    const after = await (await fetch(`${apiOrigin}/api/v1/runs/${runId}`)).json();
    assert.equal(after.stateHash, expectedRun.stateHash, 'Preview modified the sandbox source');
  }
  const errors = (await tool('get_runtime_errors', { clear: false, include_warnings: false })).structuredContent;
  console.log('Runtime errors:', JSON.stringify(errors));
  const screenshot = await tool('screenshot');
  const image = screenshot.content.find(item => item.type === 'image');
  assert(image, 'Expected screenshot image');
  const jpeg = Buffer.from(image.data, 'base64');
  assert(jpeg.length > 1000 && jpeg[0] === 0xff && jpeg[1] === 0xd8);
  await writeFile(screenshotPath, jpeg, { flag: 'wx' });
  console.log('Screenshot:', screenshotPath, jpeg.length, 'bytes');
  assert.equal(errors.count, 0, 'Runtime errors reported');
  console.log('PASS: HeroScript MCP smoke test');
} finally {
  try {
    if (started) {
      await tool('stop_scene');
      await sleep(1000);
      const after = (await tool('get_play_status')).structuredContent;
      assert(!after.is_playing, 'Test game did not stop');
      console.log('Test game stopped');
    }
  } finally {
    child.kill();
  }
}
