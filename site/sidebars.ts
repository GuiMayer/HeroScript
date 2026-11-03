import manifest from './docs-manifest.json';
const sidebar=(section:string, intro:string)=>[intro,...Array.from(new Set(manifest.entries.filter(e=>e.section===section).map(e=>e.category))).map(category=>({type:'category' as const,label:category,items:manifest.entries.filter(e=>e.section===section&&e.category===category).map(e=>e.docId)}))];
export default {apiSidebar:sidebar('api','portal/api'),developersSidebar:sidebar('developers','portal/developers'),referenceSidebar:['portal/reference']};
