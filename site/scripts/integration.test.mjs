import {test} from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFile,readdir} from 'node:fs/promises';
import path from 'node:path';
import {siteRoot,digest} from './import-docs.mjs';
async function snapshot(root){const result={};for(const entry of (await readdir(root,{withFileTypes:true})).sort((a,b)=>a.name.localeCompare(b.name))){const file=path.join(root,entry.name);if(entry.isDirectory()){for(const [name,hash]of Object.entries(await snapshot(file)))result[entry.name+'/'+name]=hash;}else result[entry.name]=digest(await readFile(file));}return result;}
test('same source and site revisions generate byte-identical documentation twice',async()=>{
 const report=JSON.parse(await readFile(path.join(siteRoot,'generated/import-report.json'),'utf8'));
 const env={...process.env,DOCS_SOURCE_SHA:report.sourceSha};
 async function generate(){execFileSync(process.execPath,['scripts/import-docs.mjs'],{cwd:siteRoot,env});execFileSync(process.execPath,['scripts/generate-reference.mjs'],{cwd:siteRoot,env});return snapshot(path.join(siteRoot,'generated'));}
 const first=await generate(),second=await generate();assert.deepEqual(first,second);
 const search=JSON.parse(await readFile(path.join(siteRoot,'generated/static/search-index.json'),'utf8'));
 assert.equal(search.length,376);
 assert.ok(search.every(entry=>entry.title&&entry.url.startsWith('/')&&['api','developers','reference'].includes(entry.section)));
 assert.ok(!search.some(entry=>entry.url.includes('/plans/')));
 assert.ok(Object.keys(first).some(name=>name.startsWith('docs/api/')));
 assert.ok(Object.keys(first).some(name=>name.startsWith('docs/reference/schema/')));
 assert.ok(!Object.keys(first).some(name=>name.startsWith('docs/plans/')));
});
