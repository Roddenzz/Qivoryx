import path from 'node:path';
import fs from 'node:fs';
import {bundle} from '@remotion/bundler';
import {openBrowser, selectComposition, renderStill} from '@remotion/renderer';

const timeline = JSON.parse(fs.readFileSync('src/timeline.json', 'utf8'));
const output = path.resolve('../artifacts/video-qa');
fs.mkdirSync(output, {recursive: true});
const serveUrl = await bundle({entryPoint: path.resolve('src/index.ts')});
const browser = await openBrowser('chrome');
try {
  const composition = await selectComposition({serveUrl, id: 'Qivoryx', puppeteerInstance: browser});
  const frames = timeline.scenes.flatMap((s, i) => [
    {frame: s.from + Math.min(110, s.frames-35), name: `${i+1}-${s.id}-a`},
    {frame: s.from + Math.min(s.frames-40, i === 10 ? 430 : Math.max(350, ...Object.values(s.cues ?? {}).map((cue) => cue+45))), name: `${i+1}-${s.id}-b`},
  ]);
  for (let n = 0; n < frames.length; n += 3) {
    await Promise.all(frames.slice(n, n+3).map(async ({frame, name}) => {
      await renderStill({serveUrl, composition, frame, output: path.join(output, `${name}.png`), imageFormat: 'png', puppeteerInstance: browser, logLevel: 'error'});
      console.log(`QA ${name}: ${frame}`);
    }));
  }
} finally {
  await browser.close({silent: true});
}
