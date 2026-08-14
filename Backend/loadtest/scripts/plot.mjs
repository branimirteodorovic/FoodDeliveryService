// Re-plots the published graphs from the published summaries.
//
// Route 2 of `docs/assets/loadtest/README.md`: Grafana can export a panel only while the run is still
// inside Prometheus' 7-day retention, and that retention sits on a volume `docker-compose.yml` calls
// disposable. The summaries in `results/published/` do not expire, so the graphs the README and
// `docs/load-testing.md` embed are drawn from *those* — which also means a reader can regenerate every
// picture in the documentation without a running stack, a Grafana, or this repository's author.
//
// No dependencies and no network: `node scripts/plot.mjs` from `loadtest/`. Output is deterministic —
// same inputs, byte-identical SVG — so a regeneration that changes a file means the data changed.
//
// These are the *client-side* numbers, which is all a k6 summary holds. The platform's own series
// (per-service p95, outbox backlog, container CPU) come from the `fds-load` dashboard and the
// sampler; where a number here needs one of those to be understood, `docs/load-testing.md` says so in
// prose rather than this script inventing a series it does not have.

import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const PUBLISHED = join(HERE, '..', 'results', 'published');
const OUT = join(HERE, '..', '..', 'docs', 'assets', 'loadtest');

// ── Palette ───────────────────────────────────────────────────────────────────────────────────
//
// An explicit white ground and dark ink, not `currentColor`: GitHub renders a README's SVG in both
// its light and its dark theme, and a chart that inherits the theme is unreadable in one of them.

const INK = '#1f2328';
const MUTED = '#656d76';
const GRID = '#d0d7de';
const BG = '#ffffff';

const BEFORE = '#cf222e';
const MIDDLE = '#bf8700';
const AFTER = '#1a7f37';
const SLO = '#8250df';

const FONT = 'ui-sans-serif, -apple-system, "Segoe UI", Helvetica, Arial, sans-serif';

// ── Reading a summary ─────────────────────────────────────────────────────────────────────────
//
// Every file used here was written by `handleSummary()` (Milestone E), so the statistics live under
// `metrics[name].values`. The five pre-Milestone-E files in the same directory keep the flat
// `--summary-export` shape and are deliberately not regenerated — `results/published/README.md` says
// why — so nothing below reads one.

const summary = (name) => JSON.parse(readFileSync(join(PUBLISHED, `${name}.summary.json`), 'utf8'));

const values = (run, metric) => run.metrics[metric]?.values ?? {};

/** The eight held steps of a `ramp`. The ramp-in between them is tagged `phase:tr` and gated by nothing. */
const STEPS = ['s01', 's02', 's03', 's04', 's05', 's06', 's07', 's08'];

/** `RAMP_STEPS=1,2,4,6,8,10,13,16` at the default `RATE=2` — customer arrivals per second. */
const RATES = [2, 4, 8, 12, 16, 20, 26, 32];

const step = (run, phase) => ({
    served: values(run, `http_req_duration{scope:journey,phase:${phase}}`).count ?? 0,
    p95: values(run, `http_req_duration{scope:journey,phase:${phase}}`)['p(95)'] ?? 0,
    errors: (values(run, `http_req_failed{phase:${phase}}`).rate ?? 0) * 100,
    shed: (values(run, `requests_throttled{phase:${phase}}`).rate ?? 0) * 100,
});

// ── SVG primitives ────────────────────────────────────────────────────────────────────────────

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

const text = (x, y, s, { size = 12, fill = INK, anchor = 'start', weight = 400 } = {}) =>
    `<text x="${r(x)}" y="${r(y)}" font-family="${FONT}" font-size="${size}" fill="${fill}" ` +
    `text-anchor="${anchor}" font-weight="${weight}">${esc(s)}</text>`;

const rect = (x, y, w, h, fill, extra = '') =>
    `<rect x="${r(x)}" y="${r(y)}" width="${r(Math.max(w, 0))}" height="${r(Math.max(h, 0))}" fill="${fill}"${extra}/>`;

const line = (x1, y1, x2, y2, stroke, width = 1, dash = '') =>
    `<line x1="${r(x1)}" y1="${r(y1)}" x2="${r(x2)}" y2="${r(y2)}" stroke="${stroke}" ` +
    `stroke-width="${width}"${dash ? ` stroke-dasharray="${dash}"` : ''}/>`;

