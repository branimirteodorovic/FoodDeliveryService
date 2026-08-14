// The four test types of §5 — **as data**. Adding a profile is an entry in `DEFINITIONS`, never a
// new script: the scenarios describe *what a user does*, the profile describes *how much and for how
// long*, and keeping those apart is what makes two runs comparable at all.
//
// Selected with `-e PROFILE=…` (the runner scripts pass it through as `--profile`):
//
//   baseline  low, constant, 5 min   what does an unloaded system cost per request?
//   ramp      steps up, ~14 min      **where is the knee?** — the number this feature exists to produce
//   spike     baseline → 10× → back  does it recover, and how fast does the queue drain?
//   soak      moderate, 2 h          do memory, connections or the outbox backlog grow without bound?
//
// ── Phases, and why every request carries the phase it happened in ───────────────────────────────
//
// A profile is a list of **phases**: an optional ramp-in, then a hold at a fixed arrival rate. The
// hold is what gets measured, and the plan's saturation rule is stated per step — *"the first step
// where p95 exceeds the SLO or `http_req_failed` exceeds 1%"*. A single run-wide percentile cannot
// answer that: it mixes eight steps together, and by the time the cumulative p95 crosses 500 ms the
// run is several steps past the knee.
//
// So a staged profile tags every request with its phase (`phase:s03`, `phase:peak`, …) and declares a
// **threshold per phase**. k6 prints one line per threshold sub-metric with its own statistics, which
// means the knee is readable straight from the terminal — the first phase whose `p(95)` goes red —
// with no Prometheus required. Ramp-in traffic is tagged `phase:tr` and excluded from all of them, so
// each step's numbers describe a plateau rather than a plateau plus the climb onto it.
//
// The cost is tag cardinality, and it is paid deliberately: `tagPhases` is off for the flat profiles,
// so a soak run — the long one, the one whose series Milestone E ships into the platform's own
// Prometheus — adds nothing. On a ramp it adds one label with nine values for fourteen minutes.

import exec from 'k6/execution';
import { SCOPE_AUTH, SCOPE_JOURNEY } from './thresholds.js';

/**
 * The shed budget for a step of a staged profile.
 *
 * Generous, and it has to be: past the knee the Gateway's guardrail (Milestone G) is *supposed* to
 * be refusing traffic, and a tight budget here would fail the run for the platform behaving
 * correctly. It is not `1` — a step where nearly everything is shed means the limiter is sized below
 * what the platform can actually serve, which is the opposite failure and just as worth catching.
 */
const STAGED_THROTTLED_RATE = 0.5;

/**
 * Arrival rate at multiplier 1×, in customers per second, split across the journeys by `MIX`.
 *
 * Every profile is expressed as multiples of this one number, so `-e RATE=` moves a whole profile up
 * or down without changing its shape — which is exactly what a second machine needs. The default of
 * 2/s is the Milestone C gate: a rate a co-located compose stack answers with p95 in the tens of ms.
 */
export const BASE_RATE = Number(__ENV.RATE || 2);

/**
 * The journey mix (§4). Shares of the customer arrival rate, not counts — the executors turn them
 * into per-scenario rates. It lives here rather than in `mixed.js` because it is part of the load
 * *model*: change it and every profile's numbers change with it.
 */
export const MIX = { browse: 0.7, order: 0.2, track: 0.08 };

/**
 * Arrival rates are expressed per **10 seconds**, because k6's `rate` is an integer. A 70% share of
 * 2/s is 1.4 arrivals per second, which rounds to 1 per second — a 29% error on the largest journey
 * in the mix — but to 14 per 10 s exactly. The time unit is what keeps a stated mix the mix that runs.
 */
const TIME_UNIT_SECONDS = 10;

/** The label a request gets while the arrival rate is still climbing onto a phase's plateau. */
export const TRANSITION_PHASE = 'tr';

/**
 * Rough seconds a delivery occupies a driver VU, used only to size the driver pool from the peak
 * order rate. Derived from the measured tick loop: claim, `picked-up`, `delivered`, one per 1.5–3 s
 * poll, plus the ticks spent finding the offer at all. See `scenarios/driver.js`.
 */
