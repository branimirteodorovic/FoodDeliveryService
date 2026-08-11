// What a run leaves behind — Milestone E.
//
// Two destinations, and they answer different questions:
//
//   Prometheus (live)     `-o experimental-prometheus-rw`, wired by the runner scripts. Watch a run
//                         while it happens, next to the platform's own RED, business and cache
//                         panels, on the `fds-load` dashboard. Prometheus keeps **7 days** on a
//                         volume `docker-compose.yml` calls disposable, so this is the view, not the
//                         record.
//   results/ (durable)    this file. `handleSummary` writes the run's numbers to disk as JSON and as
//                         a markdown table, at the moment they exist. Everything Milestone H quotes
//                         has to come from here, because the graphs behind it will have expired.
//
// ── Why this replaces k6's default terminal summary ──────────────────────────────────────────────
//
// Exporting `handleSummary` turns k6's own end-of-test summary off; there is no way to have both.
// That is not a cost being absorbed, it is the reason this exists: the default prints every metric
// k6 holds — dozens of lines, alphabetically — and buries the four that decide whether a run is
// usable. What is printed instead is the run's own report: requests, the journey percentiles, the
// business counters, the per-phase table that identifies the knee, and every threshold with its
// measured value in the exact format `README.md` tells a reader to look for.
//
// It is also the reason `--summary-export` is gone from the runner scripts. It was deprecated, it
// wrote one file, and it could not have written the markdown.

import { environment, runId } from './environments.js';
import { profile } from './profiles.js';
import { SCOPE_AUTH, SCOPE_JOURNEY } from './thresholds.js';

/**
 * Options every script folds in — the two that decide what the *output* of a run looks like, kept
 * next to the code that reads them rather than repeated seven times.
 */
export const OUTPUT_OPTIONS = {
    /**
     * The tags k6 is allowed to attach to a metric — a **allow-list**, because two of the defaults
     * are unbounded and Milestone E ships every one of them into the platform's own Prometheus as a
     * label.
     *
     * Dropped, and why each one:
     *   `url`      the *full* URL, including every restaurant and order id. `lib/http.js` already
     *              refuses a request without an explicit `name`, precisely so the endpoint has a
     *              bounded identity — and then k6 tags the same request with the raw URL anyway. One
     *              ramp would create a time series per id, which is the exact failure `CLAUDE.md`
     *              names for the server side.
     *   `error`    the error *message*. Free text from the network stack, unbounded by construction.
     *              `error_code` is kept: it is a small integer enumeration and it says as much.
     *   `proto`, `subproto`, `tls_version`, `service`
     *              constant across this whole harness, so they cost a label and carry no signal.
     *
     * Kept: `name` (the endpoint), `scope` and `phase` are custom tags — this list only governs the
     * system ones, custom tags are always kept.
     */
    systemTags: ['status', 'method', 'name', 'group', 'check', 'error_code', 'scenario', 'expected_response'],

    /**
     * The statistics the summary carries per trend metric.
     *
     * Two additions to k6's default (`avg,min,med,max,p(90),p(95)`) and both are load-bearing:
     * **`p(99)`**, which the shared SLO has a threshold on and the README quotes; and **`count`**,
     * the sample count behind a percentile. A phase that never ran has `p(95)=0` and passes its
     * threshold trivially — reading the count next to it is the only way to tell that apart from a
     * fast step, and *Reading a ramp* in the README tells people to do exactly that.
     */
    summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
};

/**
 * The `handleSummary` for a script.
 *
 * @param {string} script the script's own name, e.g. `'scenarios/mixed.js'` — it names the artifacts
 *   and heads the report, and k6 does not tell a summary which script produced it.
 * @param {object} [options]
 * @param {boolean} [options.profiled] whether this script is actually driven by `config/profiles.js`
 *   — true for `mixed.js` and nothing else. A profile shapes the *mix*; a single-journey script keeps
 *   its own `vus`/`duration` and `lib/fixtures.js` warns when one is passed anyway. Printing the
 *   ramp's stages over a flat run would put that same misleading claim in a file, which outlives the
 *   warning by months.
 */
