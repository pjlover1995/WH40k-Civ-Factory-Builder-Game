import { readFileSync, readdirSync } from 'node:fs';
import { basename, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../Assets/Scripts/Game/', import.meta.url));
const files = readdirSync(root, { recursive: true })
  .filter(name => /(?:Menu|UrbanPanel)\.cs$/.test(name));
const rawUi = /\bnew\s+(?:UnityEngine\.)?Color(?:32)?\s*\(|\b(?:typeof\s*\(\s*|AddComponent\s*<\s*)(?:UnityEngine\.UI\.)?(?:Button|Text)\b/g;
let violations = 0;
for (const file of files) {
  const source = readFileSync(join(root, file), 'utf8');
  for (const match of source.matchAll(rawUi)) {
    const before = source.slice(0, match.index);
    const lineStart = before.lastIndexOf('\n') + 1;
    const lineEnd = source.indexOf('\n', match.index);
    const line = source.slice(lineStart, lineEnd === -1 ? source.length : lineEnd);
    if (line.trimStart().startsWith('//')) continue;
    if (basename(file) === 'LandingMenu.cs' && line.includes('landingMaterial') && line.includes('World preview material')) continue;
    const lineNumber = before.split('\n').length;
    console.error(file + ':' + lineNumber + ' Use a CommandUi color, button, or label.');
    violations++;
  }
}
if (violations) process.exitCode = 1;
else console.log('Shared UI check passed for ' + files.length + ' menu and panel files.');
