// Brand exports from one SVG source. Only rebuilding artwork needs Node.js + sharp.
// npm install --no-save --prefix artifacts/icon-tools sharp
// node tools/make-icon.mjs
// Or supply an existing sharp package via MAILHELPER_ICON_SHARP_PATH.
import { createRequire } from 'node:module';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
let sharp;
for (const candidate of [process.env.MAILHELPER_ICON_SHARP_PATH, 'sharp',
  resolve(root, 'artifacts/icon-tools/node_modules/sharp')].filter(Boolean)) {
  try { sharp = require(candidate); break; } catch { /* Try the next local installation. */ }
}
if (!sharp) throw new Error('Rebuilding icons requires sharp; see tools/make-icon.mjs.');

const svg = await readFile(resolve(root, 'docs/assets/mailhelper-mark.svg'));
const render = size => sharp(svg).resize(size, size).png().toBuffer();
const save = async (relative, bytes) => {
  const target = resolve(root, relative);
  await mkdir(dirname(target), { recursive: true });
  await writeFile(target, bytes);
};

// A native frame for each common Windows scale factor, plus retina/macOS artwork.
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const frames = await Promise.all(sizes.map(render));
const directory = Buffer.alloc(6 + 16 * sizes.length);
directory.writeUInt16LE(1, 2);
directory.writeUInt16LE(sizes.length, 4);
let offset = directory.length;
for (let i = 0; i < sizes.length; i++) {
  const base = 6 + i * 16;
  directory[base] = sizes[i] & 255;
  directory[base + 1] = sizes[i] & 255;
  directory.writeUInt16LE(1, base + 4);
  directory.writeUInt16LE(32, base + 6);
  directory.writeUInt32LE(frames[i].length, base + 8);
  directory.writeUInt32LE(offset, base + 12);
  offset += frames[i].length;
}
await save('src/MailHelper.App/Assets/app.ico', Buffer.concat([directory, ...frames]));
await save('src/MailHelper.App/Assets/brand-mark.png', await render(256));
await save('src/MailHelper.App.Avalonia/Assets/icon-source.png', await render(1024));
await save('docs/assets/logo.png', await render(256));

// Actual small exports on light and dark surfaces, plus nearest-neighbour enlargements.
const layers = [];
for (let row = 0; row < 2; row++) {
  for (let i = 0; i < 5; i++) {
    const size = sizes[i];
    layers.push({ input: frames[i], left: 26 + i * 110 + Math.floor((80 - size) / 2), top: row * 235 + 20 });
    layers.push({ input: await sharp(frames[i]).resize(size * 3, size * 3, { kernel: 'nearest' }).png().toBuffer(),
      left: 26 + i * 110 + Math.floor((80 - size * 3) / 2), top: row * 235 + 94 });
  }
}
const labels = [0, 1].map(row => sizes.slice(0, 5).map((size, i) =>
  `<text x="${66 + i * 110}" y="${77 + row * 235}" text-anchor="middle" fill="${row ? '#C1CBBE' : '#66736D'}" font-family="Segoe UI,sans-serif" font-size="12">${size}px / 3x</text>`).join('')).join('');
const backdrop = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="590" height="470"><path fill="#F5F4EF" d="M0 0h590v235H0z"/><path fill="#17211D" d="M0 235h590v235H0z"/>${labels}</svg>`);
await save('artifacts/screens/brand-icon-sizes.png', await sharp(backdrop).composite(layers).png().toBuffer());
await save('artifacts/screens/icon-preview.png', await render(256));
console.log(`Generated ${sizes.join('/')}px ICO, 256px app/docs PNG and 1024px macOS PNG from mailhelper-mark.svg.`);
