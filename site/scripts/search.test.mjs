import {test} from 'node:test';
import assert from 'node:assert/strict';
import {searchEntries} from '../src/search.mjs';
test('search handles accents, audiences and empty input',()=>{
 const entries=[{title:'Configuração sessões',text:'REST',section:'api',url:'/api/'},{title:'Configuração',text:'JSON',section:'developers',url:'/developers/'}];
 assert.equal(searchEntries(entries,'configuracao sessoes').length,1);
 assert.equal(searchEntries(entries,'configuracao','developers')[0].url,'/developers/');
 assert.equal(searchEntries(entries,'   ').length,0);
 assert.equal(searchEntries(entries,'REST','developers').length,0);
});
