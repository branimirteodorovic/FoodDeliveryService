// Driver — the supply side, and the half of the platform that closes the order lifecycle.
//
//   POST /delivery/drivers/me/location  (every few seconds — the highest-frequency endpoint there is)
//   → claim an offered delivery
//   → POST /delivery/deliveries/{id}/picked-up
//   → POST /delivery/deliveries/{id}/delivered
//
// Without this scenario running, deliveries sit in `Offered` until the 30 s window lapses,
// `ProcessExpiredOffersJob` re-offers them down the candidate list, and every one of them ends
// `Unassigned`. Orders stop at `ReadyForPickup`, `orders_state_transition_total{to="Delivered"}`
// stays at zero, and the run measures a platform that never delivers anything.
//
// ── The workaround, stated plainly ───────────────────────────────────────────────────────────────
//
// **A driver cannot find out over REST that they have been offered a delivery.** Verified against
// the code, not assumed:
//
//   * `GET /delivery/deliveries` filters on `d.driver_id`, which is `NULL` until a driver *accepts*
//     — an offer sets `offered_driver_id`, a different column;
//   * `offered_driver_id` appears in no response DTO anywhere (`DeliverySummaryResponse`,
//     `DeliveryResponse`), and `DeliveryAccess.EnsureCanView` admits the customer, the *assigned*
//     driver and administrators — not the offered driver;
//   * `DeliveryOfferedIntegrationEvent` exists and is documented as the push channel, but nothing
//     consumes it yet — the RealTime service handles assignment and status, not offers.
//
// So the offer reaches a real driver app through a channel this feature has put out of scope (§12,
// SignalR), and there is no read model standing in for it. Rather than add production code from a
// load-test milestone, the harness works around it: each driver VU polls the **administrator's**
// delivery board for deliveries currently in `Offered`, and *attempts* one. The domain rejects every
// driver but the offered one (`DeliveryErrors.NotAssignedDriver`), so exactly one claim per delivery
// wins.
//
// That has a measured cost and it is not hidden: with a pool of P drivers, roughly P/2 claims are
// wasted per delivery. It is bounded — a VU records the deliveries it has already tried and never
// retries them, because `OfferTo` never offers the same delivery to the same driver twice — and it
// is counted (`driver_claims_missed`, `driver_claim_hit_rate`), so the overhead can be subtracted
// from any number this run produces. Two consequences worth knowing before reading results:
//
//   * **keep the driver pool small.** The waste is linear in it. The default of 8 is sized for the
//     order rates a compose stack sustains, not for the 50 drivers the seeder creates.
//   * **the failed claims are not free.** `AcceptDeliveryOfferCommandHandler` takes the
//     `delivery:driver-lock:{driverId}` distributed lock *before* it loads anything, so a losing
//     claim is a Redis round trip plus a delivery read. They are on the *claiming* driver's own key,
//     so they do not contend with each other, and the assignment routine's own
//     `delivery_assignment_outcome` counters are untouched by them — but Redis operations per second
//     during a run include them.
//
// The honest fix is a `GET /delivery/deliveries/offers` read model or the SignalR push, and it
// belongs in the Delivery feature, not here. `docs/load-testing.md` (Milestone H) records it.

import { sleep } from 'k6';
import { runId } from '../config/environments.js';
import { MIX, driverPoolFor, profile } from '../config/profiles.js';
import { SCOPE_DISPATCH, SCOPE_SETUP, sloThresholds } from '../config/thresholds.js';
import { dispatchToken, driverForThisVu, tokenFor } from '../lib/actors.js';
import { DELIVERY_STATUS, DRIVER_STATUS, isStatus, jitter, thinkTime } from '../lib/domain.js';
import {
    announce,
    fixture,
    offDutyDrivers,
    onDutyDrivers,
    randomRestaurant,
    requireFixture,
} from '../lib/fixtures.js';
import { gatewayUrl, get, patch, post, send } from '../lib/http.js';
import {
    deliveriesCompleted,
    deliveryFulfilmentDuration,
    driverClaimHitRate,
    driverClaimsMissed,
    driverClaimsWon,
    driverLocationReports,
} from '../lib/metrics.js';

/**
 * How much of the board one poll pulls. `GetDeliveriesQuery` orders `created_on_utc DESC` and has no
 * status filter, so this is a *recency* window over every delivery ever created — terminal ones
 * included. It has to stay comfortably larger than the number of deliveries the run creates inside
 * one 30 s offer window, or offers scroll off page 1 before anyone claims them and expire
 * `Unassigned` for a reason that is the harness's, not the platform's.
 */