export function summaryFor(script, { profiled = false } = {}) {
    return function handleSummary(data) {
        const base = artifactBase(script);

        try {
            const report = build(script, data, base, profiled);

            return {
                stdout: `\n${renderText(report)}\n`,
                [`${base}.summary.json`]: JSON.stringify(data, null, 2),
                [`${base}.summary.md`]: renderMarkdown(report),
            };
        } catch (error) {
            // A run that finished is worth more than a report that rendered. Anything thrown in here
            // would otherwise cost the whole summary — the numbers exist only in this callback, and
            // k6 has already torn the test down by the time it is called.
            return {
                stdout: `\nsummary rendering failed: ${error}\n(the raw summary is still at ${base}.summary.json)\n`,
                [`${base}.summary.json`]: JSON.stringify(data, null, 2),
            };
        }
    };
}

/**
 * Where the artifacts go, without an extension.
 *
 * The runner scripts always pass `SUMMARY_BASE`, and inside the container they have to: k6's working
 * directory is `/home/k6`, not the mounted `/loadtest`, so a relative path would write the run's only
 * durable record into a container that is deleted three seconds later (`--rm`). The fallback is for a
 * bare `k6 run` from the `loadtest/` directory.
 */
function artifactBase(script) {
    if (__ENV.SUMMARY_BASE) {
        return __ENV.SUMMARY_BASE;
    }

    const name = String(script).split('/').pop();

    return `results/${name}-${profile.name}-${runId}`;
}

/** Everything both renderers need, pulled out of k6's summary exactly once. */
function build(script, data, base, profiled) {
    const metrics = data.metrics || {};

    return {
        script,
        base,
        runId,
        environment,
        profile: profiled ? profile : null,
        durationMs: (data.state && data.state.testRunDurationMs) || 0,
        headline: headline(metrics),
        phases: profiled && profile.tagPhases ? phases(metrics) : [],
        thresholds: thresholds(metrics),
    };
}

function headline(metrics) {
    const journey = `http_req_duration{scope:${SCOPE_JOURNEY}}`;
    const auth = `http_req_duration{scope:${SCOPE_AUTH}}`;

    return {
        requests: count(metrics, 'http_reqs'),
        requestsPerSecond: value(metrics, 'http_reqs', 'rate'),
        failureRate: value(metrics, 'http_req_failed', 'rate'),
        checkRate: value(metrics, 'checks', 'rate'),
        checksPassed: value(metrics, 'checks', 'passes'),
        checksFailed: value(metrics, 'checks', 'fails'),
        iterations: count(metrics, 'iterations'),
        // Absent from the summary entirely when nothing was dropped, which is the good case — and
        // the one place a `0` is more informative than a blank, so it is defaulted rather than hidden.
        droppedIterations: count(metrics, 'dropped_iterations'),

        journeyP95: value(metrics, journey, 'p(95)'),
        journeyP99: value(metrics, journey, 'p(99)'),
        journeyMedian: value(metrics, journey, 'med'),
        journeyMax: value(metrics, journey, 'max'),
        journeySamples: value(metrics, journey, 'count'),
        authP95: value(metrics, auth, 'p(95)'),
        authSamples: value(metrics, auth, 'count'),

        // The journey-level custom metrics. Every one of them is `null` when the script that records
        // it did not run, which is why the renderers skip nulls instead of printing zeros: a `0`
        // under "orders placed" reads as a failed write path rather than as a browse-only run.
        orderPlacementP95: value(metrics, 'order_placement_duration', 'p(95)'),
        orderPlacementFailureRate: value(metrics, 'order_placement_failures', 'rate'),
        ordersPlaced: value(metrics, 'orders_placed', 'count'),
        idempotencyReplays: value(metrics, 'order_idempotency_replays', 'count'),
        kitchenTransitions: value(metrics, 'kitchen_transitions', 'count'),
        driverClaimsWon: value(metrics, 'driver_claims_won', 'count'),
        deliveriesCompleted: value(metrics, 'deliveries_completed', 'count'),
        trackPolls: value(metrics, 'track_polls', 'count'),
    };
}