const DRIVER_SECONDS_PER_DELIVERY = 20;

/**
 * Ceiling on the driver pool a profile will ask for, regardless of arrival rate.
 *
 * Not arbitrary, and worth the arithmetic. The offer-board workaround (`scenarios/driver.js`) costs
 * roughly *P/2* wasted claims per delivery for a pool of *P*, so a pool of *P* ticking every ~2.25 s
 * completes about `(P / 2.25) / (P / 2 + 3)` deliveries per second — 0.51/s at 8 drivers, 0.65/s at
 * 16, 0.71/s at 24, and **asymptotically 0.89/s no matter how many drivers run**, because the waste
 * grows with the pool. Past ~24 the extra drivers buy percentage points and pay for them in board
 * polls, so this is where the curve stops being worth it.
 *
 * That asymptote is a limit of the *harness*, not of the platform, and it is the strongest argument
 * in the repo for the "my offers" read model Milestone C's finding 3 asks for: above ~0.9 orders per
 * second the fulfilment half of a run measures a workaround. `mixed.js` says so out loud when a
 * profile's peak crosses it.
 */
const MAX_DRIVER_POOL = Number(__ENV.DRIVER_VUS_MAX || 24);

/** Fulfilment throughput the offer-board workaround can sustain, deliveries per second. */
export const FULFILMENT_CEILING = 0.9;

/**
 * The ramp's steps, as multipliers of `BASE_RATE`.
 *
 * Sub-linear at the top on purpose: the interesting region is wherever the curve bends, and steps
 * that double every time walk straight past it. Override with `-e RAMP_STEPS=10,13,16` when a
 * previous ramp has already located the neighbourhood and the next run is bisecting it.
 *
 * The default spans the knee **measured** on the reference environment — 20 customers/s green at
 * journey p95 230 ms, 26 customers/s gone at 1.85 s and 3.8% errors — with two steps of runway past
 * it, so a stock `--profile ramp` finds a knee rather than reporting that everything was fine. That
 * is a property of one 8-core host, and a machine with more of them will need higher multipliers or a
 * bigger `-e RATE=`; the ramp aborting on its cumulative thresholds is what keeps the extra steps
 * cheap when it doesn't.
 */
const RAMP_STEPS = (__ENV.RAMP_STEPS || '1,2,4,6,8,10,13,16')
    .split(',')
    .map((value) => Number(value.trim()))
    .filter((value) => value > 0);

/**
 * How long each ramp step is held. **The plan's floor is 60 s and it is a real floor**: the outbox
 * ticks every 5 s in batches of 20, the permission cache has a 5-minute TTL, and Postgres' plan cache
 * and connection pool both need a moment. A step shorter than that measures a transient and reports
 * it as a capacity number.
 */
const RAMP_HOLD = __ENV.RAMP_HOLD || '90s';

/** Climb onto each step. Short — it is dead time, tagged `tr` and measured by nothing. */
const RAMP_TRANSITION = '15s';

const DEFAULT_PROFILE = 'baseline';

/**
 * `authP95` for every profile that starts its supply side at once.
 *
 * The mixed run's kitchen and driver VUs all begin at t=0 and each acquires a token on its first
 * iteration, so Identity is handed dozens of PBKDF2 verifications inside a second or two. Measured at
 * the Milestone C gate: p95 2.87 s, median 1.09 s, against 643 ms for five concurrent logins and
 * ~150 ms for one. It is a startup transient that scales with the VU count, not the steady-state cost
 * of signing in — but it is not excluded, because token issuance being the most expensive endpoint in
 * the system is a capacity fact worth publishing (Milestone F #6).
 */
const SUPPLY_START_AUTH_P95 = 4000;