const BOARD_PAGE_SIZE = Number(__ENV.DRIVER_BOARD_PAGE_SIZE || 50);

/**
 * Tick interval. Faster than a real driver app would report, and sized against the **30 s offer
 * window** rather than against realism: an offer has to be found and claimed by its owner before it
 * lapses, and with P drivers each trying one delivery per tick the owner needs a few ticks of
 * headroom. Raising this without shrinking the driver pool is how a run ends up with more
 * `Unassigned` deliveries than delivered ones.
 */
const POLL_MIN_SECONDS = Number(__ENV.DRIVER_POLL_MIN || 1.5);
const POLL_MAX_SECONDS = Number(__ENV.DRIVER_POLL_MAX || 3);

/** How far a driver drifts between position reports. A few hundred metres, city-scale. */
const MOVE_KM = Number(__ENV.DRIVER_MOVE_KM || 0.3);

/**
 * VU-local. `tried` is the whole reason the workaround stays bounded: an offer this driver failed to
 * claim was somebody else's, and `Delivery.OfferTo` refuses to offer the same delivery to a driver
 * twice — so it can never become claimable by this VU and is never attempted again. Rebuilt from
 * each board poll so a soak run does not accumulate ids for deliveries that finished hours ago.
 */
let tried = {};

/** Where this VU's driver currently is, so position reports describe a plausible route. */
let position = null;

/** The delivery this VU is carrying, if any. */
let carrying = null;

/**
 * How many drivers this run drives. One number governs the whole scenario — `options.vus` here, the
 * `driver` stanza in `mixed.js`, and which fixture entries count as on duty — because the roster has
 * to be knowable in `setup()`, before any VU exists to ask.
 *
 * Sized from the **profile's peak order rate** (`config/profiles.js`), not chosen in the abstract:
 * too few drivers and deliveries exhaust their candidate list and park `Unassigned`, which is
 * terminal; too many and the claim waste above grows linearly while buying almost nothing. The
 * profile applies both bounds. `-e DRIVER_VUS=` still wins, for the runs that are about this number.
 */
const REQUESTED_DRIVER_POOL = Number(
    __ENV.DRIVER_VUS || __ENV.VUS || driverPoolFor(profile.peakRate * MIX.order)
);

/**
 * Clamped to what has actually been seeded. A profile whose peak asks for more drivers than exist
 * would otherwise abort in `setup()` — and "the ramp does not run against the standard fixture" is a
 * worse answer than "the ramp runs with the drivers there are, and says so".
 */
export const DRIVER_POOL = Math.max(
    1,
    Math.min(REQUESTED_DRIVER_POOL, fixture.drivers.length || REQUESTED_DRIVER_POOL)
);

export const options = {
    vus: DRIVER_POOL,
    duration: __ENV.DURATION || '2m',
    // The roster below logs in as every seeded driver once. At ~200 ms per PBKDF2 login, a 50-driver
    // fixture needs well over k6's 60 s default.
    setupTimeout: __ENV.SETUP_TIMEOUT || '5m',
    // `driver_claim_hit_rate` is gated in `mixed.js`, not here. Standalone, this script runs against
    // whatever offers happen to exist — usually none, because nothing is placing orders — and a
    // threshold would fail it for correctly finding an idle system.
    thresholds: sloThresholds(),
    tags: { testid: `driver-${runId}` },
};

export function setup() {
    requireFixture('driver.js');
    announce('driver.js', `${DRIVER_POOL} drivers of ${fixture.drivers.length} seeded`);

    prepareRoster();

    return { startedOnUtc: new Date().toISOString() };
}

