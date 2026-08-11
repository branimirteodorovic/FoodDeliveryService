// Mixed — all five journeys at once, which is the only shape that measures the platform rather than
// one endpoint of it, and the script the load **profiles** drive.
//
// ── Arrival rate, not virtual users ──────────────────────────────────────────────────────────────
//
// The customer journeys run under an *arrival-rate* executor, and that is the single most important
// decision in this file. A `constant-vus` closed loop issues its next request only after the previous
// one returns, so as the system slows the load *falls with it*: throughput plateaus, latency looks
// merely elevated, and the run reports a system coping. That is exactly backwards. Real customers do
// not slow down because the site is slow — they keep arriving, the queue grows, and the failure is a
// cliff rather than a gentle curve. Arrival rate models that, which is why `--profile ramp` can find
// a knee at all.
//
// The two operator scenarios are `constant-vus`, and correctly so: a kitchen has a fixed number of
// staff and a city has a fixed number of drivers. They are supply, not demand — sized to the order
// rate rather than driven by it.
//
// ── Shape, and where it comes from ───────────────────────────────────────────────────────────────
//
// Nothing in this file decides how much load to offer. `config/profiles.js` owns that: the executor,
// the stages, the duration, the driver pool, the per-phase thresholds. This file owns the *mix* —
// which journeys, in what proportion, with what supply behind them. Running a different test type is
// therefore `--profile spike`, not an edit:
//
//   ./run.sh scenarios/mixed.js --profile baseline     5 min, low, strict  — the reference run
//   ./run.sh scenarios/mixed.js --profile ramp         ~14 min of steps    — the knee
//   ./run.sh scenarios/mixed.js --profile spike        10× for 60 s        — recovery
//   ./run.sh scenarios/mixed.js --profile soak         2 h                 — leaks and backlogs
//
// `-e RATE=` scales any of them without changing its shape, which is what a different machine needs.

import { runId } from '../config/environments.js';
import {
    FULFILMENT_CEILING,
    MIX,
    arrivalStanza,
    describeProfile,
    phaseThresholds,
    phaseTimetable,
    profile,
} from '../config/profiles.js';
import { sloThresholds } from '../config/thresholds.js';
import { announce, fixture, requireFixture } from '../lib/fixtures.js';
import { browseJourney } from './browse.js';
import { DRIVER_POOL, driverJourney, prepareRoster } from './driver.js';
import { kitchenJourney } from './restaurant.js';
import { orderJourney } from './order.js';
import { trackJourney } from './track.js';

/**
 * How long one iteration of each journey occupies a VU, think time included — browse sleeps 1–3 s
 * three times, order adds a placement on top of a browse, track polls for up to a minute. This is
 * what turns an arrival rate into a VU allocation; under-allocating makes k6 report
 * `dropped_iterations` instead of offering the load, which is a measurement of the generator.
 */
const SECONDS_PER_ITERATION = { browse: 10, order: 12, track: 60 };

/**
 * One manager per seeded restaurant by default. A smaller pool leaves the restaurants it does not
 * cover stuck in `Pending` for the whole run — `restaurant.js` explains why and warns when it
 * happens — and the point of the mixed run is a lifecycle that completes.
 *
 * `0` drops the scenario entirely. That is not a curiosity: a ramp taken past the fulfilment ceiling
 * below is a *read-path* measurement, and running it with no kitchen and no drivers is the honest way
 * to say so — better than leaving two scenarios in the mix whose numbers describe a workaround.
 */
const KITCHEN_VUS =
    __ENV.KITCHEN_VUS === undefined ? fixture.restaurants.length || 1 : Number(__ENV.KITCHEN_VUS);

/**
 * Sized from the profile's peak order rate, bounded by `MAX_DRIVER_POOL` and by the fixture. Owned by
 * `driver.js` so one number decides the VU count *and* which seeded drivers `setup()` clocks on —
 * the roster has to be knowable before any VU exists.
 */
const DRIVER_VUS = Number(__ENV.DRIVER_VUS) === 0 ? 0 : DRIVER_POOL;

/** Orders per second this profile peaks at — what the supply side has to keep up with. */
const PEAK_ORDER_RATE = profile.peakRate * MIX.order;

