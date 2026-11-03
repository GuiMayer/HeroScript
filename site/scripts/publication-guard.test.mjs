import {test} from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {mkdtempSync,readFileSync,rmSync} from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const script=new URL('./publication-guard.mjs',import.meta.url).href;
function check(overrides={}){
 const dir=mkdtempSync(path.join(os.tmpdir(),'docs-guard-')),output=path.join(dir,'output');
 try{
  const env={...process.env,GITHUB_REPOSITORY:'GuiMayer/HeroScript',GITHUB_OUTPUT:output,WORKFLOW_SHA:'site-current',SOURCE_SHA:'source-current',EVENT_NAME:'push',ROLLBACK:'false',...overrides};
  execFileSync(process.execPath,['--input-type=module','-e',`globalThis.fetch=async url=>({ok:true,json:async()=>({object:{sha:url.endsWith('/main')?'source-current':'site-current'}})});await import(${JSON.stringify(script)});`],{env});
  return readFileSync(output,'utf8').trim();
 }finally{rmSync(dir,{recursive:true,force:true})}
}
test('publication refuses stale artifacts and restricts rollback to explicit dispatch',()=>{
 assert.equal(check(),'current=true');
 assert.equal(check({SOURCE_SHA:'old'}),'current=false');
 assert.equal(check({WORKFLOW_SHA:'old'}),'current=false');
 assert.equal(check({SOURCE_SHA:'old',ROLLBACK:'true'}),'current=false');
 assert.equal(check({SOURCE_SHA:'old',ROLLBACK:'true',EVENT_NAME:'workflow_dispatch'}),'current=true');
 assert.equal(check({WORKFLOW_SHA:'old',ROLLBACK:'true',EVENT_NAME:'workflow_dispatch'}),'current=false');
});