/**
 * The per-phase table — the plan's saturation rule, read straight down a column.
 *
 * The sub-metric names are rebuilt from the same profile definition that declared the thresholds, so
 * the two cannot drift: if `config/profiles.js` renames a phase, this table follows it or shows a
 * dash, and a dash means the threshold is gone too.
 */
function phases(metrics) {
    return profile.steps.map((step) => {
        const journey = `http_req_duration{scope:${SCOPE_JOURNEY},phase:${step.label}}`;

        return {
            label: step.label,
            rate: step.rate,
            from: step.holdStart,
            to: step.holdEnd,
            p95: value(metrics, journey, 'p(95)'),
            samples: value(metrics, journey, 'count'),
            errorRate: value(metrics, `http_req_failed{phase:${step.label}}`, 'rate'),
            authP95: value(metrics, `http_req_duration{scope:${SCOPE_AUTH},phase:${step.label}}`, 'p(95)'),
            budgetP95: step.journeyP95,
            budgetErrorRate: step.errorRate,
        };
    });
}

/**
 * Every threshold with the value it was judged on.
 *
 * k6 records whether a threshold passed but not what it measured, so the statistic is parsed back
 * out of the expression (`p(95)<500` → `p(95)`) and looked up on the metric. That is what produces
 * the `✗ 'p(95)<500' p(95)=642.03ms` line the README teaches people to read.
 */
function thresholds(metrics) {
    const results = [];

    for (const [name, metric] of Object.entries(metrics)) {
        for (const [source, outcome] of Object.entries(metric.thresholds || {})) {
            const stat = /^\s*(p\(\s*[\d.]+\s*\)|avg|min|max|med|count|rate|value)/.exec(source);

            results.push({
                metric: name,
                source,
                ok: Boolean(outcome && outcome.ok),
                stat: stat ? stat[1].replace(/\s/g, '') : null,
                value: stat ? value(metrics, name, stat[1].replace(/\s/g, '')) : null,
                time: (metric.contains || '') === 'time',
            });
        }
    }

    // Breaches first: they are the reason anyone reads this section, and on a ramp there are forty
    // lines above them.
    return results.sort((a, b) => Number(a.ok) - Number(b.ok));
}

// ── Renderers ─────────────────────────────────────────────────────────────────────────────────

function renderText(report) {
    const lines = [];
    const failed = report.thresholds.filter((threshold) => !threshold.ok);

    lines.push('═'.repeat(96));
    lines.push(
        `  ${report.script}${report.profile ? ` · profile '${report.profile.name}'` : ''} · ` +
            `run '${report.runId}' · env '${report.environment.name}'`
    );
    lines.push(
        `  ${report.profile ? `${report.profile.shape} · ` : ''}` +
            `${clock(report.durationMs / 1000)} wall clock · ${report.environment.gateway}`
    );
    lines.push('═'.repeat(96));

    lines.push('', 'TRAFFIC');
    lines.push(...pairs(trafficRows(report.headline)));

    lines.push('', 'LATENCY');
    lines.push(...pairs(latencyRows(report.headline)));

    const business = businessRows(report.headline);

    if (business.length > 0) {
        lines.push('', 'JOURNEYS');
        lines.push(...pairs(business));
    }

    if (report.phases.length > 0) {
        lines.push('', 'PHASES  (the first step whose p95 goes red is the knee — read the sample count too)');

        for (const phase of report.phases) {
            lines.push(
                `  ${phase.label.padEnd(6)} ${clock(phase.from)}–${clock(phase.to)}  ` +
                    `${String(phase.rate).padStart(5)}/s  ` +
                    `p95 ${pad(ms(phase.p95), 10)}  ` +
                    `errors ${pad(pct(phase.errorRate), 7)}  ` +
                    `n=${number(phase.samples)}`
            );
        }
    }

    lines.push('', `THRESHOLDS  (${report.thresholds.length - failed.length}/${report.thresholds.length} passed)`);

    for (const threshold of report.thresholds) {
        lines.push(`  ${threshold.ok ? '✓' : '✗'} ${threshold.metric}`);
        lines.push(`      '${threshold.source}'  ${measured(threshold)}`);
    }

    if (failed.length > 0) {
        lines.push('', `  ${failed.length} threshold(s) breached — k6 exits 99.`);
    }

    lines.push('', 'ARTIFACTS');
    lines.push(`  ${report.base}.summary.json`);
    lines.push(`  ${report.base}.summary.md`);
    lines.push('═'.repeat(96));

    return lines.join('\n');
}