export const options = {
    scenarios: scenarios(),

    thresholds: {
        // The shared SLO block, with this profile's overrides folded in (`config/profiles.js`): a
        // spike is allowed to degrade at its peak, a baseline and a soak are not allowed to degrade
        // at all, and a ramp aborts once the run-wide numbers have gone.
        ...sloThresholds(profile.thresholds),

        // One p95 and one error rate **per phase** on the staged profiles. This is what makes the
        // plan's saturation rule mechanical: the first phase whose `p(95)` line goes red is the knee,
        // and for a spike the `post` phase going green is the proof that it recovered.
        ...phaseThresholds(),

        order_placement_duration: ['p(95)<1000'],
        order_placement_failures: ['rate<0.01'],
        order_idempotency_replay_correct: ['rate>0.99'],
        kitchen_transition_success: ['rate>0.95'],
        // The end-to-end gate. If drivers never win a claim, nothing reaches `Delivered` and the run
        // has measured browsing with extra steps. Dropped when the supply side is switched off, where
        // a claim nobody makes is the point rather than a failure.
        ...(DRIVER_VUS > 0 ? { driver_claim_hit_rate: ['rate>0'] } : {}),
    },

    // Carries the profile, so Milestone E's Prometheus series — and the summary files — can tell a
    // baseline from the ramp that followed it without opening either.
    tags: { testid: `mixed-${profile.name}-${runId}` },

    // The driver roster logs in as every seeded driver once; k6's 60 s default is not enough.
    setupTimeout: __ENV.SETUP_TIMEOUT || '5m',
};

export function setup() {
    requireFixture('mixed.js');

    announce(
        'mixed.js',
        `${describeProfile()} · browse ${pct(MIX.browse)} · order ${pct(MIX.order)} · ` +
            `track ${pct(MIX.track)} · ${KITCHEN_VUS} kitchens · ${DRIVER_VUS} drivers`
    );

    if (profile.tagPhases) {
        // The timetable, in offsets from the first VU iteration — so a Grafana window, a Seq query or
        // a summary read six weeks later can be sliced by step without re-deriving the schedule.
        console.log(`phases (offset from the first iteration):\n${phaseTimetable().join('\n')}`);
    }

    // Clock the driven drivers on and every other seeded driver off, so the assignment candidate
    // pool contains exactly the drivers this run is driving. `driver.js` explains why that is not
    // cosmetic — without it most deliveries are parked `Unassigned` before anyone is offered them.
    if (DRIVER_VUS > 0) {
        prepareRoster();
    }

    warnAboutSupply();

    return { startedOnUtc: new Date().toISOString() };
}

// k6 dispatches each scenario to the named export in its `exec`. The journeys themselves live in the
// scenario files, which stay independently runnable — this file only decides how much of each.
export function browse() {
    browseJourney();
}

export function order() {
    orderJourney();
}

export function track() {
    trackJourney();
}

export function kitchen() {
    kitchenJourney();
}

export function driver() {
    driverJourney();
}

/** The five scenarios: three arrival-rate customer journeys and the two operator pools. */
function scenarios() {
    const stanzas = {
        browse: arrivalStanza('browse', MIX.browse, SECONDS_PER_ITERATION.browse),
        order: arrivalStanza('order', MIX.order, SECONDS_PER_ITERATION.order),
        track: arrivalStanza('track', MIX.track, SECONDS_PER_ITERATION.track),
    };

    // Omitted rather than declared with zero VUs, which k6 rejects.
    if (KITCHEN_VUS > 0) {
        stanzas.kitchen = supply('kitchen', KITCHEN_VUS);
    }

    if (DRIVER_VUS > 0) {
        stanzas.driver = supply('driver', DRIVER_VUS);
    }

    return stanzas;
}

/**
 * A fixed operator pool, running for the profile's whole duration — in seconds, from the profile, so
 * the two halves of a run cannot disagree about how long it is.
 */
function supply(name, vus) {
    return { executor: 'constant-vus', vus, duration: profile.duration, exec: name };
}

/**
 * The two ways a run's *supply* quietly stops being able to keep up with its demand, said out loud at
 * the moment it happens rather than inferred from a flat state-transition panel afterwards.
 */
function warnAboutSupply() {
    if (KITCHEN_VUS > fixture.restaurants.length) {
        console.warn(
            `mixed.js: KITCHEN_VUS=${KITCHEN_VUS} exceeds the ${fixture.restaurants.length} seeded ` +
                'restaurants; the surplus VUs duplicate managers and poll the same dashboard twice.'
        );
    }

    if (DRIVER_VUS > 0 && PEAK_ORDER_RATE > FULFILMENT_CEILING) {
        // The harness's own ceiling, not the platform's — see `MAX_DRIVER_POOL` in
        // `config/profiles.js` for the arithmetic. Worth a warning every time, because the failure
        // mode is silent: placements keep succeeding, `Unassigned` climbs, and the delivery numbers
        // look like a platform limit.
        console.warn(
            `mixed.js: this profile peaks at ${PEAK_ORDER_RATE.toFixed(2)} orders/s, above the ` +
                `~${FULFILMENT_CEILING}/s the offer-board workaround can fulfil with any driver ` +
                'pool. Deliveries will strand `Unassigned` past that point — that is the harness, ' +
                'not the platform. Read the placement and read-path numbers; treat the fulfilment ' +
                'ones as a floor, or re-run with -e KITCHEN_VUS=0 -e DRIVER_VUS=0 for a clean ' +
                'read-path measurement.'
        );
    }
}

function pct(share) {
    return `${Math.round(share * 100)}%`;
}
