#!/usr/bin/env node
/**
 * Pemeriksaan gaya ringan untuk frontend (pengganti ESLint di baseline repo).
 * Fokus pada hal yang mahal bila lolos ke produksi: `any`, log sisa debug,
 * dan berkas client yang lupa `'use client';`.
 */
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

const ROOT = path.resolve(process.cwd(), 'src');
const EXTENSIONS = new Set(['.ts', '.tsx']);

async function walk(dir) {
  const entries = await readdir(dir, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) files.push(...(await walk(full)));
    else if (EXTENSIONS.has(path.extname(entry.name))) files.push(full);
  }
  return files;
}

const problems = [];

function report(file, line, message) {
  problems.push(`${path.relative(process.cwd(), file)}:${line}  ${message}`);
}

const files = await walk(ROOT);

for (const file of files) {
  const relative = path.relative(ROOT, file).replaceAll('\\', '/');
  const source = await readFile(file, 'utf8');
  const lines = source.split(/\r?\n/);

  lines.forEach((line, index) => {
    if (/\bdebugger\b/.test(line)) report(file, index + 1, 'hapus pernyataan debugger');
    if (/\bconsole\.log\(/.test(line)) report(file, index + 1, 'hapus console.log sebelum commit');
    if (/:\s*any\b|<any>|\bas\s+any\b/.test(line)) report(file, index + 1, 'hindari tipe any');
    if (/[ \t]+$/.test(line)) report(file, index + 1, 'trailing whitespace');
    if (line.length > 160) report(file, index + 1, 'baris melebihi 160 karakter');
  });

  const isServerOnly =
    relative.startsWith('lib/server/') ||
    relative.startsWith('app/api/') ||
    relative === 'app/layout.tsx' ||
    relative.endsWith('/page.tsx') ||
    relative.endsWith('/not-found.tsx') ||
    relative.startsWith('components/layout/PublicShell') ||
    relative.startsWith('components/traceability/TraceDetailView');

  const needsClient = /\b(useState|useEffect|useRouter|useMemo|useCallback|useRef)\s*\(/.test(source);
  const declaresClient = /^\s*'use client';/.test(source);

  if (needsClient && !declaresClient && !isServerOnly) {
    report(file, 1, "berkas memakai hook React tetapi tidak diawali 'use client';");
  }
  if (declaresClient && !source.trimStart().startsWith("'use client';")) {
    report(file, 1, "'use client'; harus menjadi pernyataan pertama");
  }
}

if (problems.length > 0) {
  console.error(`Lint gagal: ${problems.length} masalah\n`);
  for (const problem of problems) console.error(`  ${problem}`);
  process.exit(1);
}

console.log(`Lint bersih: ${files.length} berkas TypeScript diperiksa.`);
