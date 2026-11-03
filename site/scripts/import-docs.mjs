import {execFileSync} from 'node:child_process';
import {readFile, writeFile, mkdir, rm} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {unified} from 'unified';
import parse from 'remark-parse';
import stringify from 'remark-stringify';
import gfm from 'remark-gfm';
import {visit} from 'unist-util-visit';

export const siteRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export function git(repo, ...args) {return execFileSync('git', ['-C', repo, ...args], {maxBuffer: 32 * 1024 * 1024});}
export const digest = bytes => createHash('sha256').update(bytes).digest('hex');
export function safeRelative(value) {
  if (typeof value !== 'string' || !value || value.includes('\\') || value.includes(':') || value.startsWith('/') || value.split('/').includes('..')) throw new Error(`Unsafe source path: ${value}`);
  return value;
}
export function checkManifest(manifest, policy) {
  const ids = new Set(), slugs = new Set(), sources = new Set();
  for (const entry of manifest.entries) {
    safeRelative(entry.source);
    if (!policy.allowedRoots.some(root => entry.source.startsWith(root)) || policy.excludedRoots.some(root => entry.source.startsWith(root))) throw new Error(`Unapproved source: ${entry.source}`);
    if (!['api', 'developers'].includes(entry.section) || !entry.slug.startsWith(`/${entry.section}/`) || !entry.slug.endsWith('/')) throw new Error(`Invalid route: ${entry.slug}`);
    if (ids.has(entry.docId) || slugs.has(entry.slug) || sources.has(entry.source)) throw new Error(`Duplicate manifest entry: ${entry.source}`);
    if (entry.docId !== entry.source.replace(/^docs\//, '').replace(/\.md$/, '')) throw new Error(`Document identity must preserve source path: ${entry.source}`);
    ids.add(entry.docId); slugs.add(entry.slug); sources.add(entry.source);
  }
}

export async function importDocs() {
  const repo = path.resolve(process.env.DOCS_REPOSITORY || path.join(siteRoot, '..'));
  let canonical = 'origin/main';
  if (!process.env.CI) {
    try { git(repo, 'rev-parse', '--verify', '--quiet', 'refs/heads/main'); canonical = 'main'; } catch { /* A portal-only clone has only origin/main. */ }
  }
  const requested = process.env.DOCS_SOURCE_SHA || canonical;
  if (!/^(main|origin\/main|[a-f0-9]{40})$/.test(requested)) throw new Error('Source must be main or a full commit SHA');
  const sha = git(repo, 'rev-parse', '--verify', `${requested}^{commit}`).toString().trim();
  git(repo, 'merge-base', '--is-ancestor', sha, canonical);
  const siteSha = git(siteRoot, 'rev-parse', 'HEAD').toString().trim();
  const manifest = JSON.parse(await readFile(path.join(siteRoot, 'docs-manifest.json'), 'utf8'));
  const policy = JSON.parse(await readFile(path.join(siteRoot, 'source-policy.json'), 'utf8'));
  checkManifest(manifest, policy);
  const tracked = new Map(git(repo, 'ls-tree', '-r', sha).toString().trim().split('\n').map(line => {
    const [metadata, filename] = line.split('\t'); return [filename, metadata.split(' ')[0]];
  }));
  const read = source => {
    safeRelative(source);
    if (tracked.get(source) !== '100644' && tracked.get(source) !== '100755') throw new Error(`Missing/non-regular source: ${source}`);
    return git(repo, 'show', `${sha}:${source}`);
  };
  const generated = path.join(siteRoot, 'generated');
  if (path.dirname(generated) !== siteRoot || path.basename(generated) !== 'generated') throw new Error('Invalid output boundary');
  await rm(generated, {recursive: true, force: true});
  await mkdir(path.join(generated, 'docs'), {recursive: true});
  await mkdir(path.join(generated, 'static/contracts'), {recursive: true});
  const report = {sourceSha: sha, siteSha, documents: [], assets: [], excluded: [], unclassified: []};
  const bySource = new Map(manifest.entries.map(entry => [entry.source, entry]));
  const copied = new Set();
  const sourceUrl = source => `https://github.com/${policy.repository}/blob/${sha}/${source.split('/').map(encodeURIComponent).join('/')}`;
  async function asset(source) {
    safeRelative(source);
    if (!/\.(png|svg|jpe?g|webp|gif|json|ya?ml|http)$/i.test(source)) throw new Error(`Asset type not authorized: ${source}`);
    if (!policy.allowedRoots.some(root => source.startsWith(root)) || policy.excludedRoots.some(root => source.startsWith(root))) throw new Error(`Asset outside policy: ${source}`);
    const bytes = read(source), target = path.join(generated, 'static/assets', source);
    if (!copied.has(source)) {
      await mkdir(path.dirname(target), {recursive: true}); await writeFile(target, bytes);
      copied.add(source); report.assets.push({source, sha256: digest(bytes)});
    }
    return `/HeroScript/assets/${source.split('/').map(encodeURIComponent).join('/')}`;
  }
  const processor = unified().use(parse).use(gfm).use(stringify, {fences: true, bullet: '-', listItemIndent: 'one'});
  for (const entry of manifest.entries) {
    const bytes = read(entry.source);
    const text = bytes.toString('utf8').replace(/^\uFEFF/, '');
    if (/^---\r?\n[\s\S]*?\n(?:id|slug|title):/.test(text)) throw new Error(`Source front matter requires explicit migration: ${entry.source}`);
    const ast = processor.parse(text), nodes = [];
    visit(ast, node => {
      if (['link', 'image', 'definition'].includes(node.type)) nodes.push(node);
      if (node.type === 'html' && /<\s*(script|iframe)|\bon\w+\s*=/i.test(node.value)) throw new Error(`Executable HTML rejected: ${entry.source}`);
    });
    for (const node of nodes) {
      const url = node.url;
      if (/^(javascript|data|file):|^[A-Za-z]:[\\/]/i.test(url)) throw new Error(`Unsafe link in ${entry.source}: ${url}`);
      if (/^(https?:|mailto:|#)/.test(url)) continue;
      if (url.startsWith('/')) throw new Error(`Absolute source link needs editorial mapping in ${entry.source}: ${url}`);
      const index = url.indexOf('#');
      const file = decodeURIComponent(index < 0 ? url : url.slice(0, index));
      const fragment = index < 0 ? '' : url.slice(index);
      const target = path.posix.normalize(path.posix.join(path.posix.dirname(entry.source), file));
      safeRelative(target);
      if (!tracked.has(target)) throw new Error(`Broken source link: ${entry.source} -> ${url}`);
      if (bySource.has(target)) {
        node.url = path.posix.relative(path.posix.dirname(entry.source), target) + fragment;
      } else if (node.type === 'image' || /\.(json|ya?ml|http)$/.test(target)) {
        node.url = 'pathname://' + await asset(target) + fragment;
      } else {node.url = sourceUrl(target) + fragment;}
    }
    const metadata = `---\ntitle: ${JSON.stringify(entry.title)}\nslug: ${JSON.stringify(entry.slug)}\nedit_url: ${JSON.stringify(`https://github.com/${policy.repository}/edit/main/${entry.source}`)}\n---\n`;
    const provenance = `\n\n---\n[Fonte desta publicação (${sha.slice(0, 8)})](${sourceUrl(entry.source)}) · [Veja também](/HeroScript/${entry.section === 'api' ? 'developers' : 'api'}/)\n`;
    const target = path.join(generated, 'docs', entry.source.replace(/^docs\//, ''));
    await mkdir(path.dirname(target), {recursive: true});
    await writeFile(target, metadata + processor.stringify(ast) + provenance);
    report.documents.push({source: entry.source, docId: entry.docId, slug: entry.slug, sha256: digest(bytes)});
  }
  for (const name of ['api', 'developers', 'reference', 'glossary']) {
    const source = path.join(siteRoot, 'docs/portal', `${name}.md`);
    const target = path.join(generated, 'docs/portal', `${name}.md`);
    await mkdir(path.dirname(target), {recursive: true}); await writeFile(target, await readFile(source));
  }
  for (const source of ['openapi/heroscript-v1.json', 'asyncapi/heroscript-events-v1.json']) {
    const bytes = read(source); await writeFile(path.join(generated, 'static/contracts', path.basename(source)), bytes);
    report.assets.push({source, sha256: digest(bytes)});
  }
  for (const source of tracked.keys()) if (source.startsWith('examples/http/') && source.endsWith('.http')) await asset(source);
  for (const source of tracked.keys()) if (source.startsWith('docs/') && source.endsWith('.md') && !bySource.has(source)) {
    (policy.excludedRoots.some(root => source.startsWith(root)) ? report.excluded : report.unclassified).push(source);
  }
  report.documents.sort((a,b) => a.source.localeCompare(b.source));
  report.assets.sort((a,b) => a.source.localeCompare(b.source));
  await writeFile(path.join(generated, 'import-report.json'), JSON.stringify(report, null, 2) + '\n');
  await writeFile(path.join(generated, 'static/site-version.json'), JSON.stringify({sourceSha: sha, siteSha, documentCount: report.documents.length}, null, 2) + '\n');
  console.log(`Imported ${report.documents.length} documents from ${sha}; ${report.unclassified.length} unclassified files remain excluded.`);
  return report;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await importDocs();