/**
 * Clock the on-duty drivers **on** and every other seeded driver **off**, before the run starts.
 *
 * ── Why this exists, and it is the most surprising thing in this file ────────────────────────────
 *
 * A seeded driver who is not being driven is not merely idle — it actively *starves assignment*, and
 * the reason is a detail of `RedisDriverLocationStore.FindNearestAvailableAsync`:
 *
 *   * `delivery:drivers:available` is a Redis GEO set with **no per-member TTL**. Anyone who ever
 *     went available stays in it at their last position, forever.
 *   * Freshness lives in a *separate* per-driver key with a 60 s TTL, and the search drops a
 *     candidate whose freshness key has lapsed.
 *   * But `GEOSEARCH` applies `count: CandidateLimit` (10) **before** that filter. So the ten
 *     nearest *members* are selected first, and only then are the stale ones thrown away.
 *
 * With 50 seeded drivers and 8 being driven, the ten nearest members around a restaurant are almost
 * all stale, the filter discards them, and the offer routine sees **no candidates at all** — so it
 * parks the delivery `Unassigned` immediately, which is terminal and waits for a human. Measured on
 * a 3-minute mixed run before this was added: 48 deliveries `Unassigned` against 37 delivered, and
 * **34 of the 48 had an empty `tried_driver_ids`** — never offered to anyone.
 *
 * That is a real platform finding, not a harness artifact: in production every driver who closes the
 * app leaves a permanent geo member, and once those outnumber `CandidateLimit` near a restaurant,
 * orders there stop being assignable. It belongs on Milestone F's list — a pool trimmed on
 * `GoOffline` only, plus a limit applied *after* the freshness filter (or a `ZREM` of stale members
 * during the search) is the fix, and it is production code this milestone deliberately does not
 * write.
 *
 * What the harness can do honestly is not lie about its own world: if a run drives 8 drivers, the
 * other 42 should be clocked off, which is what a real driver who is not working has done. Going
 * offline `ZREM`s the member (`SetDriverAvailabilityCommandHandler`), so the pool ends up containing
 * exactly the drivers this run is driving.
 */
export function prepareRoster() {
    const onDuty = onDutyDrivers(DRIVER_POOL);
    const offDuty = offDutyDrivers(DRIVER_POOL);

    if (REQUESTED_DRIVER_POOL > DRIVER_POOL) {
        // Warned here rather than in init context, which k6 executes once per VU — a ramp would print
        // this several hundred times.
        console.warn(
            `driver.js: profile '${profile.name}' wants ${REQUESTED_DRIVER_POOL} drivers for its ` +
                `peak but the fixture has ${fixture.drivers.length}. Running with ` +
                `${DRIVER_POOL}; the fulfilment half of this run is supply-limited. Re-seed with ` +
                '`--drivers <n>` to lift it.'
        );
    }

    if (onDuty.length < DRIVER_POOL) {
        // Two VUs on one driver race each other: both claim, one gets `NotAssignedDriver`, and the
        // failure looks like the platform's rather than the harness's.
        throw new Error(
            `driver.js: a pool of ${DRIVER_POOL} but only ${fixture.drivers.length} seeded drivers ` +
                '— VUs would share driver identities and race each other. Lower DRIVER_VUS, or ' +
                're-seed with more drivers.'
        );
    }

    for (const driver of offDuty) {
        setAvailability(driver, false);
    }

    let available = 0;

    for (const driver of onDuty) {
        // Before anything else: finish whatever this driver was carrying when a previous run ended.
        // Milestone D runs four profiles back to back, and k6 stops a run mid-iteration by design —
        // so every run leaves a few drivers holding an `Assigned` or `PickedUp` delivery, and a
        // `Busy` driver is `Busy` until that delivery completes. Nothing else in the platform will
        // ever complete it: the customer is not driving, the offer has already been accepted, and
        // `ProcessExpiredOffersJob` only re-offers deliveries that were never claimed.
        //
        // Left alone, that compounds — the second run strands two more, and by the fourth the whole
        // on-duty roster is `Busy`, `setup()` aborts, and the only documented way out is
        // `docker compose down -v` plus a three-minute re-seed. Draining is what the driver would
        // have done, costs two calls per stuck delivery, and is tagged `setup` so it never lands in
        // a journey percentile.
        drain(driver);

        setAvailability(driver, true);

        if (isAvailable(driver)) {
            available += 1;
        }
    }

    console.log(
        `driver.js roster: ${available}/${onDuty.length} on duty and available, ` +
            `${offDuty.length} clocked off`
    );

    if (available === 0) {
        throw new Error(
            'driver.js: no on-duty driver is Available — every one of them is Busy or Offline, ' +
                'and the drain above did not free them. A delivery stuck in a state the driver ' +
                'cannot advance will do that; check `delivery.deliveries` for this roster, or ' +
                'reset with `docker compose down -v` and re-seed.'
        );
    }
}