/**
 * The first minute of a staged run, measured by nothing.
 *
 * **This phase is here because the first spike run failed on the wrong thing.** Its `pre` phase — two
 * minutes at the *baseline* rate, before the spike — recorded journey p95 938 ms with a 14.9 s
 * maximum, while the 10× peak that followed it managed 194 ms. Nothing about the platform explains
 * that ordering. What `pre` had actually measured was the run's own ignition: k6 initialising VUs, 44
 * operator VUs and their dispatch tokens acquiring PBKDF2 tokens inside a couple of seconds on a host
 * the generator shares, and every fixture identity's first authenticated request paying for a cold
 * Redis permission cache behind `CustomClaimsTransformation`.
 *
 * All of that is real load and it stays in the run — `warm` is tagged, counted, and its numbers are
 * printed like everyone else's. It is simply not allowed to be the phase a knee gets attributed to.
 * The same reasoning as `setup()`'s warm-up in Milestone A, one level up: a deployment property is
 * not what these thresholds are about, and on a ramp it would put the first red step at step one.
 */
const WARM_PHASE = {
    label: 'warm',
    multiplier: 1,
    hold: __ENV.WARM_HOLD || '60s',
    journeyP95: 5000,
    errorRate: 0.05,
    authP95: 20000,
    // The ignition burst is a crowd of VUs arriving at once on a cold platform, so a little shedding
    // here is the guardrail working. It is still budgeted rather than exempt: a warm-up that is
    // mostly 429s would mean the limiter cannot absorb a normal start.
    throttledRate: 0.1,
};

const DEFINITIONS = {
    baseline: {
        question: 'What does an unloaded system cost per request? Everything else is read against this.',
        shape: `${BASE_RATE}/s constant`,
        // Strict, and it must stay strict: this is the profile whose two consecutive runs have to
        // agree within the tolerance in the README, or nothing measured afterwards means anything.
        thresholds: { authP95: SUPPLY_START_AUTH_P95 },
        phases: [{ label: 'steady', multiplier: 1, hold: __ENV.DURATION || '5m' }],
    },

    ramp: {
        question: 'Where is the knee? — the number this whole feature is built to produce.',
        shape: `${RAMP_STEPS.join('× → ')}× of ${BASE_RATE}/s, ${RAMP_HOLD} per step`,
        // Deliberately expected to fail at the top. `abortOnFail` stops the run once the *cumulative*
        // error rate or journey p95 has gone, because past the knee the run has already answered its
        // question and ten more minutes of recording timeouts adds nothing. The delay covers the
        // first step, where a handful of samples can swing a percentile.
        //
        // The per-phase thresholds below do **not** abort: a sub-metric that has just come into
        // existence has very few samples, and one slow request at the top of a step would end the run
        // several steps early. They mark the knee; the cumulative ones stop the run.
        //
        // No run-wide `authP95`: the login percentile is gated per phase instead (see below), because
        // a cumulative one on a staged run is dominated by the ignition burst in `warm` and says
        // nothing about any step.
        thresholds: {
            abortOnFail: true,
            delayAbortEval: '3m',
            authP95: null,
            // The run-wide gate is the *cumulative* shed fraction across every step, most of which
            // are below the knee — so a ramp that ends up shedding half of everything it offered has
            // found a limiter set too low, not a platform at capacity.
            throttledRate: STAGED_THROTTLED_RATE,
        },
        tagPhases: true,
        phases: [
            WARM_PHASE,
            ...RAMP_STEPS.map((multiplier, index) => ({
                label: `s${String(index + 1).padStart(2, '0')}`,
                multiplier,
                // Every step brings new VUs, and a new VU logs in once — so the login budget stays
                // generous all the way up, and it is the *journey* percentiles that mark the knee.
                authP95: 8000,
                throttledRate: STAGED_THROTTLED_RATE,
                hold: RAMP_HOLD,
                // The first step continues at the warm-up's rate; the rest climb onto theirs.
                rampIn: index === 0 ? null : RAMP_TRANSITION,
            })),
        ],
    },

    spike: {
        question: 'Does it recover, and how long does the queue take to drain?',
        shape: `${BASE_RATE}/s → 10× for 60s → ${BASE_RATE}/s`,
        thresholds: { authP95: null, errorRate: 0.05, throttledRate: STAGED_THROTTLED_RATE },
        tagPhases: true,
        // The pass condition for a spike is **recovery**, not survival, and phase thresholds are how
        // that gets said in a way k6 can check: `peak` is allowed to degrade, `post` is not. A run
        // that stays slow after the spike ends fails on `post` while its run-wide percentile — two
        // minutes of calm either side of one minute of chaos — looks perfectly healthy.
        //
        // Login is gated the same way, and it is the most interesting line in the profile: a 10×
        // arrival spike is a crowd of *new* VUs, each of which signs in once, so the spike lands
        // squarely on the one endpoint in the system that burns CPU on purpose. Measured at 10× on a
        // healthy host: p95 2.81 s at the peak against 273 ms in `pre`, and `post` recorded no new
        // logins at all. `peak` is therefore allowed to bend; `post` is not, because a login path
        // still slow two minutes after the crowd left is a queue that never drained.
        phases: [
            WARM_PHASE,
            // `pre` and `post` are the baseline rate: nothing should be shed there, and `post`
            // shedding after the crowd has gone is a guardrail that failed to recover.
            { label: 'pre', multiplier: 1, hold: '2m', authP95: SUPPLY_START_AUTH_P95, throttledRate: 0.01 },
            {
                label: 'peak',
                multiplier: 10,
                hold: '60s',
                rampIn: '10s',
                journeyP95: 5000,
                errorRate: 0.05,
                authP95: 15000,
                // A 10× arrival spike is exactly the case admission control is for. Shedding most of
                // it is the correct outcome; the pass condition is what `post` looks like afterwards.
                throttledRate: 0.9,
            },
            {
                label: 'post',
                multiplier: 1,
                hold: '3m',
                rampIn: '10s',
                authP95: SUPPLY_START_AUTH_P95,
                throttledRate: 0.01,
            },
        ],
    },

    soak: {
        question: 'Do memory, connections or the outbox backlog grow without bound?',
        shape: `${BASE_RATE * 2}/s constant`,
        // Strict: a soak that is allowed to degrade is a soak that cannot fail, and the whole point is
        // that hour two looks like hour one.
        thresholds: { authP95: SUPPLY_START_AUTH_P95 },
        phases: [{ label: 'steady', multiplier: 2, hold: __ENV.SOAK_DURATION || __ENV.DURATION || '2h' }],
    },
};