/** Two decimals, trailing zeros dropped — keeps the output stable and the file small. */
const r = (n) => Number(n.toFixed(2)).toString();

/** A legend row, drawn from the plot's top-right corner leftwards. */
function legend(x, y, entries) {
    let out = '';
    let cursor = x;
    for (const { label, color } of entries) {
        out += rect(cursor, y - 8, 10, 10, color);
        out += text(cursor + 15, y, label, { size: 11, fill: MUTED });
        cursor += 15 + label.length * 6.1 + 18;
    }
    return out;
}

function svg(width, height, body) {
    return (
        `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}" width="${width}" ` +
        `height="${height}" role="img">\n` +
        rect(0, 0, width, height, BG) +
        '\n' +
        body +
        '\n</svg>\n'
    );
}

/**
 * A grouped-bar panel with a linear or log10 y axis.
 *
 * Log is not decoration on chart 1: the unguarded run's last step is 14.4 s next to a 41 ms step, and
 * a linear axis renders every step below the cliff as a flat line at zero — which hides the thing the
 * chart is about.
 */
function panel({ x, y, w, h, series, labels, yMax, yMin = 0, log = false, ticks, format, rule }) {
    const scale = (v) => {
        if (!log) return y + h - ((v - yMin) / (yMax - yMin)) * h;
        const lo = Math.log10(yMin || 1);
        const clamped = Math.max(v, yMin || 1);
        return y + h - ((Math.log10(clamped) - lo) / (Math.log10(yMax) - lo)) * h;
    };

    let out = '';

    for (const t of ticks) {
        const ty = scale(t);
        out += line(x, ty, x + w, ty, GRID, 1);
        out += text(x - 8, ty + 4, format(t), { size: 11, fill: MUTED, anchor: 'end' });
    }

    if (rule) {
        const ry = scale(rule.at);
        out += line(x, ry, x + w, ry, rule.color ?? SLO, 1.5, '5 4');
        // Left-anchored: the right edge is where the last step's annotation lands, and on the chart
        // that matters the last step is the whole point.
        out += text(x + 6, ry - 6, rule.label, { size: 11, fill: rule.color ?? SLO, anchor: 'start' });
    }

    const slot = w / labels.length;
    const groupWidth = slot * 0.62;
    const barWidth = groupWidth / series.length;

    labels.forEach((label, i) => {
        const left = x + slot * i + (slot - groupWidth) / 2;
        series.forEach((s, j) => {
            const v = s.data[i];
            const bx = left + barWidth * j;
            const top = scale(v);
            out += rect(bx, top, barWidth - 1.5, y + h - top, s.color);
            if (s.annotate?.(v, i)) {
                out += text(bx + (barWidth - 1.5) / 2, top - 5, s.annotate(v, i), {
                    size: 10,
                    fill: s.color,
                    anchor: 'middle',
                    weight: 600,
                });
            }
        });
        out += text(x + slot * i + slot / 2, y + h + 16, label, { size: 11, fill: MUTED, anchor: 'middle' });
    });

    out += line(x, y + h, x + w, y + h, INK, 1);
    return out;
}

const ms = (v) => (v >= 1000 ? `${(v / 1000).toFixed(v >= 10000 ? 1 : 2)} s` : `${Math.round(v)} ms`);

// ── 1. The cliff and the plateau ──────────────────────────────────────────────────────────────
//
// The one the plan calls "the single best artifact this whole feature produces": the same ramp, on the
// same machine, with the Gateway's admission control as the only variable. Requests *served* on top,
// because throughput falling while offered load rises is the definition of the cliff and it is visible
// in the bar heights alone; latency underneath, because the plateau's whole claim is that the requests
// which were admitted still got the same half-second they got two steps earlier.

