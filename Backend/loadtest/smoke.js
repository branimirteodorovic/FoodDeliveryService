// The harness's own test.
//
// Five VUs for thirty seconds over the read path — list, detail, menu — through the Gateway, with
// thresholds armed. It is not a capacity measurement and it is not meant to be one: it is the thing
// every later milestone runs first to prove that auth, tagging, correlation and the pass/fail gate
// all still work before spending twenty minutes on a ramp.
//
// Three properties it must keep:
//   * it runs green against an EMPTY database (the list returns `[]`), so it works before Milestone B
//   * it exits non-zero on a threshold breach, so it can be a gate rather than a report
//   * it measures the platform warm — see `setup()` for why that is not a way of flattering it

import { sleep } from 'k6';
import { environment, credentials, runId } from './config/environments.js';
import { OUTPUT_OPTIONS, summaryFor } from './config/output.js';
import { SCOPE_SETUP, sloThresholds } from './config/thresholds.js';
import { getToken } from './lib/auth.js';
import { gatewayUrl, get, send } from './lib/http.js';
import { hasFixture, fixture, randomRestaurant, requireFixture } from './lib/fixtures.js';

export const options = {
    vus: Number(__ENV.VUS || 5),
    duration: __ENV.DURATION || '30s',
    thresholds: sloThresholds(),
    // Tags every metric the run emits, so Milestone E's Prometheus series can be filtered to one run.
    tags: { testid: `smoke-${runId}` },
    // The bounded label set and the summary's trend statistics — `config/output.js`.
    ...OUTPUT_OPTIONS,
};

/** Writes `results/…summary.{json,md}` and the terminal report. See `config/output.js`. */
export const handleSummary = summaryFor('smoke.js');

/**
 * Preflight, then warm the platform. `setup()` runs to completion before any VU starts, which is
 * exactly the ordering this needs.
 *
 * **Why the warm-up is here and not omitted.** Measured on this stack: the first authenticated
 * request against a freshly started service takes ~2.5 s inside Restaurants (~3.0 s at the Gateway),
 * while the same request in steady state takes 15–40 ms. The cost is the cold path behind
 * `CustomClaimsTransformation` → `IPermissionService` — the MassTransit RPC to Users and its
 * RabbitMQ topology, plus the Redis permission cache being empty. With five VUs all issuing their
 * first request at once, five 3-second samples out of ~70 put p95 at 3 s and fail a run in which
 * every other request was fast.
 *
 * That is a real number and it is worth knowing (it is why Milestone F #4 is on the shortlist), so it
 * is measured rather than hidden: these requests are tagged `scope: setup` and show up in the summary
 * under `{scope:setup}`. They are simply not what the journey SLO is about. Process cold start is a
 * deployment property; the thresholds are about what a user experiences against a running system.
 */
export function setup() {
    // Fail fast and legibly. Without this, a stack that isn't up yet produces thirty seconds of
    // connection errors and a threshold breach whose cause takes a log dive to find.
    const { response } = send('GET', gatewayUrl('health/ready'), null, {
        name: 'GET /health/ready',
        scope: SCOPE_SETUP,
        body: { 'reports Healthy': (_, r) => String(r.body).includes('Healthy') },
    });

    if (response.status !== 200) {
        throw new Error(
            `gateway not ready at ${environment.gateway} (status ${response.status}). ` +
                'Is `docker-compose up -d` finished? Is ENV right for where k6 is running ' +
                `(ENV=${environment.name})?`
        );
    }

    console.log(
        `run '${runId}' · env '${environment.name}' · gateway ${environment.gateway} · ` +
            `identity ${environment.identity} · fixture ${hasFixture ? 'present' : 'absent (empty-catalogue mode)'}`
    );

    // One full journey, on the same credential the VUs use, so the permission cache it populates is
    // the one they hit. The login is tagged `setup` too: against a freshly started Identity the first
    // token request took 7.9 s here, which measures Duende's startup, not the cost of a login.
    const token = getToken(credentials.username, credentials.password, SCOPE_SETUP);

    checkFixture(token);

    journey(token, SCOPE_SETUP, false);

    return { startedOnUtc: new Date().toISOString() };
}