function renderMarkdown(report) {
    const failed = report.thresholds.filter((threshold) => !threshold.ok);
    const lines = [];

    lines.push(
        `# ${report.script}${report.profile ? ` · \`${report.profile.name}\`` : ''} · run \`${report.runId}\``
    );
    lines.push('');

    if (report.profile) {
        lines.push(`> ${report.profile.question}`);
        lines.push('');
    }

    lines.push('| | |');
    lines.push('|---|---|');

    if (report.profile) {
        lines.push(`| Shape | ${report.profile.shape} |`);
    }

    lines.push(`| Wall clock | ${clock(report.durationMs / 1000)} |`);
    lines.push(`| Environment | \`${report.environment.name}\` · ${report.environment.gateway} |`);
    lines.push(`| Verdict | ${failed.length === 0 ? '**all thresholds passed**' : `**${failed.length} threshold(s) breached**`} |`);
    lines.push('');

    // The environment a number came from is half the number — and it is the half a markdown file
    // pasted into a document loses first. Everything above is what k6 can see by itself; the host,
    // the replica count and whether the generator was co-located are not, so the reader is told to
    // add them rather than left to assume they were captured.
    lines.push(
        '> Host CPU/RAM, replica count and whether the generator was co-located are **not** captured ' +
            'here — k6 cannot see them. Record them next to any number quoted from this file ' +
            '(`loadtest/README.md` → *Before every run*).'
    );
    lines.push('');

    lines.push('## Traffic');
    lines.push(...table(trafficRows(report.headline)));
    lines.push('');

    lines.push('## Latency');
    lines.push(...table(latencyRows(report.headline)));

    const business = businessRows(report.headline);

    if (business.length > 0) {
        lines.push('');
        lines.push('## Journeys');
        lines.push(...table(business));
    }

    if (report.phases.length > 0) {
        lines.push('');
        lines.push('## Phases');
        lines.push('');
        lines.push('| Phase | Window | Rate | journey p95 | Errors | Samples | login p95 |');
        lines.push('|---|---|---|---|---|---|---|');

        for (const phase of report.phases) {
            lines.push(
                `| \`${phase.label}\` | ${clock(phase.from)}–${clock(phase.to)} | ${phase.rate}/s | ` +
                    `${ms(phase.p95)} | ${pct(phase.errorRate)} | ${number(phase.samples)} | ${ms(phase.authP95)} |`
            );
        }

        lines.push('');
        lines.push(
            '_A phase with no samples has `p(95)=0` and passes its threshold trivially — that is what ' +
                'the steps after an aborted ramp look like._'
        );
    }

    lines.push('');
    lines.push('## Thresholds');
    lines.push('');
    lines.push('| | Metric | Gate | Measured |');
    lines.push('|---|---|---|---|');

    for (const threshold of report.thresholds) {
        lines.push(
            `| ${threshold.ok ? '✓' : '**✗**'} | \`${threshold.metric}\` | \`${threshold.source}\` | ` +
                `${measured(threshold)} |`
        );
    }

    lines.push('');

    return `${lines.join('\n')}\n`;
}