function cliffVsPlateau() {
    const before = summary('mixed.js-ramp-g-before-01');
    const after = summary('mixed.js-ramp-g-after-01');

    const b = STEPS.map((s) => step(before, s));
    const a = STEPS.map((s) => step(after, s));
    const labels = RATES.map((rate) => `${rate}/s`);

    const W = 900;
    const H = 520;
    const X = 74;
    const PW = W - X - 26;

    let body = text(X - 46, 34, 'The cliff and the plateau', { size: 17, weight: 600 });
    body += text(
        X - 46,
        54,
        'One ramp, run twice: 2 → 32 customers/s, 90 s per step, the Gateway’s admission control the only difference',
        { size: 12, fill: MUTED },
    );
    body += legend(X - 46, 76, [
        { label: 'no limiter (g-before-01)', color: BEFORE },
        { label: 'limiter on (g-after-01)', color: AFTER },
    ]);

    body += text(X - 46, 108, 'Requests served in the step', { size: 12, weight: 600 });
    body += panel({
        x: X,
        y: 118,
        w: PW,
        h: 150,
        labels,
        series: [
            {
                color: BEFORE,
                data: b.map((s) => s.served),
                annotate: (v, i) => (i === 7 ? v.toLocaleString('en-US') : ''),
            },
            {
                color: AFTER,
                data: a.map((s) => s.served),
                annotate: (v, i) => (i === 7 ? v.toLocaleString('en-US') : ''),
            },
        ],
        yMax: 18000,
        ticks: [0, 6000, 12000, 18000],
        format: (t) => (t === 0 ? '0' : `${t / 1000}k`),
    });

    body += text(X - 46, 320, 'Journey p95 (log scale)', { size: 12, weight: 600 });
    body += panel({
        x: X,
        y: 330,
        w: PW,
        h: 150,
        labels,
        series: [
            {
                color: BEFORE,
                data: b.map((s) => s.p95),
                annotate: (v, i) => (i === 7 ? ms(v) : ''),
            },
            {
                color: AFTER,
                data: a.map((s) => s.p95),
                annotate: (v, i) => (i === 7 ? ms(v) : ''),
            },
        ],
        yMin: 10,
        yMax: 20000,
        log: true,
        ticks: [10, 100, 1000, 10000],
        format: (t) => (t >= 1000 ? `${t / 1000} s` : `${t} ms`),
        rule: { at: 500, label: '500 ms SLO' },
    });

    body += text(X - 46, H - 12, 'compose · 8 vCPU · generator co-located · 1 replica per service · loadtest/results/published/', {
        size: 11,
        fill: MUTED,
    });

    return svg(W, H, body);
}

// ── 2. Round one, change by change ────────────────────────────────────────────────────────────
//
// The three `f-*` runs are one controlled sequence and mean nothing apart. p99 and the order path are
// on the chart next to p95 because they are where the story is: the headline percentile barely moved
// while the tail halved, and a chart showing only p95 would make round one look like it did nothing.

function roundOne() {
    const runs = [
        { label: 'before', file: 'mixed.js-ramp-f-before-02', color: BEFORE },
        { label: '+ event pipeline', file: 'mixed.js-ramp-f-pipeline-01', color: MIDDLE },
        { label: '+ bounded pools', file: 'mixed.js-ramp-f-pools-01', color: AFTER },
    ].map((run) => {
        const s = summary(run.file);
        return {
            ...run,
            data: [
                values(s, 'http_req_duration{scope:journey}')['p(95)'],
                values(s, 'http_req_duration{scope:journey}')['p(99)'],
                values(s, 'order_placement_duration')['p(95)'],
            ],
        };
    });

    const W = 900;
    const H = 400;
    const X = 74;
    const PW = W - X - 26;

    let body = text(X - 46, 34, 'Round one: what the measured fixes moved', { size: 17, weight: 600 });
    body += text(
        X - 46,
        54,
        'Same ramp (RAMP_STEPS=10,13,16,20,25), same machine, same afternoon — one change at a time',
        { size: 12, fill: MUTED },
    );
    body += legend(X - 46, 76, runs.map(({ label, color }) => ({ label, color })));

    body += panel({
        x: X,
        y: 104,
        w: PW,
        h: 210,
        labels: ['journey p95', 'journey p99', 'POST /orders p95'],
        series: runs.map((run) => ({
            color: run.color,
            data: run.data,
            annotate: (v) => ms(v),
        })),
        yMax: 2500,
        ticks: [0, 500, 1000, 1500, 2000, 2500],
        format: (t) => (t >= 1000 ? `${t / 1000} s` : `${t} ms`),
    });

    body += text(
        X - 46,
        H - 40,
        'Latency was never the point: the before run failed 0.40% of requests and 2.09% of order placements; the after run failed none',
        { size: 11, fill: MUTED },
    );
    body += text(
        X - 46,
        H - 22,
        'and completed more work on the same eight cores. The tail — p99, and the write path — is where bounded pools show up.',
        { size: 11, fill: MUTED },
    );
    body += text(X - 46, H - 4, '678 × PostgresException 53300 before · 0 after · loadtest/results/published/', {
        size: 11,
        fill: MUTED,
    });

    return svg(W, H, body);
}

