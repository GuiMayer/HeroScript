import {appendFileSync} from 'node:fs';
const repository=process.env.GITHUB_REPOSITORY;
if(repository!=='GuiMayer/HeroScript')throw new Error('Publication repository mismatch');
async function ref(name){const response=await fetch(`https://api.github.com/repos/${repository}/git/ref/heads/${name}`,{headers:{Authorization:`Bearer ${process.env.GH_TOKEN}`,'X-GitHub-Api-Version':'2022-11-28',Accept:'application/vnd.github+json'}});if(!response.ok)throw new Error(`Cannot resolve publication ref (${response.status})`);return (await response.json()).object.sha;}
const site=await ref('codex/docs-site'),source=await ref('main');
const rollback=process.env.ROLLBACK==='true'&&process.env.EVENT_NAME==='workflow_dispatch';
const current=site===process.env.SITE_SHA&&(rollback||source===process.env.SOURCE_SHA);
appendFileSync(process.env.GITHUB_OUTPUT,`current=${current}\n`);
console.log(current?'Publication revisions are current.':'Superseded artifact skipped; the current push schedules its own build.');
