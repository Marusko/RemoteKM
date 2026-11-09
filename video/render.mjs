// Renders index.html to an MP4, frame by frame, through headless Chrome and ffmpeg.
//
//   node render.mjs                       -> remotekm-promo-en.mp4, 1920x1080 at 30 fps
//   node render.mjs --lang sk             -> remotekm-promo-sk.mp4
//   node render.mjs --fps 60 --out a.mp4
//   node render.mjs --stills 3,15,24      -> one PNG per listed second, for checking scenes
//   node render.mjs --mute                -> without the music
//
// Needs Node 22+ (for the built-in WebSocket), Chrome or Edge, and ffmpeg on PATH. Nothing to
// install: Chrome is driven over the DevTools protocol directly.
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = {};
for (let i = 2; i < process.argv.length; i++) {
    const key = process.argv[i].replace(/^--/, '');
    args[key] = process.argv[i + 1]?.startsWith('--') === false ? process.argv[++i] : true;
}

const fps = Number(args.fps ?? 30);
const lang = typeof args.lang === 'string' ? args.lang : 'en';
const [width, height] = [1920, 1080];
const out = resolve(args.out ?? join(here, `remotekm-promo-${lang}.mp4`));
const stills = typeof args.stills === 'string' ? args.stills.split(',').map(Number) : null;

const browser = [
    process.env.CHROME,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/usr/bin/google-chrome',
    '/usr/bin/chromium',
    '/usr/bin/chromium-browser',
    // Playwright's Chromium, wherever it was installed
    ...['/opt/pw-browsers', join(homedir(), '.cache/ms-playwright')].flatMap(dir => {
        try {
            return readdirSync(dir).filter(d => /^chromium-\d+$/.test(d)).sort().reverse()
                .flatMap(d => [join(dir, d, 'chrome-linux64/chrome'), join(dir, d, 'chrome-linux/chrome')]);
        } catch { return []; }
    })
].find(p => p && existsSync(p));
if (!browser) throw new Error('No Chrome or Edge found; set CHROME to its path');

const profile = mkdtempSync(join(tmpdir(), 'remotekm-render-'));
const port = 9300 + Math.floor(Math.random() * 500);
const chrome = spawn(browser, [
    '--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
    '--allow-file-access-from-files', '--hide-scrollbars', '--force-device-scale-factor=1',
    `--window-size=${width},${height}`, '--no-first-run', '--mute-audio',
    // Chrome refuses to run as root (containers, CI) without this.
    ...(process.getuid?.() === 0 ? ['--no-sandbox'] : []),
    'about:blank'
], { stdio: 'ignore' });

async function target() {
    for (let i = 0; i < 100; i++) {
        try {
            const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
            const page = list.find(t => t.type === 'page');
            if (page) return page.webSocketDebuggerUrl;
        } catch { /* not up yet */ }
        await new Promise(r => setTimeout(r, 100));
    }
    throw new Error('Chrome did not start');
}

const ws = new WebSocket(await target());
await new Promise(r => ws.addEventListener('open', r, { once: true }));
let nextId = 0;
const pending = new Map();
const loaded = [];
ws.addEventListener('message', e => {
    const msg = JSON.parse(e.data);
    if (msg.id != null) {
        const p = pending.get(msg.id);
        pending.delete(msg.id);
        msg.error ? p.reject(new Error(msg.error.message)) : p.resolve(msg.result);
    } else if (msg.method === 'Page.loadEventFired') loaded.splice(0).forEach(r => r());
});
const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++nextId;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async expression => (await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })).result.value;

await send('Page.enable');
await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
const onLoad = new Promise(r => loaded.push(r));
await send('Page.navigate', { url: pathToFileURL(join(here, 'index.html')).href + `?clean&lang=${lang}` });
await onLoad;
await evaluate('document.fonts.ready.then(() => new Promise(r => setTimeout(r, 300)))');

// Freeze the CSS animations (the pulsing dots, the caret) and drive them from the video clock.
await evaluate(`document.getAnimations().forEach(a => a.pause()); true`);
const seek = t => evaluate(`window.__seek(${t}); document.getAnimations().forEach(a => a.currentTime = ${t * 1000}); true`);
const shot = async () => Buffer.from((await send('Page.captureScreenshot', { format: 'png', optimizeForSpeed: true })).data, 'base64');

try {
    if (stills) {
        for (const t of stills) {
            await seek(t);
            const file = join(dirname(out), `still-${lang}-${String(t).replace('.', '_')}.png`);
            writeFileSync(file, await shot());
            console.log(file);
        }
    } else {
        const duration = await evaluate('window.__duration');
        const frames = Math.round(duration * fps);
        // The page names its music; it is cut to the video's length with the page's fade-out.
        const music = args.mute ? null : await evaluate('window.__music');
        const audio = music ? [
            '-i', join(here, music.src),
            '-filter_complex', `[1:a]atrim=0:${duration},afade=t=out:st=${duration - music.fadeOut}:d=${music.fadeOut}[a]`,
            '-map', '0:v', '-map', '[a]', '-c:a', 'aac', '-b:a', '192k'
        ] : [];
        const ffmpeg = spawn('ffmpeg', [
            '-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(fps), '-i', '-', ...audio,
            '-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', out
        ], { stdio: ['pipe', 'inherit', 'inherit'] });
        const done = new Promise((res, rej) => ffmpeg.on('close', c => c === 0 ? res() : rej(new Error(`ffmpeg exited ${c}`))));
        const started = Date.now();
        for (let f = 0; f < frames; f++) {
            await seek(f / fps);
            const png = await shot();
            if (!ffmpeg.stdin.write(png)) await new Promise(r => ffmpeg.stdin.once('drain', r));
            if (f % fps === 0) process.stdout.write(`\r${(f / fps).toFixed(0)}s / ${duration}s  (${((Date.now() - started) / 1000).toFixed(0)}s elapsed)`);
        }
        ffmpeg.stdin.end();
        await done;
        console.log(`\n${out}`);
    }
} finally {
    ws.close();
    const exited = new Promise(r => chrome.once('exit', r));
    chrome.kill();
    await exited;
    // Chrome's helper processes can hold the profile a little longer on Windows; it is only temp.
    try { rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 }); } catch { }
}
