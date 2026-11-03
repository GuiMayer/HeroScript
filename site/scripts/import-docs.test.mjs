import {test} from 'node:test';
import assert from 'node:assert/strict';
import {safeRelative, checkManifest} from './import-docs.mjs';
const policy={allowedRoots:['docs/'],excludedRoots:['docs/plans/']};
const entry={source:'docs/api/contracts.md',docId:'api/contracts',slug:'/api/contracts/',section:'api'};
test('source paths reject traversal and local filesystem links',()=>{
 for(const value of ['../secret','/etc/passwd','C:\\save','docs/../secret','']) assert.throws(()=>safeRelative(value));
 assert.equal(safeRelative(entry.source),entry.source);
});
test('manifest accepts a canonical identity and rejects copies or excluded sources',()=>{
 assert.doesNotThrow(()=>checkManifest({entries:[entry]},policy));
 assert.throws(()=>checkManifest({entries:[entry,entry]},policy));
 assert.throws(()=>checkManifest({entries:[{...entry,source:'docs/plans/private.md'}]},policy));
 assert.throws(()=>checkManifest({entries:[{...entry,docId:'copy'}]},policy));
});