function trafficRows(headline) {
    return [
        ['requests', `${number(headline.requests)}  (${decimals(headline.requestsPerSecond, 1)}/s)`],
        ['http_req_failed', pct(headline.failureRate)],
        [
            'checks',
            `${pct(headline.checkRate)}  (${number(headline.checksPassed)} passed, ` +
                `${number(headline.checksFailed)} failed)`,
        ],
        ['iterations', `${number(headline.iterations)}  (dropped ${number(headline.droppedIterations)})`],
    ];
}

function latencyRows(headline) {
    return [
        [
            'journey  {scope:journey}',
            `p95 ${ms(headline.journeyP95)}   p99 ${ms(headline.journeyP99)}   ` +
                `med ${ms(headline.journeyMedian)}   max ${ms(headline.journeyMax)}   ` +
                `n=${number(headline.journeySamples)}`,
        ],
        [
            'login    {scope:auth}',
            `p95 ${ms(headline.authP95)}   n=${number(headline.authSamples)}` +
                '   — PBKDF2, and mostly the run\'s own ignition burst',
        ],
    ].concat(
        headline.orderPlacementP95 === null
            ? []
            : [['POST /orders', `p95 ${ms(headline.orderPlacementP95)}`]]
    );
}

/**
 * The business counters, and only the ones this run actually produced. A browse-only run has no
 * order line rather than a zero — see the note in {@link headline}.
 */
function businessRows(headline) {
    const rows = [
        ['orders placed', headline.ordersPlaced, number],
        ['placement failures', headline.orderPlacementFailureRate, pct],
        ['idempotency replays', headline.idempotencyReplays, number],
        ['tracking polls', headline.trackPolls, number],
        ['kitchen transitions', headline.kitchenTransitions, number],
        ['offers claimed', headline.driverClaimsWon, number],
        ['deliveries completed', headline.deliveriesCompleted, number],
    ];

    return rows
        .filter(([, recorded]) => recorded !== null)
        .map(([label, recorded, format]) => [label, format(recorded)]);
}

// ── Formatting ────────────────────────────────────────────────────────────────────────────────

function pairs(rows) {
    return rows.map(([label, text]) => `  ${label.padEnd(26)}${text}`);
}

function table(rows) {
    return ['', '| | |', '|---|---|', ...rows.map(([label, text]) => `| ${label} | ${text} |`)];
}

function measured(threshold) {
    if (threshold.value === null) {
        return '—';
    }

    // `count>100` is a whole number of orders; four decimal places on it reads like a rate and
    // invites the reader to look for a precision that is not there.
    const rendered =
        threshold.stat === 'count'
            ? number(threshold.value)
            : threshold.time
              ? ms(threshold.value)
              : decimals(threshold.value, 4);

    return `${threshold.stat}=${rendered}`;
}

/** `null` for a metric this run never recorded — the renderers drop those rows entirely. */
function value(metrics, name, stat) {
    const metric = metrics[name];

    if (!metric || !metric.values || metric.values[stat] === undefined) {
        return null;
    }

    return metric.values[stat];
}

/** `count` specifically: absent means zero, and a zero is worth printing for these. */
function count(metrics, name) {
    const found = value(metrics, name, 'count');

    return found === null ? 0 : found;
}

function ms(milliseconds) {
    if (milliseconds === null) {
        return '—';
    }

    return milliseconds >= 1000
        ? `${decimals(milliseconds / 1000, 2)} s`
        : `${decimals(milliseconds, 2)} ms`;
}

function pct(rate) {
    return rate === null ? '—' : `${decimals(rate * 100, 2)}%`;
}

function number(value_) {
    if (value_ === null) {
        return '—';
    }

    // Thousands separators without `toLocaleString`, which k6's runtime implements only for the
    // default locale and has been known to render as `1,633` on one platform and `1633` on another.
    return String(Math.round(value_)).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

function decimals(value_, places) {
    return value_ === null ? '—' : Number(value_).toFixed(places);
}

function clock(totalSeconds) {
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = Math.round(totalSeconds % 60);

    return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

function pad(text, width) {
    return String(text).padEnd(width);
}