export const profile = resolve();

/**
 * The k6 executor stanza for one journey's share of the mix.
 *
 * Flat profiles get `constant-arrival-rate`, staged ones `ramping-arrival-rate` — never
 * `constant-vus` for a customer journey. A closed VU loop issues its next request only after the
 * previous one returns, so as the platform slows the offered load falls with it: throughput plateaus,
 * latency looks merely elevated, and the run reports a system coping. Real customers keep arriving.
 * Arrival rate is the only model in which a knee exists to be found.
 *
 * @param {string} name the `exec` function and scenario name
 * @param {number} share this journey's share of `BASE_RATE`
 * @param {number} secondsPerIteration how long one iteration occupies a VU, think time included —
 *   what turns an arrival rate into a VU allocation
 */
export function arrivalStanza(name, share, secondsPerIteration) {
    const target = (multiplier) =>
        Math.max(1, Math.round(BASE_RATE * multiplier * share * TIME_UNIT_SECONDS));

    // VUs needed to sustain a rate when each iteration holds one for `secondsPerIteration`.
    const need = (multiplier) => Math.max(2, Math.ceil(BASE_RATE * multiplier * share * secondsPerIteration));

    // Pre-allocated for the *first* phase, capped for the *peak*. k6 initialises pre-allocated VUs
    // before the test starts — allocating a ramp's peak up front would spend a minute and a gigabyte
    // building runtimes that sit idle for ten. The rest are initialised on demand as the rate climbs,
    // which is cheap here (a VU's init is a 100 KB `JSON.parse` of the fixture). `dropped_iterations`
    // in the summary is what says this was sized too small — and it is also a saturation signal in
    // its own right, so read it before blaming the generator.
    const preAllocatedVUs = Math.ceil(need(profile.steps[0].multiplier) * 1.5);
    const maxVUs = Math.max(preAllocatedVUs, Math.ceil(need(profile.peakMultiplier) * 2));

    const common = {
        timeUnit: `${TIME_UNIT_SECONDS}s`,
        preAllocatedVUs,
        maxVUs,
        exec: name,
    };

    if (profile.constant) {
        return {
            executor: 'constant-arrival-rate',
            rate: target(profile.steps[0].multiplier),
            duration: profile.duration,
            ...common,
        };
    }

    const stages = [];

    for (const step of profile.steps) {
        if (step.transitionSeconds > 0) {
            stages.push({ target: target(step.multiplier), duration: `${step.transitionSeconds}s` });
        }

        stages.push({ target: target(step.multiplier), duration: `${step.holdSeconds}s` });
    }

    return {
        executor: 'ramping-arrival-rate',
        // Without this k6 starts at zero and spends the first phase climbing to it.
        startRate: target(profile.steps[0].multiplier),
        stages,
        ...common,
    };
}