export default function () {
    // One login per VU — cached in lib/auth.js. See the comment there before "optimising" this.
    journey(getToken(credentials.username, credentials.password));
}

/**
 * When Milestone B's seeder has run, prove its fixture is still worth reading before anything is
 * measured against it: the environment matches, and one seeded restaurant's menu still resolves
 * through the API.
 *
 * Skipped entirely without a fixture — `smoke.js` has to stay runnable against an empty database.
 * With one, this is the check that turns "every request 404s" into a one-line failure naming the
 * cause, which is usually that the database was recreated since the fixture was written.
 */
function checkFixture(token) {
    if (!hasFixture) {
        return;
    }

    requireFixture('smoke.js');

    const seeded = randomRestaurant();

    get(gatewayUrl(`restaurants/${seeded.restaurantId}/menu`), {
        name: 'GET /restaurants/:id/menu',
        token,
        scope: SCOPE_SETUP,
        body: {
            'seeded restaurant still resolves': (json) =>
                Boolean(json) && json.restaurantId === seeded.restaurantId,
            'seeded menu items are still on the menu': (json) => {
                const live = new Set(
                    (json.categories || []).flatMap((category) => (category.items || []).map((item) => item.id))
                );

                return seeded.menuItemIds.every((id) => live.has(id));
            },
        },
    });

    console.log(
        `fixture '${fixture.runId}' · ${fixture.restaurants.length} restaurants · ` +
            `${fixture.customers.length} customers · ${fixture.drivers.length} drivers`
    );
}

/**
 * Browse: list → detail → menu, the read path a customer actually walks.
 *
 * @param {string} token
 * @param {string} [scope] threshold bucket; `setup` for the warm-up.
 * @param {boolean} [think] think time between steps. Off for the warm-up, which is not simulating
 *   anyone and only needs to touch each path once.
 */
function journey(token, scope, think = true) {
    const list = get(gatewayUrl('restaurants?page=1&pageSize=20'), {
        name: 'GET /restaurants',
        token,
        scope,
        body: { 'body is an array': (json) => Array.isArray(json) },
    });

    if (think) {
        sleep(randomBetween(1, 3));
    }

    const restaurantId = pickRestaurantId(list.json);

    // No restaurants anywhere: an unseeded database. The list call above already proved the read
    // path works end to end, so the iteration ends here rather than inventing an id to 404 on.
    if (!restaurantId) {
        return;
    }

    get(gatewayUrl(`restaurants/${restaurantId}`), {
        // The literal `:id`, not the value — one time series for the endpoint, not one per restaurant.
        name: 'GET /restaurants/:id',
        token,
        scope,
        body: {
            'has the requested id': (json) => Boolean(json) && json.id === restaurantId,
            'has a name': (json) => Boolean(json) && typeof json.name === 'string' && json.name.length > 0,
        },
    });

    if (think) {
        sleep(randomBetween(1, 3));
    }

    get(gatewayUrl(`restaurants/${restaurantId}/menu`), {
        name: 'GET /restaurants/:id/menu',
        token,
        scope,
        body: {
            'is the requested restaurant\'s menu': (json) => Boolean(json) && json.restaurantId === restaurantId,
            'carries categories': (json) => Boolean(json) && Array.isArray(json.categories),
        },
    });

    if (think) {
        sleep(randomBetween(1, 3));
    }
}

/**
 * Prefer what the list actually returned — that is the real browse behaviour and it keeps the smoke
 * test honest about the data the platform is holding right now. The fixture is the fallback for the
 * window where the catalogue exists but this page of it happens to be empty.
 */
function pickRestaurantId(list) {
    if (Array.isArray(list) && list.length > 0) {
        return list[Math.floor(Math.random() * list.length)].id;
    }

    const seeded = randomRestaurant();

    return seeded ? seeded.restaurantId : null;
}

function randomBetween(min, max) {
    return min + Math.random() * (max - min);
}