// ── 3. How overload gets expressed ────────────────────────────────────────────────────────────
//
// The same overload, said two ways. Without admission control a saturated platform expresses itself as
// failure — a third of the top step, indistinguishable from an outage. With it, the same saturation is
// a stated refusal: one request in twenty, deliberate, with a Retry-After on it. Both bars are the
// platform being over capacity; only one of them is something a client can respond to.

function overloadExpressed() {
    const before = summary('mixed.js-ramp-g-before-01');
    const after = summary('mixed.js-ramp-g-after-01');

    const b = STEPS.map((s) => step(before, s));
    const a = STEPS.map((s) => step(after, s));
    const labels = RATES.map((rate) => `${rate}/s`);

    const W = 900;
    const H = 540;
    const X = 74;
    const PW = W - X - 26;

    let body = text(X - 46, 34, 'How overload gets expressed', { size: 17, weight: 600 });
    body += text(X - 46, 54, 'Share of requests refused at each step, and what the refusal was', {
        size: 12,
        fill: MUTED,
    });

    // Two panels rather than one, because a shared axis is the misleading choice here: 4.99% next to
    // 32.38% renders the entire guarded run as a flat line at zero, which is the opposite of the
    // point. The scales differ by 6× and both say so on the axis and in the caption.
    body += text(X - 46, 88, 'Without the limiter — requests that failed', { size: 12, weight: 600, fill: BEFORE });
    body += panel({
        x: X,
        y: 98,
        w: PW,
        h: 140,
        labels,
        series: [
            { color: BEFORE, data: b.map((s) => s.errors), annotate: (v) => (v > 1 ? `${v.toFixed(1)}%` : '') },
        ],
        yMax: 35,
        ticks: [0, 10, 20, 30],
        format: (t) => `${t}%`,
    });

    body += text(X - 46, 292, 'With the limiter — requests shed, and requests that failed', {
        size: 12,
        weight: 600,
        fill: AFTER,
    });
    body += legend(X + 300, 292, [
        { label: 'shed (429)', color: AFTER },
        { label: 'failed', color: MIDDLE },
    ]);
    body += panel({
        x: X,
        y: 302,
        w: PW,
        h: 140,
        labels,
        series: [
            { color: AFTER, data: a.map((s) => s.shed), annotate: (v) => (v > 0.2 ? `${v.toFixed(2)}%` : '') },
            { color: MIDDLE, data: a.map((s) => s.errors), annotate: () => '' },
        ],
        yMax: 6,
        ticks: [0, 2, 4, 6],
        format: (t) => `${t}%`,
    });

    body += text(X - 46, H - 58, 'Note the axes: the top panel runs to 35%, the bottom to 6%.', {
        size: 11,
        fill: MUTED,
    });
    body += text(
        X - 46,
        H - 40,
        'A 429 is deliberately not counted as a failure — it is an answer, in microseconds, with a Retry-After on it.',
        { size: 11, fill: MUTED },
    );
    body += text(
        X - 46,
        H - 22,
        'Every one of the 1,581 rejections was a read or a non-critical write — zero order or delivery lifecycle transitions, zero health probes.',
        { size: 11, fill: MUTED },
    );
    body += text(X - 46, H - 4, 'compose · 8 vCPU · generator co-located · loadtest/results/published/', {
        size: 11,
        fill: MUTED,
    });

    return svg(W, H, body);
}

// ── Run ───────────────────────────────────────────────────────────────────────────────────────

const charts = [
    ['knee-cliff-vs-plateau.svg', cliffVsPlateau],
    ['round-one-fixes.svg', roundOne],
    ['overload-expressed.svg', overloadExpressed],
];

mkdirSync(OUT, { recursive: true });
for (const [name, draw] of charts) {
    writeFileSync(join(OUT, name), draw(), 'utf8');
    console.log(`wrote docs/assets/loadtest/${name}`);
}
