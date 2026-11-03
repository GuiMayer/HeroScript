import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {siteRoot,git,digest} from './import-docs.mjs';
const root=path.join(siteRoot,'generated'), docs=path.join(root,'docs');
const report=JSON.parse(await readFile(path.join(root,'import-report.json'),'utf8'));
const spec=JSON.parse(await readFile(path.join(root,'static/contracts/heroscript-v1.json'),'utf8'));
const events=JSON.parse(await readFile(path.join(root,'static/contracts/heroscript-events-v1.json'),'utf8'));
const slug=(value)=>value.toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'')+'-'+digest(value).slice(0,8);
const table=value=>String(value??'—').replaceAll('|','\\|').replaceAll('\n',' ');
async function page(id,title,body){const file=path.join(docs,id+'.md');await mkdir(path.dirname(file),{recursive:true});await writeFile(file,`---\ntitle: ${JSON.stringify(title)}\nslug: /${id}/\n---\n# ${title}\n\n${body}\n`);}
const schemas=spec.components?.schemas??{};
const refMap=new Map();
for(const [kind,values]of Object.entries(spec.components??{}))for(const [name,value]of Object.entries(values))refMap.set('#/components/'+kind+'/'+name.replaceAll('~','~0').replaceAll('/','~1'),{name,kind,value,id:'reference/'+(kind==='schemas'?'schema/':'component/'+kind+'-')+slug(name)});
function resolveRef(ref){const item=refMap.get(ref);if(!item)throw new Error(`Unsupported or missing reference: ${ref}`);return item;}
function references(value){const found=new Set();function scan(v){if(!v||typeof v!=='object')return;for(const [k,x]of Object.entries(v)){if(k==='$ref'){resolveRef(x);found.add(x);}else scan(x);}}scan(value);return [...found].sort().map(ref=>{const item=resolveRef(ref);return `- [${item.kind}: ${item.name}](../${item.id.replace('reference/','')}.md)`;}).join('\n');}
const operations=[], groups=new Map();
for(const [route,item]of Object.entries(spec.paths??{}))for(const method of ['get','post','put','patch','delete','options','head'])if(item[method]){
 const op=item[method],id='reference/rest/'+slug(method+' '+route),title=method.toUpperCase()+' '+route;
 const parameters=[...(item.parameters??[]),...(op.parameters??[])].map(p=>p.$ref?resolveRef(p.$ref).value:p);
 let body=`${op.summary??''}\n\n${op.description??''}\n\nClassificação: consulte a [governança](../../api/governance.md). A presença no contrato não promove uma superfície experimental a estável.\n\n`;
 if(op.deprecated)body+='**Operação marcada como deprecated na especificação.**\n\n';
 if(parameters.length)body+='## Parâmetros\n\n| Nome | Origem | Obrigatório | Schema |\n| --- | --- | --- | --- |\n'+parameters.map(p=>`| ${table(p.name)} | ${table(p.in)} | ${p.required?'Sim':'Não'} | ${table(p.schema?.type??p.schema?.$ref??p.$ref)} |`).join('\n')+'\n\n';
 if(op.requestBody)body+='## Corpo da requisição\n\n```json\n'+JSON.stringify(op.requestBody,null,2)+'\n```\n\n';
 body+='## Respostas\n\n| Status | Descrição |\n| --- | --- |\n'+Object.entries(op.responses??{}).map(([code,response])=>`| ${code} | ${table(response.description)} |`).join('\n')+'\n\n```json\n'+JSON.stringify(op.responses??{},null,2)+'\n```\n\n';
 body+='## Schemas relacionados\n\n'+(references(op)||'Não há referência a schema nomeado nesta operação.')+'\n\n[Voltar ao índice REST](../rest.md) · [Contrato original](pathname:///HeroScript/contracts/heroscript-v1.json)\n';
 await page(id,title,body);operations.push({id,title,route,method});const tag=op.tags?.[0]??'Outras operações';if(!groups.has(tag))groups.set(tag,[]);groups.get(tag).push(id);
}
await page('reference/rest','Referência REST v1',`Esta referência é gerada do contrato versionado, na mesma revisão dos guias. A especificação estática é uma superfície revisável: sua ausência não prova que um endpoint descrito nos guias esteja indisponível. Nenhum payload ou garantia de estabilidade é inventado.\n\n[Download OpenAPI](pathname:///HeroScript/contracts/heroscript-v1.json) · [Comandos e erros](../api/contracts.md)\n\n| Operação | Endpoint |\n| --- | --- |\n${operations.map(o=>`| [${o.method.toUpperCase()}](rest/${path.basename(o.id)}.md) | \`${o.route}\` |`).join('\n')}`);
for(const [name,value]of Object.entries(schemas))await page('reference/schema/'+slug(name),name,'```json\n'+JSON.stringify(value,null,2)+'\n```\n\n## Referências\n\n'+(references(value)||'Sem referências nomeadas.')+'\n\n[Índice REST](../rest.md)');
for(const item of refMap.values())if(item.kind!=='schemas')await page(item.id,item.kind+': '+item.name,'```json\n'+JSON.stringify(item.value,null,2)+'\n```\n\n'+references(item.value)+'\n\n[Índice REST](../rest.md)');
await page('reference/events','Eventos e SSE',`Os eventos duráveis são projeções derivadas do journal. Não substituem comandos ou o estado autoritativo. A API documenta SSE; esta página não anuncia um transporte adicional.\n\n[Guia de eventos](../api/events.md) · [Download AsyncAPI](pathname:///HeroScript/contracts/heroscript-events-v1.json)\n\n## Canais publicados\n\n${Object.entries(events.channels??{}).map(([name,value])=>'### '+name+'\n\n```json\n'+JSON.stringify(value,null,2)+'\n```').join('\n\n')}`);
const repo=process.env.DOCS_REPOSITORY||path.join(siteRoot,'..');let examples=[];
for(const source of ['docs/schemas/build-composition-content.schema.json','docs/schemas/gameplay-response.schema.json','docs/schemas/gameplay-event-catalog.schema.json']){
 const bytes=git(repo,'show',`${report.sourceSha}:${source}`);JSON.parse(bytes);const file=path.join(root,'static/contracts',path.basename(source));await writeFile(file,bytes);examples.push(`- [${path.basename(source)}](pathname:///HeroScript/contracts/${path.basename(source)})`);
}
await page('reference/schemas','Schemas e exemplos',examples.join('\n')+'\n\n## Jornadas HTTP\n\n'+report.assets.filter(a=>a.source.startsWith('examples/http/')).map(a=>`- [${path.basename(a.source)}](pathname:///HeroScript/assets/${a.source})`).join('\n')+'\n\nOs exemplos usam IDs e credenciais fictícios; substitua-os no seu ambiente. O GitHub Pages não hospeda a engine nem executa requests.');
await writeFile(path.join(root,'reference-sidebar.json'),JSON.stringify(['portal/reference','reference/rest',...[...groups].map(([label,items])=>({type:'category',label,collapsed:true,items})),{type:'category',label:'Schemas REST',collapsed:true,items:Object.keys(schemas).sort().map(n=>'reference/schema/'+slug(n))},'reference/events','reference/schemas','portal/glossary'],null,2));
console.log(`Generated ${operations.length} REST operations and ${Object.keys(schemas).length} schemas.`);

const manifest=JSON.parse(await readFile(path.join(siteRoot,'docs-manifest.json'),'utf8'));
const entries=[];
for(const item of (Array.isArray(manifest)?manifest:manifest.entries)){
 const text=await readFile(path.join(docs,item.docId+'.md'),'utf8');
 entries.push({title:item.title,url:item.slug,section:item.section,text:text.replace(/^---[\s\S]*?---\s*/, '').replace(/\[([^\]]+)\]\([^)]+\)/g,'$1').replace(/```[\s\S]*?```/g,' ').replace(/[#*`|]/g,' ').replace(/\s+/g,' ').trim().slice(0,6000)});
}
for(const op of operations)entries.push({title:op.title,url:'/'+op.id+'/',section:'reference',text:spec.paths[op.route][op.method].description??spec.paths[op.route][op.method].summary??''});
for(const [name,value]of Object.entries(schemas))entries.push({title:name,url:'/reference/schema/'+slug(name)+'/',section:'reference',text:value.description??''});
await writeFile(path.join(root,'static/search-index.json'),JSON.stringify(entries));