/**
 * Carry every delivery this driver is still holding through to `Delivered`.
 *
 * `GET /delivery/deliveries` filters on `driver_id` for a non-administrator (`@IsAdmin OR
 * d.driver_id = @UserId`), so this is the one board a driver *can* read: their own accepted work.
 * Only the two live states are drained — an `Offered` delivery is not this driver's yet, and a
 * terminal one needs nothing.
 */
function drain(driver) {
    const token = tokenFor(driver, SCOPE_SETUP);

    const board = get(gatewayUrl('delivery/deliveries?page=1&pageSize=20'), {
        name: 'GET /delivery/deliveries (drain)',
        token,
        scope: SCOPE_SETUP,
        body: { 'body is an array': (json) => Array.isArray(json) },
    });

    if (!Array.isArray(board.json)) {
        return;
    }

    for (const delivery of board.json) {
        if (isStatus(delivery.status, DELIVERY_STATUS, 'Assigned')) {
            step(delivery.id, 'picked-up', token);
        }

        if (isStatus(delivery.status, DELIVERY_STATUS, 'Assigned', 'PickedUp')) {
            step(delivery.id, 'delivered', token);

            console.log(`driver.js: drained ${delivery.id}, left behind by an earlier run`);
        }
    }
}

/** One transition during the drain. Best effort — a delivery cancelled underneath it is a 400. */
function step(deliveryId, path, token) {
    send('POST', gatewayUrl(`delivery/deliveries/${deliveryId}/${path}`), null, {
        name: `POST /delivery/deliveries/:id/${path}`,
        token,
        scope: SCOPE_SETUP,
        status: [204, 400],
    });
}

/**
 * `PATCH availability`. A `400` is the expected answer to a no-op transition — the aggregate refuses
 * Available → Available — so both statuses are declared and neither is a failure.
 */
function setAvailability(driver, available) {
    patch(gatewayUrl('delivery/drivers/me/availability'), { available }, {
        name: 'PATCH /delivery/drivers/me/availability',
        token: tokenFor(driver, SCOPE_SETUP),
        scope: SCOPE_SETUP,
        status: [204, 400],
    });
}

/** Whether a driver really ended up Available — a Busy one silently takes no offers all run. */
function isAvailable(driver) {
    const profile = get(gatewayUrl('delivery/drivers/me'), {
        name: 'GET /delivery/drivers/me',
        token: tokenFor(driver, SCOPE_SETUP),
        scope: SCOPE_SETUP,
    });

    return Boolean(profile.json) && isStatus(profile.json.status, DRIVER_STATUS, 'Available');
}

export default function () {
    driverJourney();
}

/**
 * One driver-app tick: report position, then either progress the delivery being carried or try to
 * pick one up — and then wait until the next tick.
 *
 * **The wait is inside the journey, not in the caller.** This scenario runs under `constant-vus`, an
 * open loop with nothing pacing it, so a VU that returns immediately starts again immediately.
 * Measured, with the sleep left in the standalone `default` and omitted from the composed path: the
 * 8 drivers and 20 kitchens together produced 221 requests per second — 5,796 position reports in
 * three minutes from eight drivers, against the ~400 a real 3-second reporting interval gives — and
 * every percentile in that run described the polling loop instead of the journeys. The tick interval
 * *is* the journey.
 */
export function driverJourney() {
    const driver = driverForThisVu(DRIVER_POOL);
    const token = tokenFor(driver);

    reportPosition(token);

    if (carrying) {
        progress(carrying, token);
    } else {
        claimAnOffer(driver, token);
    }

    sleep(thinkTime(POLL_MIN_SECONDS, POLL_MAX_SECONDS));
}

/**
 * The position report — one call per active driver every few seconds, and by a wide margin the
 * highest-volume write in the system. It deliberately bypasses the aggregate and the outbox (a
 * position is telemetry, not domain state) and writes straight to Redis plus a history row, and it
 * is also what keeps an available driver enrolled in the GEO candidate pool. A driver scenario that
 * skipped it would drop out of assignment the moment its seeded position went stale.
 */
function reportPosition(token) {
    if (!position) {
        // First tick: start near a seeded restaurant, which is where the assignment radius looks.
        const anchor = randomRestaurant();

        position = anchor ? [anchor.latitude, anchor.longitude] : [44.7866, 20.4489];
    }

    position = jitter(position[0], position[1], MOVE_KM);

    post(gatewayUrl('delivery/drivers/me/location'), { latitude: position[0], longitude: position[1] }, {
        name: 'POST /delivery/drivers/me/location',
        token,
        status: 204,
    });

    driverLocationReports.add(1);
}