/**
 * The per-phase thresholds described at the top of this file — `{}` for a flat profile.
 *
 * Every phase gets the two criteria the plan's saturation rule names, so the first red line *is* the
 * answer. The third criterion — a backlog growing monotonically across a step — is deliberately not
 * here: k6 cannot see the outbox. That one is read from the platform's own telemetry, and the README
 * says where.
 */
export function phaseThresholds() {
    if (!profile.tagPhases) {
        return {};
    }

    const thresholds = {};

    for (const step of profile.steps) {
        thresholds[`http_req_duration{scope:${SCOPE_JOURNEY},phase:${step.label}}`] = [
            `p(95)<${step.journeyP95}`,
        ];
        thresholds[`http_req_failed{phase:${step.label}}`] = [`rate<${step.errorRate}`];
        // The guardrail's own line, per step. On a ramp this is the plateau made legible: the shed
        // fraction stays at zero step after step and then starts climbing at exactly the step where
        // the platform ran out of capacity — which used to be the step where everything timed out.
        thresholds[`requests_throttled{phase:${step.label}}`] = [`rate<${step.throttledRate}`];
        // Login per phase, which is the only way to ask "did it *recover*?" about a path whose
        // run-wide percentile is fixed by whatever happened in the first thirty seconds.
        thresholds[`http_req_duration{scope:${SCOPE_AUTH},phase:${step.label}}`] = [
            `p(95)<${step.authP95}`,
        ];
    }

    return thresholds;
}

/**
 * The phase tag for a request happening right now — merged into every request's tags by `lib/http.js`
 * and `lib/auth.js`, and `{}` unless the profile asked for phases.
 *
 * Timed from **the scenario's start, not the test's**. `setup()` runs first and a mixed run's setup
 * clocks a whole driver roster on and off, which can take a minute or more; measuring from test start
 * would slide every phase boundary by however long that took and quietly mislabel the results. Both
 * are unavailable outside a VU — a `setup()` request has no phase, which is correct.
 */
export function phaseTag() {
    if (!profile.tagPhases) {
        return {};
    }

    let elapsedSeconds;

    try {
        elapsedSeconds = (Date.now() - exec.scenario.startTime) / 1000;
    } catch (_) {
        // init / setup / teardown — outside every phase.
        return {};
    }

    return { phase: phaseAt(elapsedSeconds) };
}

/** Which phase an offset into the run belongs to; `tr` while the rate is still climbing. */
export function phaseAt(elapsedSeconds) {
    for (const step of profile.steps) {
        if (elapsedSeconds >= step.holdStart && elapsedSeconds < step.holdEnd) {
            return step.label;
        }
    }

    return TRANSITION_PHASE;
}

/**
 * The driver pool this profile's peak needs, bounded by `MAX_DRIVER_POOL`. Owned here rather than in
 * `scenarios/driver.js` because supply sizing is part of the load model — and because the roster has
 * to be knowable in `setup()`, before any VU exists to ask.
 */
