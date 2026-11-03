import React, {useEffect,useState} from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import {searchEntries} from '../search.mjs';
type Entry={title:string,url:string,section:string,text:string};
export default function Search(){
 const [entries,setEntries]=useState<Entry[]>([]),[query,setQuery]=useState(''),[section,setSection]=useState(''),[failed,setFailed]=useState(false);
 const index=useBaseUrl('/search-index.json');
 useEffect(()=>{const abort=new AbortController();fetch(index,{signal:abort.signal}).then(r=>{if(!r.ok)throw Error();return r.json()}).then(setEntries).catch(e=>{if(e.name!=='AbortError')setFailed(true)});return()=>abort.abort()},[index]);
 const results=searchEntries(entries,query,section);
 return <Layout title="Buscar documentação"><main className="container margin-vert--lg"><h1>Buscar documentação</h1>
 <label htmlFor="query">Termos, endpoints ou sistemas</label><input id="query" type="search" value={query} onChange={e=>setQuery(e.target.value)} className="search-input"/>
 <label htmlFor="audience">Seção</label><select id="audience" value={section} onChange={e=>setSection(e.target.value)}><option value="">Todas</option><option value="api">Consumidores da API</option><option value="developers">Desenvolvedores</option><option value="reference">Referências</option></select>
 <p role="status">{failed?'Não foi possível carregar a busca. Use a navegação por seções.':query.trim()?`${results.length} resultados (até 30).`:'Digite para buscar nos artigos e contratos publicados.'}</p>
 <ul>{results.map((e:Entry)=><li key={e.url}><Link to={e.url}>{e.title}</Link><p>{e.text.slice(0,180)}</p></li>)}</ul></main></Layout>
}
