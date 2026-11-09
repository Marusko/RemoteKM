// RemoteKM promo video.
//
// Everything on screen is a pure function of one number, the time t in seconds: tweens are
// declared up front against absolute times, and render(t) works out every element's state from
// scratch. That is what lets the scrubber seek anywhere, and render.mjs capture exact frames.
//
// ?lang=sk switches the headlines and captions (strings.js). The app's own screens stay in
// English, as the app itself is.
(() => {
    'use strict';

    const params = new URLSearchParams(location.search);
    const LANG = STRINGS[params.get('lang')] ? params.get('lang') : 'en';
    // Slovak typesetting keeps a one-letter word (a, k, v, z, …) off the end of a line.
    const glue = x => typeof x === 'string' ? x.replace(/(^|\s)([aikosuvz])\s/gi, '$1$2 ')
        : Array.isArray(x) ? x.map(glue) : x;
    const S = LANG === 'sk'
        ? Object.fromEntries(Object.entries(STRINGS.sk).map(([k, v]) => [k, glue(glue(v))]))
        : STRINGS[LANG];
    const W = 1920, H = 1080;

    document.documentElement.lang = LANG;

    const $ = (s, r = document) => r.querySelector(s);
    const $$ = (s, r = document) => [...r.querySelectorAll(s)];
    const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
    const lerp = (a, b, p) => a + (b - a) * p;

    for (const el of $$('[data-t]')) el.textContent = S[el.dataset.t];

    const E = {
        linear: x => x,
        out: x => 1 - Math.pow(1 - x, 3),
        in: x => x * x * x,
        inOut: x => x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2,
        back: x => { const c = 1.70158; return 1 + (c + 1) * Math.pow(x - 1, 3) + c * Math.pow(x - 1, 2); }
    };

    /** Eased progress of t through [start, start + dur]. */
    const ramp = (t, start, dur, ease = 'inOut') => E[ease](clamp((t - start) / dur));
    /** 1 while start <= t < end. */
    const during = (t, start, end) => t >= start && t < end;

    // ------------------------------------------------------------------ tween engine

    const DEF = { opacity: 1, x: 0, y: 0, scale: 1, rot: 0, rx: 0, blur: 0, clipB: 0 };
    const tracks = new Map();
    const updaters = [];
    const scenes = [];

    const els = t => typeof t === 'string' ? $$(t) : t instanceof Element ? [t] : [...t];

    function tw(target, start, dur, props, ease = 'out') {
        for (const el of els(target)) {
            let m = tracks.get(el);
            if (!m) tracks.set(el, m = new Map());
            for (const [p, [from, to]] of Object.entries(props)) {
                let list = m.get(p);
                if (!list) m.set(p, list = []);
                list.push({ start, end: start + dur, from, to, ease: E[ease] });
                list.sort((a, b) => a.start - b.start);
            }
        }
    }

    function enter(target, start, from = { y: 40 }, dur = 0.8, ease = 'out') {
        const props = { opacity: [0, 1] };
        for (const [k, v] of Object.entries(from)) props[k] = [v, DEF[k]];
        tw(target, start, dur, props, ease);
    }

    function exit(target, start, to = {}, dur = 0.5, ease = 'in') {
        const props = { opacity: [1, 0] };
        for (const [k, v] of Object.entries(to)) props[k] = [DEF[k], v];
        tw(target, start, dur, props, ease);
    }

    const stagger = (target, start, step, fn) => els(target).forEach((el, i) => fn(el, start + i * step));
    const every = fn => updaters.push(fn);
    const scene = (sel, from, to) => scenes.push({ el: $(sel), from, to });

    function valueAt(list, t) {
        if (t < list[0].start) return list[0].from;
        let tw = list[0];
        for (const x of list) if (x.start <= t) tw = x;
        if (t >= tw.end) return tw.to;
        return lerp(tw.from, tw.to, tw.ease((t - tw.start) / (tw.end - tw.start)));
    }

    function apply(el, v) {
        const s = el.style;
        if ('opacity' in v) {
            s.opacity = v.opacity;
            s.visibility = v.opacity <= 0.001 ? 'hidden' : '';
        }
        if ('x' in v || 'y' in v || 'scale' in v || 'rot' in v || 'rx' in v) {
            let tr = 'rx' in v ? `perspective(1800px) rotateX(${v.rx}deg) ` : '';
            tr += `translate(${v.x ?? 0}px, ${v.y ?? 0}px) scale(${v.scale ?? 1}) rotate(${v.rot ?? 0}deg)`;
            s.transform = tr;
        }
        if ('blur' in v) s.filter = v.blur > 0.05 ? `blur(${v.blur}px)` : '';
        if ('clipB' in v) s.clipPath = `inset(${v.clipB}% 0 0 0)`;
    }

    function render(t) {
        for (const s of scenes) s.el.style.display = t >= s.from && t < s.to ? '' : 'none';
        for (const [el, m] of tracks) {
            const v = {};
            for (const [p, list] of m) v[p] = valueAt(list, t);
            apply(el, v);
        }
        for (const fn of updaters) fn(t);
    }

    // ------------------------------------------------------------------ helpers

    const NS = 'http://www.w3.org/2000/svg';

    function svgEl(tag, attrs = {}, parent) {
        const el = document.createElementNS(NS, tag);
        for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
        parent?.appendChild(el);
        return el;
    }

    const pad = n => String(Math.floor(n)).padStart(2, '0');
    const clock = s => `${pad(s / 3600)}:${pad(s / 60 % 60)}:${pad(s % 60)}`;

    function setText(el, text) {
        if (el.textContent !== text) el.textContent = text;
    }

    function tag(name, className, html = '') {
        const el = document.createElement(name);
        el.className = className;
        el.innerHTML = html;
        return el;
    }

    /** Shows el only while t is inside one of the [from, to) ranges. */
    function showWhen(el, ...ranges) {
        every(t => { el.style.display = ranges.some(([a, b]) => during(t, a, b)) ? '' : 'none'; });
    }

    /** Toggles a class on el while t is inside one of the ranges. */
    function classWhen(el, cls, ...ranges) {
        every(t => el.classList.toggle(cls, ranges.some(([a, b]) => during(t, a, b))));
    }

    /** Text typed out between start and start + dur. */
    function typewriter(el, text, start, dur) {
        every(t => setText(el, text.slice(0, Math.round(clamp((t - start) / dur) * text.length))));
    }

    function letters(el, text) {
        el.textContent = '';
        return [...text].map(ch => {
            const s = document.createElement('span');
            s.textContent = ch;
            el.appendChild(s);
            return s;
        });
    }

    /** Position of el's box relative to root, through offsetParents (ignores transforms). */
    function offsetIn(el, root) {
        let x = 0, y = 0;
        for (let e = el; e && e !== root; e = e.offsetParent) {
            x += e.offsetLeft;
            y += e.offsetTop;
        }
        return [x, y];
    }

    function centerIn(el, root) {
        const [x, y] = offsetIn(el, root);
        return [x + el.offsetWidth / 2, y + el.offsetHeight / 2];
    }

    /** Piecewise path through [time, x, y] keyframes, eased between each pair. */
    function path(keys) {
        return t => {
            if (t <= keys[0][0]) return [keys[0][1], keys[0][2]];
            for (let i = 1; i < keys.length; i++) {
                const [t1, x1, y1] = keys[i];
                if (t <= t1) {
                    const [t0, x0, y0] = keys[i - 1];
                    const k = E.inOut((t - t0) / (t1 - t0 || 1));
                    return [lerp(x0, x1, k), lerp(y0, y1, k)];
                }
            }
            const last = keys[keys.length - 1];
            return [last[1], last[2]];
        };
    }

    /** Builds a pseudo-random QR code: three finder squares and scattered modules. */
    function makeQr(host, seed0 = 11) {
        const N = 25;
        let seed = seed0;
        const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
        const finder = (r, c, R, C) => {
            const y = r - R, x = c - C;
            if (y < 0 || x < 0 || y > 6 || x > 6) return null;
            return y === 0 || y === 6 || x === 0 || x === 6 || (y >= 2 && y <= 4 && x >= 2 && x <= 4);
        };
        for (let r = 0; r < N; r++) for (let c = 0; c < N; c++) {
            const i = document.createElement('i');
            const f = finder(r, c, 0, 0) ?? finder(r, c, 0, N - 7) ?? finder(r, c, N - 7, 0);
            const inZone = (r < 8 && c < 8) || (r < 8 && c >= N - 8) || (r >= N - 8 && c < 8);
            if (!(f ?? (!inZone && rnd() > 0.5))) i.style.background = 'transparent';
            host.appendChild(i);
        }
    }

    // ------------------------------------------------------------------ timeline

    // Scene start times. The outro logo lands on the music's drop at 1:51.9, after the
    // breakdown that starts at 1:50.3.
    const T = { intro: 0, hook: 6, rig: 13, tray: 83.4, grid: 94.4, stats: 104.2, outro: 111.8 };
    const DURATION = 121;

    // Music by Alexey Kazantsev from Pixabay. It starts with the video and fades out with it.
    const MUSIC = { src: 'assets/331music-energetic-breakbeat-powerful-action-rock-600481.mp3', fadeOut: 2.5 };

    // ------------------------------------------------------------------ background

    (() => {
        const svg = $('.bg-waves');
        // Slow ripples, like a signal going out from the phone and the PC.
        const sources = [[W * 0.16, H * 0.86], [W * 0.86, H * 0.18]];
        const R = 900, N = 7;
        const rings = sources.flatMap(([cx, cy]) =>
            Array.from({ length: N }, (_, i) => ({ c: svgEl('circle', { cx, cy, r: 0 }, svg), i })));
        const a = $('.bg-glow-a'), b = $('.bg-glow-b');
        every(t => {
            for (const { c, i } of rings) {
                const r = (t * 26 + i * R / N) % R;
                c.setAttribute('r', r.toFixed(1));
                c.setAttribute('opacity', (0.16 * Math.sin(Math.PI * r / R)).toFixed(3));
            }
            // The glows drift on their own, and gather behind the logo at the start and end.
            const logo = Math.max(1 - ramp(t, 4.5, 2), ramp(t, T.outro - 0.5, 2));
            a.style.left = `${lerp(W * 0.3 + Math.sin(t * 0.21) * W * 0.14, W / 2, logo)}px`;
            a.style.top = `${lerp(H * 0.35 + Math.cos(t * 0.17) * H * 0.15, H * 0.4, logo)}px`;
            b.style.left = `${W * 0.76 + Math.sin(t * 0.15 + 2) * W * 0.11}px`;
            b.style.top = `${H * 0.67 + Math.cos(t * 0.19) * H * 0.15}px`;
        });
    })();

    // ------------------------------------------------------------------ 1. intro

    {
        const s = T.intro;
        scene('#s-intro', s, T.hook);
        tw('.intro-icon', s + 0.2, 1.4, { scale: [0.6, 1], rot: [-12, 0] }, 'back');
        tw('.intro-icon', s + 0.2, 0.5, { opacity: [0, 1] }, 'linear');
        const ls = letters($('#intro-wordmark'), 'RemoteKM');
        ls.slice(6).forEach(l => l.classList.add('km-letter'));
        stagger(ls, s + 1.1, 0.06, (el, at) => enter(el, at, { y: 70, blur: 10 }, 0.7));
        enter('#name-meaning', s + 1.8, { y: 20, blur: 6 }, 0.8);
        enter('#intro-pill', s + 2.5, { y: 24 }, 0.8);
        exit('.intro-logo', s + 5.2, { scale: 1.08, blur: 14 }, 0.7, 'inOut');
        exit('#name-meaning', s + 5.15, { y: 14, blur: 8 }, 0.55);
        exit('#intro-pill', s + 5.1, { y: 20 }, 0.5);
    }

    // ------------------------------------------------------------------ 2. hook

    {
        const s = T.hook;
        scene('#s-hook', s, T.rig);
        stagger('.hook-line', s + 0.2, 0.28, (el, at) => enter(el, at, { y: 70, blur: 12 }, 0.9));
        enter('.hook-sub', s + 1.6, { y: 30 }, 0.8);
        stagger('.hook-bullets li', s + 2.3, 0.22, (el, at) => enter(el, at, { y: 30, scale: 0.92 }, 0.6, 'back'));
        stagger(['.hook-title', '.hook-sub', '.hook-bullets'].map(x => $(x)), s + 6.1, 0.07, (el, at) => exit(el, at, { y: -40, blur: 8 }, 0.55));
    }

    // ------------------------------------------------------------------ captions

    /** Adds one caption per [key, start, end] to host: it slides in at start and out by end. */
    function captions(host, list) {
        for (const [key, start, end] of list) {
            const el = tag('div', 'caption', '<p class="eyebrow"></p><h2></h2><p></p>');
            el.children[0].textContent = S[key + 'Eyebrow'];
            el.children[1].textContent = S[key + 'Title'];
            el.children[2].textContent = S[key + 'Body'];
            host.appendChild(el);
            enter(el, start, { x: -40 }, 0.7);
            exit(el, end - 0.4, { x: -30 }, 0.4);
        }
    }

    // ------------------------------------------------------------------ 3. the rig

    // The rig runs from T.rig to T.tray: connecting, the trackpad, the keyboard, then the phone
    // on its own. Its beats use absolute times, listed here.
    const B = {
        disc: 13.6, scanTap: 18.9, scan: 19.25, qrOk: 20.5, connecting: 20.9,
        pair: 22.3, accept: 24.5, shell: 24.9,
        keysTap: 50.7, keys: 50.85, monitorOut: 73.4,
        padTap: 74.8, collapse: 75.8, expand: 77.4, statusTap: 78.9, exportTap: 81.0, phoneOut: 82.7
    };

    {
        const end = T.tray;
        scene('#s-rig', T.rig, end);

        captions($('#rig-captions'), [
            ['find', 13.3, 18.6], ['qr', 18.7, 22.1], ['pair', 22.3, 28.8],
            ['move', 29.0, 34.0], ['click', 34.1, 38.6], ['scroll', 38.7, 43.3], ['drag', 43.4, 48.0], ['speed', 48.1, 51.0],
            ['type', 51.1, 56.3], ['lang', 56.4, 62.6], ['alt', 62.7, 67.4], ['media', 67.5, 73.5],
            ['full', 73.8, 78.6], ['status', 78.7, 83.1]
        ]);

        // The devices. Later the PC leaves and the phone moves to the middle and grows.
        enter('#monitor', T.rig + 0.2, { y: 90, rx: 14, scale: 0.95 }, 1.3);
        exit('#monitor', B.monitorOut, { y: -50, scale: 0.97 }, 0.7, 'inOut');
        enter('#phone', T.rig + 0.6, { y: 160 }, 1.1);
        const PHONE_DX = 24, PHONE_DY = -269, PHONE_K = 1.2;
        tw('#phone', B.monitorOut + 0.2, 1.1, { x: [0, PHONE_DX], y: [0, PHONE_DY], scale: [1, PHONE_K] }, 'inOut');
        tw('#phone', B.phoneOut, 0.6, { opacity: [1, 0], y: [PHONE_DY, PHONE_DY + 70] }, 'in');
    }

    // ---- The PC

    const pc = $('#pc');
    const ph = $('#ph');

    // Where the PC's pointer is. Before the phone takes over, it's the PC's own mouse
    // clicking Accept. The finger gestures below are derived from these same keyframes.
    const cursorAt = path([
        [13.0, 700, 430], [22.9, 700, 430], [24.1, 414, 370],
        [29.8, 414, 370], [31.0, 250, 190], [32.2, 62, 60],
        [35.3, 62, 60], [36.2, 112, 88],
        [39.0, 112, 88], [39.8, 560, 262],
        [43.8, 560, 262], [44.6, 420, 63],
        [45.6, 420, 63], [47.0, 600, 116]
    ]);
    // Windows dragged with hold-then-drag follow the pointer from where they were grabbed.
    const GRAB = [420, 63], DRAG_T = 45.6;

    {
        const cursor = $('#pc-cursor');
        every(t => {
            if (t < T.rig || t > T.tray) return;
            const [x, y] = cursorAt(t);
            cursor.style.transform = `translate(${x}px, ${y}px)`;
        });

        // QR window, then the pairing prompt with its countdown.
        makeQr($('#qr-pc'));
        makeQr($('#qr-cam'));
        enter('#w-qr', B.scanTap - 0.3, { scale: 0.92, y: 20 }, 0.5, 'back');
        exit('#w-qr', 21.7, { scale: 0.96 }, 0.3);
        enter('#w-pair', B.pair, { scale: 0.92, y: 20 }, 0.5, 'back');
        exit('#w-pair', B.accept + 0.15, { scale: 0.96 }, 0.25);
        classWhen($('#pair-accept'), 'down', [B.accept, B.accept + 0.2]);
        every(t => setText($('#pair-countdown'), `Auto-rejects in ${Math.max(1, 30 - Math.floor(t - B.pair))}s if no response.`));
        enter('#pc-toast', B.shell + 0.3, { x: 40 }, 0.5, 'back');
        exit('#pc-toast', 28.4, { x: 30 }, 0.4);

        // Click the folder, right-click it, open it.
        classWhen($('#icon-photos'), 'sel', [32.8, 36.7]);
        every(t => $('#ctx-open').classList.toggle('hot', t > 35.9 && t < 36.7));
        enter('#ctx', 34.8, { scale: 0.92 }, 0.2);
        exit('#ctx', 36.65, {}, 0.12);
        enter('#w-photos', 36.8, { scale: 0.86 }, 0.4, 'back');

        // Photos: tiles, scrolled by two fingers; then the window is dragged by its title bar.
        const tones = [['#f0b84a', '#ff5c72'], ['#4f8cf7', '#00d2a8'], ['#6c5ce7', '#4f8cf7'], ['#2ec27e', '#f0b84a'],
            ['#ff5c72', '#6c5ce7'], ['#00d2a8', '#2a3f8f'], ['#f0a93f', '#8b7cf0'], ['#1e8f9f', '#9ad0f5']];
        for (let i = 0; i < 32; i++) {
            const [a, b] = tones[(i * 5 + Math.floor(i / 4)) % tones.length];
            const tile = document.createElement('i');
            tile.style.background = `linear-gradient(${120 + (i * 37) % 120}deg, ${a}, ${b})`;
            $('#photos-grid').appendChild(tile);
        }
        every(t => {
            const scroll = 190 * ramp(t, 40.1, 1.5) + 150 * ramp(t, 41.95, 0.75);
            $('#photos-grid').style.transform = `translateY(${-scroll}px)`;
            const [cx, cy] = cursorAt(Math.max(t, DRAG_T));
            $('#w-photos').style.translate = t >= DRAG_T ? `${cx - GRAB[0]}px ${cy - GRAB[1]}px` : '';
        });

        // Notes, typed into from the phone.
        enter('#w-notes', B.keys, { scale: 0.94, y: 16 }, 0.5, 'back');

        // Alt+Tab: Photos, then Music; letting go of Alt brings Music to the front.
        const ALT = { down: 63.2, tabs: [63.6, 64.4], up: 65.3 };
        showWhen($('#alttab'), [ALT.tabs[0], ALT.up]);
        const cards = $$('.alttab-card');
        every(t => {
            const pick = t >= ALT.tabs[1] ? 2 : 1;
            cards.forEach((c, i) => c.classList.toggle('on', i === pick));
            $('#w-music').style.zIndex = t >= ALT.up ? 3 : '';
            $('#w-notes').style.zIndex = 2;
        });
        tw('#w-music', ALT.up, 0.35, { scale: [0.97, 1] }, 'back');

        // Media: Play from the keyboard's header, then Fn + F3 (Vol+) three times.
        const PLAY_T = 68.0, VOL = [70.4, 70.9, 71.4];
        every(t => {
            const playing = t >= PLAY_T;
            $('#music-play-ic use').setAttribute('href', playing ? '#i-pause' : '#i-play');
            const pos = 72 + Math.max(0, t - PLAY_T);
            setText($('#music-time'), `${Math.floor(pos / 60)}:${pad(pos % 60)}`);
            $('#music-fill').style.width = `${pos / 228 * 100}%`;
            $$('#eq i').forEach((b, i) => {
                const h = playing ? 22 + 70 * Math.abs(Math.sin(t * (4.1 + i * 1.3) + i * 1.7)) : 18;
                b.style.height = `${h}%`;
            });
            const level = 40 + 6 * VOL.filter(v => t >= v).length;
            setText($('#vol-num'), String(level));
            $('#vol-fill').style.width = `${level}%`;
        });
        enter('#media-flyout', PLAY_T + 0.1, { y: -16 }, 0.4, 'back');
        exit('#media-flyout', 69.9, { y: -10 }, 0.3);
        enter('#vol-flyout', VOL[0] - 0.05, { y: -16 }, 0.35, 'back');
        exit('#vol-flyout', 72.7, { y: -10 }, 0.3);

        // The PC follows the phone to Slovak.
        enter('#lang-flyout', 56.8, { y: 16 }, 0.4, 'back');
        exit('#lang-flyout', 58.9, { y: 10 }, 0.3);
    }

    // ---- The phone: pages and views

    showWhen($('#pg-discovery'), [0, B.scan]);
    showWhen($('#pg-scan'), [B.scan, B.shell]);
    showWhen($('#pg-shell'), [B.shell, 999]);
    showWhen($('#v-trackpad'), [0, B.keys], [B.padTap + 0.1, B.statusTap + 0.1]);
    showWhen($('#v-keyboard'), [B.keys, B.padTap + 0.1]);
    showWhen($('#v-status'), [B.statusTap + 0.1, 999]);
    classWhen($('#nav-0'), 'on', [0, B.keys], [B.padTap + 0.1, B.statusTap + 0.1]);
    classWhen($('#nav-1'), 'on', [B.keys, B.padTap + 0.1]);
    classWhen($('#nav-2'), 'on', [B.statusTap + 0.1, 999]);

    // Discovery: searching, two PCs found, an address typed by hand, then Scan QR code.
    showWhen($('#disc-empty'), [0, 15.45]);
    every(t => { $('#disc-empty .spinner').style.transform = `rotate(${t * 400}deg)`; });
    enter('#host-1', 15.4, { x: -24 }, 0.5, 'back');
    enter('#host-2', 15.7, { x: -24 }, 0.5, 'back');
    typewriter($('#ip-text'), '192.168.1.20', 16.2, 0.8);
    typewriter($('#port-text'), '45455', 17.2, 0.35);
    every(t => {
        $('#ip-hint').style.display = t >= 16.25 ? 'none' : '';
        $('#port-hint').style.display = t >= 17.25 ? 'none' : '';
    });
    classWhen($('#btn-scan'), 'down', [B.scanTap, B.scanTap + 0.2]);

    // The scanner: a slightly shaky camera finds the code on the PC.
    every(t => {
        if (t < B.scan || t > B.shell) return;
        const k = ramp(t, B.scan, 1.1);
        const shake = 1 - ramp(t, B.qrOk - 0.3, 0.4);
        $('#cam-scene').style.transform = `translate(${Math.sin(t * 5.3) * 6 * shake}px, ${Math.cos(t * 4.1) * 5 * shake}px) scale(${lerp(0.82, 1.08, k)})`;
        $('#cam-frame').classList.toggle('ok', t >= B.qrOk);
        $('#cam-line').style.transform = `translateY(${Math.sin((t - B.scan) * 4) * 96}px)`;
        $('#cam-line').style.opacity = t < B.qrOk ? 1 : 0;
    });
    tw('#cam-frame', B.qrOk, 0.3, { scale: [1.12, 1] }, 'back');
    enter('#connecting', B.connecting, {}, 0.3);
    exit('#connecting', B.shell - 0.1, {}, 0.25);
    every(t => { $('#connecting .spinner').style.transform = `rotate(${t * 400}deg)`; });
    enter('#pg-shell', B.shell, { scale: 0.98 }, 0.35);

    // Trackpad: the hint gives way to the first touch; speed slider; the three buttons.
    exit('#tp-hint', 29.6, {}, 0.4, 'out');
    const SPEED = { start: 48.6, dur: 0.8, from: 1.0, to: 1.8 };
    const speedAt = t => lerp(SPEED.from, SPEED.to, ramp(t, SPEED.start, SPEED.dur));
    every(t => {
        const v = speedAt(t);
        const f = (v - 0.5) / 2.5;
        $('#speed-fill').style.width = `${f * 100}%`;
        $('#speed-thumb').style.left = `${f * 100}%`;
        setText($('#speed-val'), `${v.toFixed(1)}x`);
    });
    const BTN_TAPS = [49.75, 50.05, 50.35];
    BTN_TAPS.forEach((at, i) => classWhen($(`#mb-${i}`), 'down', [at, at + 0.16]));

    // The rail folds away and comes back, as the collapse button does in the app.
    every(t => {
        const k = ramp(t, B.collapse + 0.1, 0.4) * (1 - ramp(t, B.expand + 0.1, 0.4));
        $('#rail').style.transform = `translateX(${-90 * k}px)`;
        $('#rail').style.opacity = 1 - k;
        $('#content').style.left = `${lerp(84, 0, k)}px`;
        $('#float-expand').style.opacity = k;
        $('#float-expand').style.visibility = k > 0.01 ? '' : 'hidden';
    });

    // Status: live numbers since the phone connected at B.shell.
    every(t => {
        if (t < B.statusTap) return;
        setText($('#st-latency'), `${3 + Math.round(Math.abs(Math.sin(t * 2.3)) * 3)} ms`);
        setText($('#st-uptime'), clock(t - B.shell + 0.5));
        setText($('#st-sent'), `${(48.2 + (t - B.statusTap) * 1.7).toFixed(1)} KB`);
        setText($('#st-recv'), `${(12.6 + (t - B.statusTap) * 0.4).toFixed(1)} KB`);
    });
    classWhen($('#btn-export'), 'down', [B.exportTap, B.exportTap + 0.2]);
    enter('#sheet', B.exportTap + 0.25, { y: 140 }, 0.5);

    // ---- The keyboard, built like KeyboardView.Build: same rows, widths and layouts.

    const LAYOUTS = {
        en: {
            label: 'EN · QWERTY',
            num: [['`', '~'], ['1', '!'], ['2', '@'], ['3', '#'], ['4', '$'], ['5', '%'], ['6', '^'], ['7', '&'], ['8', '*'], ['9', '('], ['0', ')'], ['-', '_'], ['=', '+']],
            l2: 'qwertyuiop', e2: [['[', '{'], [']', '}'], ['\\', '|']],
            l3: 'asdfghjkl', e3: [[';', ':'], ["'", '"']],
            l4: 'zxcvbnm', e4: [[',', '<'], ['.', '>'], ['/', '?']]
        },
        sk: {
            label: 'SK · QWERTZ',
            num: [[';', '°'], ['+', '1'], ['ľ', '2'], ['š', '3'], ['č', '4'], ['ť', '5'], ['ž', '6'], ['ý', '7'], ['á', '8'], ['í', '9'], ['é', '0'], ['=', '%'], ['´', 'ˇ']],
            l2: 'qwertzuiop', e2: [['ú', '/'], ['ä', '('], ['ň', ')']],
            l3: 'asdfghjkl', e3: [['ô', '"'], ['§', '!']],
            l4: 'yxcvbnm', e4: [[',', '?'], ['.', ':'], ['-', '_']]
        }
    };
    const SWITCH_T = 57.4;
    const FN_LABELS = ['Mute', 'Vol−', 'Vol+', 'Prev', 'Play', 'Next', 'Stop', 'Home', 'End', 'PgUp', 'PgDn', 'Ins'];

    const KB = (() => {
        const kb = $('#kb');
        const chars = [];   // { el, n, s, faces: { en: [n, s], sk: [n, s] }, col }
        const named = {};
        const icon = id => `<svg class="ic"><use href="#i-${id}"/></svg>`;
        const row = () => kb.appendChild(tag('div', 'kb-row'));
        const key = (r, weight, cls, html, name) => {
            const el = tag('span', `key ${cls}`, html);
            el.style.flexGrow = weight;
            r.appendChild(el);
            if (name) named[name] = el;
            return el;
        };
        const charKey = (r, col, en, sk) => {
            const el = key(r, 1, '', '<span class="key-face"><span class="n"></span><span class="s"></span></span>');
            const c = { el, face: $('.key-face', el), n: $('.n', el), s: $('.s', el), faces: { en, sk }, col };
            chars.push(c);
            return c;
        };
        const letter = ch => [ch, ch.toUpperCase()];

        const fn = row();
        key(fn, 1.4, 'mod', 'Esc');
        const fkeys = [];
        for (let i = 1; i <= 12; i++) fkeys.push(key(fn, 1, '', `F${i}`, `F${i}`));

        const r1 = row();
        LAYOUTS.en.num.forEach((c, i) => charKey(r1, i, c, LAYOUTS.sk.num[i]));
        key(r1, 1.8, '', icon('backspace'));

        const r2 = row();
        key(r2, 1.5, '', 'Tab', 'tab');
        for (let i = 0; i < 10; i++) charKey(r2, i + 1, letter(LAYOUTS.en.l2[i]), letter(LAYOUTS.sk.l2[i]));
        LAYOUTS.en.e2.forEach((c, i) => charKey(r2, 11 + i, c, LAYOUTS.sk.e2[i]));

        const r3 = row();
        key(r3, 1.7, 'mod', 'Caps');
        for (let i = 0; i < 9; i++) charKey(r3, i + 1, letter(LAYOUTS.en.l3[i]), letter(LAYOUTS.sk.l3[i]));
        LAYOUTS.en.e3.forEach((c, i) => charKey(r3, 10 + i, c, LAYOUTS.sk.e3[i]));
        key(r3, 2.0, '', icon('enter'), 'enter');

        const r4 = row();
        key(r4, 2.0, 'mod', icon('shift'), 'shift');
        for (let i = 0; i < 7; i++) charKey(r4, i + 1, letter(LAYOUTS.en.l4[i]), letter(LAYOUTS.sk.l4[i]));
        LAYOUTS.en.e4.forEach((c, i) => charKey(r4, 8 + i, c, LAYOUTS.sk.e4[i]));
        key(r4, 2.0, 'mod', icon('shift'));

        const r5 = row();
        key(r5, 1.2, 'mod', 'Fn', 'fn');
        key(r5, 1.3, 'mod', 'Ctrl');
        key(r5, 1.1, 'mod', 'Win');
        key(r5, 1.1, 'mod', 'Alt', 'alt');
        key(r5, 5, '', 'Space', 'space');
        key(r5, 1.1, 'mod', 'Alt');
        key(r5, 1.3, 'mod', 'Ctrl');
        key(r5, 1.1, '', 'Ins');
        key(r5, 1.1, '', 'Del');
        ['left', 'up', 'down', 'right'].forEach(d => key(r5, 1, '', icon(d)));

        /** The key that types ch on a layout, and whether it needs Shift. */
        function find(ch, lang) {
            if (ch === ' ') return { el: named.space, shift: false };
            for (const c of chars) {
                const [n, s] = c.faces[lang];
                if (n === ch) return { el: c.el, shift: false };
                if (s === ch) return { el: c.el, shift: true };
            }
            throw new Error(`No key for "${ch}" on ${lang}`);
        }

        return { chars, named, fkeys, find };
    })();

    // Key presses: { el, at, dur }. Each shows the key lit and a fingertip on it.
    const PRESSES = [];
    const press = (el, at, dur = 0.12) => PRESSES.push({ el, at, dur });

    // Typed text on the PC: [time, text appended].
    const TYPED = [];
    function type(text, start, step, lang) {
        [...text].forEach((ch, i) => {
            const at = start + i * step;
            const k = KB.find(ch, lang);
            if (k.shift) press(KB.named.shift, at - 0.07, 0.2);
            press(k.el, at, 0.11);
            TYPED.push([at, ch]);
        });
    }

    type('Hello from the couch!', 51.7, 0.155, 'en');
    press(KB.named.enter, 58.3);
    TYPED.push([58.3, '\n']);
    type('Ahoj z gauča', 58.65, 0.17, 'sk');
    // Holding Shift: the shifted characters light up.
    const SHIFT_HOLD = [60.9, 62.2];
    press(KB.named.shift, SHIFT_HOLD[0], SHIFT_HOLD[1] - SHIFT_HOLD[0]);
    // Alt+Tab.
    press(KB.named.alt, 63.2, 65.3 - 63.2);
    press(KB.named.tab, 63.6, 0.16);
    press(KB.named.tab, 64.4, 0.16);
    // Fn, then Vol+ (F3) three times.
    const FN_T = 69.3;
    press(KB.named.fn, FN_T, 0.12);
    [70.4, 70.9, 71.4].forEach(at => press(KB.named.F3, at, 0.14));

    every(t => {
        if (t < B.keys - 0.1 || t > B.padTap + 0.2) return;
        const lang = t < SWITCH_T ? 'en' : 'sk';
        setText($('#kb-layout'), LAYOUTS[lang].label);

        // The layout switch flips each key over, in a wave from left to right; the new face
        // shows from halfway through the flip.
        for (const c of KB.chars) {
            const k = clamp((t - (SWITCH_T - 0.25) - c.col * 0.02) / 0.4);
            const [n, s] = c.faces[k < 0.5 ? 'en' : 'sk'];
            setText(c.n, n);
            setText(c.s, s);
            c.face.style.transform = k > 0 && k < 1 ? `scaleY(${Math.abs(Math.cos(Math.PI * k)).toFixed(3)})` : '';
        }

        const down = new Set(PRESSES.filter(p => during(t, p.at, p.at + p.dur)).map(p => p.el));
        for (const el of $$('.key', $('#kb'))) el.classList.toggle('down', down.has(el));
        $('#kb').classList.toggle('shifted', down.has(KB.named.shift));

        const fnOn = t >= FN_T;
        KB.named.fn.classList.toggle('lit', fnOn);
        KB.fkeys.forEach((el, i) => {
            const k = clamp((t - FN_T - i * 0.03) / 0.2);
            setText(el, fnOn && k >= 0.5 ? FN_LABELS[i] : `F${i + 1}`);
            el.style.opacity = fnOn && k < 1 ? Math.abs(1 - 2 * k) : '';
        });
    });
    classWhen($('#kb-media-play'), 'down', [68.0, 68.16]);

    every(t => {
        let text = '';
        for (const [at, ch] of TYPED) if (t >= at) text += ch;
        setText($('#notes-text'), text);
    });

    // ---- Fingertips on the glass

    // The pointer moves G PC pixels per pixel of finger travel (with the speed slider at 1.0x).
    const G = 1.6;
    const surface = $('#surface');
    const FINGER_TWO = 26;

    /** A touch: where it is on the phone screen, how visible, its tap ripple and hold ring. */
    const touchAt = (x, y, a = 1, ring = -1, hold = -1) => ({ x, y, a, ring, hold });

    /** A finger that lands at t0, stays dur, lifts, and leaves a ripple. */
    function tapTouches(t, t0, dur, at, n = 1) {
        if (t < t0 - 0.1 || t > t0 + dur + 0.45) return [];
        const a = clamp((t - t0 + 0.1) / 0.1) * (1 - clamp((t - t0 - dur) / 0.12));
        const ring = clamp((t - t0) / 0.45);
        const [x, y] = at();
        const pts = n === 1 ? [[x, y]] : [[x - FINGER_TWO, y + 4], [x + FINGER_TWO, y - 4]];
        return pts.map(([px, py]) => touchAt(px, py, a, ring));
    }

    /** One finger on the trackpad surface moving with the pointer from a to b. */
    function dragTouches(t, a, b, start, hold = 0) {
        if (t < a - 0.1 || t > b + 0.15) return [];
        const [sx, sy] = offsetIn(surface, ph);
        const [c0x, c0y] = cursorAt(a);
        const [cx, cy] = cursorAt(t);
        const alpha = clamp((t - a + 0.1) / 0.1) * (1 - clamp((t - b) / 0.15));
        const h = hold ? clamp((t - a) / hold) : -1;
        return [touchAt(sx + start[0] + (cx - c0x) / G, sy + start[1] + (cy - c0y) / G, alpha, -1, h < 1 ? h : -1)];
    }

    /** Two fingers on the surface sliding by dy. */
    function scrollTouches(t, a, b, start, dy) {
        if (t < a - 0.1 || t > b + 0.15) return [];
        const [sx, sy] = offsetIn(surface, ph);
        const alpha = clamp((t - a + 0.1) / 0.1) * (1 - clamp((t - b) / 0.15));
        const y = sy + start[1] + dy * ramp(t, a + 0.1, b - a - 0.2);
        return [touchAt(sx + start[0] - FINGER_TWO, y + 3, alpha), touchAt(sx + start[0] + FINGER_TWO, y - 3, alpha)];
    }

    const onSurface = (x, y) => () => { const [sx, sy] = offsetIn(surface, ph); return [sx + x, sy + y]; };
    const onEl = sel => () => centerIn(typeof sel === 'string' ? $(sel) : sel, ph);

    const GESTURES = [
        // Connecting
        t => tapTouches(t, B.scanTap, 0.14, onEl('#btn-scan')),
        // Trackpad
        t => dragTouches(t, 29.6, 32.35, [560, 262]),
        t => tapTouches(t, 32.75, 0.1, onSurface(340, 75)),
        t => tapTouches(t, 34.75, 0.12, onSurface(330, 150), 2),
        t => dragTouches(t, 35.15, 36.3, [320, 140]),
        t => tapTouches(t, 36.55, 0.1, onSurface(352, 160)),
        t => dragTouches(t, 38.85, 39.95, [180, 150]),
        t => scrollTouches(t, 40.0, 41.65, [360, 250], -150),
        t => scrollTouches(t, 41.85, 42.75, [360, 240], -110),
        t => dragTouches(t, 43.65, 44.75, [360, 240]),
        t => dragTouches(t, 45.0, 47.15, [300, 150], 0.55),
        t => {
            // The speed slider's thumb, dragged.
            if (t < SPEED.start - 0.2 || t > SPEED.start + SPEED.dur + 0.25) return [];
            const thumb = $('#speed-thumb');
            const [x, y] = centerIn(thumb, ph);
            const a = clamp((t - SPEED.start + 0.2) / 0.1) * (1 - clamp((t - SPEED.start - SPEED.dur - 0.05) / 0.15));
            return [touchAt(x, y, a)];
        },
        ...BTN_TAPS.map((at, i) => t => tapTouches(t, at, 0.14, onEl(`#mb-${i}`))),
        t => tapTouches(t, B.keysTap, 0.12, onEl('#nav-1')),
        // Keyboard
        t => PRESSES.flatMap(p => tapTouches(t, p.at, p.dur, onEl(p.el))),
        t => tapTouches(t, 68.0, 0.14, onEl('#kb-media-play')),
        // Phone app
        t => tapTouches(t, B.padTap, 0.12, onEl('#nav-0')),
        t => tapTouches(t, B.collapse, 0.12, onEl('#nav-collapse')),
        t => tapTouches(t, B.expand, 0.12, onEl('#float-expand')),
        t => tapTouches(t, B.statusTap, 0.12, onEl('#nav-2')),
        t => tapTouches(t, B.exportTap, 0.14, onEl('#btn-export'))
    ];

    {
        const pool = Array.from({ length: 6 }, () => {
            const el = tag('div', 'touch', '<span class="tring"></span><span class="tdot"></span>');
            const svg = svgEl('svg', { class: 'thold', viewBox: '0 0 60 60' }, el);
            const arc = svgEl('circle', { cx: 30, cy: 30, r: 26, pathLength: 1, 'stroke-dasharray': 1 }, svg);
            $('#touches').appendChild(el);
            return { el, ring: $('.tring', el), dot: $('.tdot', el), svg, arc };
        });
        every(t => {
            const list = t >= T.rig && t < T.tray ? GESTURES.flatMap(g => g(t)) : [];
            pool.forEach((p, i) => {
                const touch = list[i];
                p.el.style.display = touch ? '' : 'none';
                if (!touch) return;
                p.el.style.transform = `translate(${touch.x.toFixed(1)}px, ${touch.y.toFixed(1)}px)`;
                p.dot.style.opacity = touch.a;
                p.ring.style.opacity = touch.ring >= 0 ? (1 - touch.ring) * 0.9 : 0;
                p.ring.style.transform = `scale(${1 + Math.max(0, touch.ring) * 0.9})`;
                p.svg.style.display = touch.hold >= 0 ? '' : 'none';
                p.arc.style.strokeDashoffset = 1 - Math.max(0, touch.hold);
            });
        });
    }

    // ------------------------------------------------------------------ 4. PC app: tray and settings

    {
        const s = T.tray;
        const end = T.grid;
        scene('#s-tray', s, end);
        captions($('#tray-captions'), [['tray', s + 0.2, s + 5.6], ['settings', s + 5.7, end - 0.3]]);
        enter('#desk', s + 0.3, { y: 80, rx: 12, scale: 0.95 }, 1.1);
        exit('#desk', end - 0.8, { y: 60, scale: 0.97 }, 0.6, 'inOut');

        const desk = $('#desk');
        const items = $$('.tm-item');
        // The pointer's keyframes are measured from the layout (offsets ignore the transforms).
        const at = el => { const [x, y] = centerIn(el, desk); return [x, y]; };
        const left = (el, dx) => { const [x, y] = offsetIn(el, desk); return [x + dx, y + el.offsetHeight / 2]; };

        enter('#tray-tip', s + 1.4, { y: 6 }, 0.2);
        exit('#tray-tip', s + 1.85, {}, 0.1);
        enter('#tray-menu', s + 1.9, { scale: 0.92, y: 10 }, 0.25, 'out');
        exit('#tray-menu', s + 5.35, {}, 0.15);
        enter('#w-settings', s + 5.5, { scale: 0.93, y: 20 }, 0.5, 'back');

        const CLICK = { auto: s + 6.5, reverse: s + 7.5, save: s + 9.7 };
        every(t => {
            if (t < s || t > end) return;
            const icon = at($('#tray-icon'));
            const it = i => left(items[i], 60);
            const cursorPath = path([
                [s, 560, 430], [s + 0.5, 560, 430], [s + 1.4, icon[0], icon[1]],
                [s + 2.4, icon[0], icon[1]], [s + 2.9, ...it(1)], [s + 3.4, ...it(2)], [s + 3.9, ...it(3)],
                [s + 4.4, ...it(4)], [s + 5.0, ...it(5)],
                [s + 5.6, ...it(5)], [s + 6.2, ...left($('#chk-auto'), 11)],
                [s + 6.8, ...left($('#chk-auto'), 11)], [s + 7.3, ...left($('#chk-reverse'), 11)],
                [s + 7.8, ...left($('#chk-reverse'), 11)], [s + 8.4, ...at($('#set-wipe'))],
                [s + 8.9, ...at($('#set-wipe'))], [s + 9.5, ...at($('#set-save'))]
            ]);
            const [x, y] = cursorPath(t);
            $('#desk-cursor').style.transform = `translate(${x}px, ${y}px)`;
            $('#tray-icon').classList.toggle('hot', t > s + 1.3 && t < s + 5.4);
            // The menu item under the pointer lights up, as in the real tray menu.
            const menuOpen = t > s + 2.1 && t < s + 5.35;
            items.forEach((el, i) => {
                const [ix, iy] = offsetIn(el, desk);
                const over = x > ix && x < ix + el.offsetWidth && y > iy && y < iy + el.offsetHeight;
                el.classList.toggle('hot', menuOpen && i > 0 && over);
            });
            $('#chk-auto').classList.toggle('on', t >= CLICK.auto);
            $('#chk-confirm').classList.add('on');
            $('#chk-reverse').classList.toggle('on', t >= CLICK.reverse);
            $('#set-wipe').style.boxShadow = t > s + 8.5 && t < s + 9.2
                ? `0 0 0 ${3 + 3 * Math.sin((t - s) * 9)}px rgba(255, 92, 114, 0.35)` : '';
            $('#set-save').classList.toggle('down', during(t, CLICK.save, CLICK.save + 0.2));
        });
    }

    // ------------------------------------------------------------------ 5. feature grid

    {
        const s = T.grid;
        const end = T.stats;
        const ICONS = ['hand', 'keyboard', 'globe', 'fn', 'music', 'radar', 'qr', 'shield', 'pulse', 'tray', 'timer', 'lock'];
        scene('#s-grid', s, end);
        const grid = $('#feature-grid');
        S.features.forEach(([title, line], i) => {
            const card = tag('article', 'card', `<span class="card-icon"><svg class="ic"><use href="#i-${ICONS[i]}"/></svg></span><h3></h3><p></p>`);
            $('h3', card).textContent = title;
            $('p', card).textContent = line;
            grid.appendChild(card);
        });
        enter('#s-grid .section-head', s + 0.1, { y: 30 }, 0.8);
        exit('#s-grid .section-head', end - 0.9, { y: -30 }, 0.5);
        stagger('#feature-grid .card', s + 0.5, 0.08, (el, at) => enter(el, at, { y: 40, scale: 0.94 }, 0.6, 'back'));
        stagger('#feature-grid .card', end - 0.9, 0.03, (el, at) => exit(el, at, { y: -20, scale: 0.97 }, 0.4));
        // A highlight sweeps across the grid once every card is in, blending from the card's
        // resting hairline and fill to the accent and back.
        const mix = (k, a) => `rgba(${lerp(255, 108, k)}, ${lerp(255, 92, k)}, ${lerp(255, 231, k)}, ${lerp(a[0], a[1], k)})`;
        every(t => {
            const cards = grid.children;
            for (let i = 0; i < cards.length; i++) {
                const d = Math.abs((t - s - 2.2) * 2.5 - 1.5 - i);
                const k = t > s + 2.2 && t < s + 8.6 ? E.inOut(clamp(1 - d / 1.5)) : 0;
                cards[i].style.borderColor = k ? mix(k, [0.08, 0.7]) : '';
                cards[i].style.background = k ? mix(k, [0.03, 0.14]) : '';
            }
        });
    }

    // ------------------------------------------------------------------ 6. stats

    {
        const s = T.stats;
        const end = T.outro;
        scene('#s-stats', s, end);
        stagger('.stat', s + 0.2, 0.15, (el, at) => enter(el, at, { y: 50, scale: 0.94 }, 0.7, 'back'));
        stagger('.req', s + 1.4, 0.15, (el, at) => enter(el, at, { y: 30 }, 0.6, 'back'));
        stagger(['.stats', '#reqs'].map(x => $(x)), end - 0.7, 0.08, (el, at) => exit(el, at, { y: -30, blur: 6 }, 0.5));
        // The two zeros count down to nothing.
        every(t => {
            if (t < s || t > end) return;
            setText($('#stat-accounts'), String(Math.round(3 * (1 - ramp(t, s + 0.8, 0.9, 'out')))));
            setText($('#stat-cables'), String(Math.round(3 * (1 - ramp(t, s + 0.95, 0.9, 'out')))));
        });
    }

    // ------------------------------------------------------------------ 7. outro

    {
        const s = T.outro;
        scene('#s-outro', s, DURATION + 1);
        // The logo punches in on the drop.
        enter('#outro-logo', s + 0.1, { scale: 0.6, blur: 14 }, 0.6, 'back');
        enter('#outro-title', s + 0.8, { y: 40 }, 0.8);
        enter('#outro-sub', s + 1.3, { y: 30 }, 0.8);
        enter('#outro-cta', s + 1.8, { y: 30, scale: 0.9 }, 0.8, 'back');
        enter('#outro-url', s + 2.3, { y: 20 }, 0.8);
    }

    enter('#watermark', T.rig + 0.4, { y: 20 }, 0.6);
    exit('#watermark', T.outro - 0.6, {}, 0.4);
    tw('#fade-out', DURATION - 1.2, 1.2, { opacity: [0, 1] }, 'inOut');

    // ------------------------------------------------------------------ player

    const stage = $('#stage');
    function fit() {
        const k = Math.min(innerWidth / W, innerHeight / H);
        stage.style.transform = `scale(${k}) translate(-50%, -50%)`;
        stage.style.transformOrigin = '0 0';
    }
    addEventListener('resize', fit);
    fit();

    // The language button reloads the page with the other language.
    const link = (key, value) => {
        const p = new URLSearchParams(location.search);
        p.set(key, value);
        return `?${p}`;
    };
    $('#btn-lang').textContent = LANG === 'en' ? 'SK' : 'EN';
    $('#btn-lang').href = link('lang', LANG === 'en' ? 'sk' : 'en');

    const scrub = $('#scrub');
    const timeLabel = $('#time');
    const mmss = x => `${Math.floor(x / 60)}:${pad(x % 60)}`;
    let t = 0, playing = false, last = 0, onEnd = null;

    // The music follows the video clock: it is moved to t on every seek, and pulled back when
    // it drifts. Its volume carries the same fade-out render.mjs mixes into the MP4.
    const music = $('#music');
    music.src = MUSIC.src;

    function show(x) {
        t = clamp(x, 0, DURATION);
        render(t);
        scrub.value = t / DURATION * 1000;
        timeLabel.textContent = `${mmss(t)} / ${mmss(DURATION)}`;
        music.volume = 1 - ramp(t, DURATION - MUSIC.fadeOut, MUSIC.fadeOut, 'linear');
    }

    function seek(x) {
        show(x);
        if (playing) music.currentTime = t;
    }

    function frame(now) {
        if (!playing) return;
        t += (now - last) / 1000;
        last = now;
        if (t >= DURATION) { show(DURATION); setPlaying(false); onEnd?.(); return; }
        if (!music.paused && Math.abs(music.currentTime - t) > 0.15) music.currentTime = t;
        show(t);
        requestAnimationFrame(frame);
    }

    function setPlaying(on) {
        if (on && t >= DURATION) show(0);
        playing = on;
        document.body.classList.toggle('playing', on);
        if (on) {
            music.currentTime = t;
            music.play().catch(() => { });
            last = performance.now();
            requestAnimationFrame(frame);
        } else {
            music.pause();
        }
    }

    $('#btn-mute').onclick = () => {
        music.muted = !music.muted;
        document.body.classList.toggle('muted', music.muted);
    };
    $('#btn-play').onclick = () => setPlaying(!playing);
    $('#btn-restart').onclick = () => { seek(0); setPlaying(true); };
    $('#btn-full').onclick = () => document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen();
    scrub.oninput = () => seek(scrub.value / 1000 * DURATION);

    addEventListener('keydown', e => {
        if (e.code === 'Space') { e.preventDefault(); setPlaying(!playing); }
        else if (e.key === 'r') $('#btn-restart').onclick();
        else if (e.key === 'f') $('#btn-full').onclick();
        else if (e.key === 'm') $('#btn-mute').onclick();
        else if (e.key === 'ArrowRight') seek(t + 5);
        else if (e.key === 'ArrowLeft') seek(t - 5);
    });

    // Controls fade out while playing and the mouse is still.
    let idleTimer;
    addEventListener('mousemove', () => {
        document.body.classList.remove('idle');
        clearTimeout(idleTimer);
        idleTimer = setTimeout(() => playing && document.body.classList.add('idle'), 1800);
    });

    // Real-time recording of the stage through screen capture, cropped to the stage where the
    // browser supports Region Capture. render.mjs gives frame-exact output; this needs nothing.
    $('#btn-record').onclick = async () => {
        let stream;
        try {
            stream = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: 60 }, audio: true, preferCurrentTab: true });
        } catch { return; }
        const [track] = stream.getVideoTracks();
        if (window.CropTarget && track.cropTo) await track.cropTo(await CropTarget.fromElement(stage));
        const type = MediaRecorder.isTypeSupported('video/webm;codecs=vp9') ? 'video/webm;codecs=vp9' : 'video/webm';
        const rec = new MediaRecorder(stream, { mimeType: type, videoBitsPerSecond: 16_000_000 });
        const chunks = [];
        rec.ondataavailable = e => chunks.push(e.data);
        rec.onstop = () => {
            stream.getTracks().forEach(x => x.stop());
            document.body.classList.remove('recording', 'clean');
            const a = document.createElement('a');
            a.href = URL.createObjectURL(new Blob(chunks, { type: 'video/webm' }));
            a.download = `remotekm-promo-${LANG}.webm`;
            a.click();
        };
        document.body.classList.add('recording', 'clean');
        seek(0);
        await new Promise(r => setTimeout(r, 500));
        rec.start();
        onEnd = () => { onEnd = null; setTimeout(() => rec.stop(), 600); };
        setPlaying(true);
    };

    // Hooks for render.mjs.
    window.__duration = DURATION;
    window.__size = [W, H];
    window.__seek = x => show(x);
    window.__music = MUSIC;

    if (params.has('clean')) document.body.classList.add('clean');
    seek(params.has('t') ? Number(params.get('t')) : 0);
    document.fonts.ready.then(() => {
        seek(t);
        if (params.has('autoplay')) setPlaying(true);
    });
})();
