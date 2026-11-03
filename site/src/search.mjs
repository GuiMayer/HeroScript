const normalize=value=>value.normalize('NFD').replace(/\p{Diacritic}/gu,'').toLowerCase();
export function searchEntries(entries,query,section=''){
 const terms=normalize(query).trim().split(/\s+/).filter(Boolean);
 if(!terms.length)return [];
 return entries.filter(e=>(!section||e.section===section)&&terms.every(t=>normalize(e.title+' '+e.text).includes(t))).sort((a,b)=>Number(normalize(b.title).includes(normalize(query)))-Number(normalize(a.title).includes(normalize(query)))).slice(0,30);
}