export function driverPoolFor(peakOrdersPerSecond) {
    const wanted = Math.ceil(peakOrdersPerSecond * DRIVER_SECONDS_PER_DELIVERY);

    return Math.max(4, Math.min(wanted, MAX_DRIVER_POOL));
}

/** One line naming the profile, for `setup()`. */
export function describeProfile() {
    return `profile '${profile.name}' · ${profile.shape} · ${profile.durationSeconds}s · ${profile.question}`;
}

/**
 * The phase timetable, as offsets from the first VU iteration. Printed by `mixed.js` so a summary
 * read weeks later — or a Grafana window during the run — can be sliced by step without re-deriving
 * the schedule from the profile definition.
 */
export function phaseTimetable() {
    return profile.steps.map(
        (step) =>
            `  ${step.label.padEnd(6)} ${clock(step.holdStart)}–${clock(step.holdEnd)}  ` +
            `${step.rate}/s  (p95<${step.journeyP95}ms, errors<${pct(step.errorRate)}, ` +
            `shed<${pct(step.throttledRate)})`
    );
}

function resolve() {
    const name = __ENV.PROFILE || DEFAULT_PROFILE;
    const definition = DEFINITIONS[name];

    if (!definition) {
        throw new Error(
            `unknown PROFILE '${name}' — expected one of: ${Object.keys(DEFINITIONS).join(', ')}`
        );
    }

    const defaults = {
        journeyP95: 500,
        errorRate: 0.01,
        authP95: 2000,
        throttledRate: 0.001,
        ...definition.thresholds,
    };

    let offset = 0;

    const steps = definition.phases.map((phase) => {
        const transitionSeconds = phase.rampIn ? seconds(phase.rampIn) : 0;
        const holdSeconds = seconds(phase.hold);

        const step = {
            label: phase.label,
            multiplier: phase.multiplier,
            rate: round(BASE_RATE * phase.multiplier),
            transitionSeconds,
            holdSeconds,
            holdStart: offset + transitionSeconds,
            holdEnd: offset + transitionSeconds + holdSeconds,
            journeyP95: phase.journeyP95 || defaults.journeyP95,
            errorRate: phase.errorRate || defaults.errorRate,
            authP95: phase.authP95 || defaults.authP95 || SUPPLY_START_AUTH_P95,
            throttledRate: phase.throttledRate || defaults.throttledRate,
        };

        offset = step.holdEnd;

        return step;
    });

    const peakMultiplier = steps.reduce((peak, step) => Math.max(peak, step.multiplier), 0);

    return {
        name,
        question: definition.question,
        shape: definition.shape,
        thresholds: definition.thresholds || {},
        tagPhases: Boolean(definition.tagPhases),
        constant: steps.length === 1 && steps[0].transitionSeconds === 0,
        steps,
        peakMultiplier,
        peakRate: round(BASE_RATE * peakMultiplier),
        durationSeconds: offset,
        // What the supply side runs for: the whole thing, `constant-vus`, in seconds so no profile
        // can express a duration the two halves of a run disagree about.
        duration: `${offset}s`,
    };
}

/**
 * `90s`, `2m`, `1m30s`, `2h` → seconds. Deliberately strict: a typo'd duration that silently became
 * `NaN` would make every phase boundary — and therefore every per-phase threshold — nonsense.
 */
function seconds(duration) {
    const match = /^(?:(\d+(?:\.\d+)?)h)?(?:(\d+(?:\.\d+)?)m)?(?:(\d+(?:\.\d+)?)s)?$/.exec(String(duration));
    const total = match
        ? Number(match[1] || 0) * 3600 + Number(match[2] || 0) * 60 + Number(match[3] || 0)
        : 0;

    if (total <= 0) {
        throw new Error(`invalid duration '${duration}' — expected something like '90s', '5m' or '2h'`);
    }

    return total;
}

function clock(totalSeconds) {
    const minutes = Math.floor(totalSeconds / 60);
    const remainder = Math.round(totalSeconds % 60);

    return `${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`;
}

function pct(rate) {
    return `${round(rate * 100)}%`;
}

function round(value) {
    return Math.round(value * 100) / 100;
}