/** Read the offer board and attempt one claim. See the file header for why this is not a journey. */
function claimAnOffer(driver, token) {
    const offered = readOfferBoard();

    if (offered.length === 0) {
        return;
    }

    // Rebuilt against what is actually on the board, so the set cannot grow across a soak run.
    const stillOffered = {};

    for (const delivery of offered) {
        if (tried[delivery.id]) {
            stillOffered[delivery.id] = true;
        }
    }

    tried = stillOffered;

    // One claim per tick — a driver holding two deliveries is refused by `driver.Reserve()` anyway,
    // and more attempts per pass only multiplies the waste.
    //
    // **Oldest first, and deliberately in lockstep with the other drivers.** The board arrives
    // `created_on_utc DESC`, so this reverses it. Every driver converging on the same delivery looks
    // wasteful and is not: a VU tries any given delivery at most once either way, so the total
    // number of wasted claims is identical — what changes is *when* they happen. Concentrated on the
    // oldest outstanding offer, the driver who actually owns it claims within a tick or two;
    // scattered at random across the board, the owner may not reach it before the 30 s window
    // lapses. Measured on a 3-minute mixed run: choosing at random left **108 deliveries
    // `Unassigned` against 82 delivered**, because `OfferNextAsync` parks a delivery the moment
    // every currently-available candidate has been tried — and with a small pool, that happens after
    // a handful of expiries, permanently, waiting for a human.
    const candidates = offered
        .filter((delivery) => !tried[delivery.id])
        .sort((a, b) => String(a.createdOnUtc).localeCompare(String(b.createdOnUtc)));

    const target = candidates[0];

    if (!target) {
        return;
    }

    tried[target.id] = true;

    const result = send('POST', gatewayUrl(`delivery/deliveries/${target.id}/accept`), null, {
        name: 'POST /delivery/deliveries/:id/accept',
        token,
        // 400 is the expected outcome of a claim on somebody else's offer — `NotAssignedDriver`, or
        // `OfferExpired` if the window lapsed between the board read and the claim. Declared, so
        // `http_req_failed` keeps meaning "unexpected", and counted below so the cost is visible.
        status: [204, 400],
    });

    const won = result.response.status === 204;

    driverClaimHitRate.add(won);

    if (!won) {
        driverClaimsMissed.add(1);

        return;
    }

    driverClaimsWon.add(1);
    carrying = { deliveryId: target.id, acceptedAt: Date.now(), status: 'Assigned' };
}

/**
 * The administrator's view of every delivery, filtered to the offered ones.
 *
 * Tagged `scope: dispatch` — it is harness scaffolding standing in for a missing endpoint, so it
 * stays out of the journey latency SLO while still counting toward the error rate and the checks.
 * The admin token is cached per VU like every other (`lib/auth.js`), so this costs one extra login
 * per driver VU for the whole run, not one per poll.
 */
function readOfferBoard() {
    const board = get(gatewayUrl(`delivery/deliveries?page=1&pageSize=${BOARD_PAGE_SIZE}`), {
        name: 'GET /delivery/deliveries (offer board)',
        token: dispatchToken(),
        scope: SCOPE_DISPATCH,
        body: { 'body is an array': (json) => Array.isArray(json) },
    });

    if (!Array.isArray(board.json)) {
        return [];
    }

    return board.json.filter((delivery) => isStatus(delivery.status, DELIVERY_STATUS, 'Offered'));
}

/** Carry the delivery one step: `Assigned → PickedUp → Delivered`, one transition per tick. */
function progress(delivery, token) {
    const step = delivery.status === 'Assigned'
        ? { path: 'picked-up', to: 'PickedUp' }
        : { path: 'delivered', to: 'Delivered' };

    const result = send('POST', gatewayUrl(`delivery/deliveries/${delivery.deliveryId}/${step.path}`), null, {
        name: `POST /delivery/deliveries/:id/${step.path}`,
        token,
        status: 204,
    });

    if (!result.ok) {
        // The delivery moved underneath this VU — cancelled with its order, most likely. Drop it and
        // go back to the board rather than retrying a transition the domain will keep refusing.
        carrying = null;

        return;
    }

    if (step.to === 'PickedUp') {
        delivery.status = 'PickedUp';

        return;
    }

    deliveriesCompleted.add(1);
    deliveryFulfilmentDuration.add(Date.now() - delivery.acceptedAt);
    carrying = null;
}
