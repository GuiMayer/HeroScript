import {readFile,readdir,stat,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {parse} from 'parse5';
import {siteRoot,digest} from './import-docs.mjs';
const build=path.join(siteRoot,'build');
async function walk(root){const files=[];for(const item of await readdir(root,{withFileTypes:true})){const file=path.join(root,item.name);if(item.isDirectory())files.push(...await walk(file));else files.push(file);}return files;}
const all=await walk(build),html=all.filter(f=>f.endsWith('.html')),pages=new Map(),links=[];
for(const file of html){const relative=path.relative(build,file).replaceAll('\\','/');const url=new URL('/HeroScript/'+relative.replace(/index\.html$/,''),'https://docs.invalid');const ids=new Set();const tree=parse(await readFile(file,'utf8'));
 function scan(node){const attrs=Object.fromEntries((node.attrs??[]).map(a=>[a.name,a.value]));if(attrs.id)ids.add(attrs.id);for(const key of ['href','src'])if(attrs[key])links.push({source:file,url,href:attrs[key]});for(const child of node.childNodes??[])scan(child);}
 scan(tree);pages.set(file,ids);
}
const errors=[],cache=new Map();
async function targetFile(url){const pathname=decodeURIComponent(url.pathname);if(!pathname.startsWith('/HeroScript/'))return null;let relative=pathname.slice('/HeroScript/'.length);if(relative.split('/').includes('..'))return null;let file=path.resolve(build,relative);if(!file.startsWith(build+path.sep)&&file!==build)return null;
 if(cache.has(file))return cache.get(file);const original=file;try{if((await stat(file)).isDirectory())file=path.join(file,'index.html');await stat(file);}catch{try{file=path.join(original,'index.html');await stat(file);}catch{file=null;}}cache.set(original,file);return file;}
for(const link of links){if(/^(mailto:|tel:|data:)/.test(link.href))continue;if(/^(javascript:|file:|pathname:)/i.test(link.href)){errors.push(`Unsafe/unresolved link: ${link.href}`);continue;}let url;try{url=new URL(link.href,link.url);}catch{errors.push(`Invalid URL: ${link.href}`);continue;}if(url.origin!=='https://docs.invalid')continue;const target=await targetFile(url);if(!target){errors.push(`${path.relative(build,link.source)} -> missing ${url.pathname}`);continue;}if(url.hash&&url.hash!=='#'&&pages.has(target)&&!pages.get(target).has(decodeURIComponent(url.hash.slice(1))))errors.push(`${path.relative(build,link.source)} -> missing anchor ${url.pathname}${url.hash}`);}
const imported=JSON.parse(await readFile(path.join(siteRoot,'generated/import-report.json'),'utf8'));
for(const asset of imported.assets){const relative=asset.source.startsWith('openapi/')||asset.source.startsWith('asyncapi/')?'contracts/'+path.basename(asset.source):'assets/'+asset.source;const bytes=await readFile(path.join(build,relative));if(digest(bytes)!==asset.sha256)errors.push(`Asset differs from pinned source: ${asset.source}`);}
const version=JSON.parse(await readFile(path.join(build,'site-version.json'),'utf8'));if(version.sourceSha!==imported.sourceSha)errors.push('Published source version mismatch');
const report={pages:html.length,links:links.length,sourceSha:imported.sourceSha,siteSha:imported.siteSha,errors:[...new Set(errors)]};await writeFile(path.join(siteRoot,'generated/validation-report.json'),JSON.stringify(report,null,2)+'\n');
if(errors.length)throw new Error(report.errors.join('\n'));console.log(`Validated ${html.length} HTML pages and ${links.length} links, anchors and source assets.`);
