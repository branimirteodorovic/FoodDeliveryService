// Reads the dataset Milestone B's seeder produces.
//
// `open()` runs in init context only, which is exactly right: the file is read once per VU at
// startup and never touched during the test, so nothing in the hot path does I/O.
//
// Milestone A ships this file empty-tolerant on purpose. `smoke.js` has to run green against a
// database nobody has seeded — that is what lets the harness be reviewed and merged before the
// seeder exists, and it stays useful afterwards as the "is the stack even up?" check.

import { environment, runId } from '../config/environments.js';

const FIXTURE_PATH = '../fixtures/seed.json';

const EMPTY = { runId: null, generatedOnUtc: null, environment: null, restaurants: [], customers: [], drivers: [] };

function load() {
    try {
        const parsed = JSON.parse(open(FIXTURE_PATH));

        return { ...EMPTY, ...parsed };
    } catch (_) {
        // Absent or unreadable. Not an error here — callers ask `hasFixture` and degrade.
        return EMPTY;
    }
}

export const fixture = load();

/** True when Milestone B's seeder has run and left a usable dataset behind. */
export const hasFixture = fixture.restaurants.length > 0;

/**
 * Warns once, in init context, when a scenario that wants seeded data hasn't got any — with the
 * command that fixes it. A run that silently measures an empty catalogue is worse than one that
 * fails: the numbers look excellent and mean nothing.
 */
export function requireFixture(scenarioName) {
    if (!hasFixture) {
        throw new Error(
            `${scenarioName} needs fixtures/seed.json and none was found. Run the seeder first:\n` +
                '  dotnet run --project tools/FoodDeliveryService.LoadTest.Seeder'
        );
    }

    if (fixture.environment && stackOf(fixture.environment) !== stackOf(environment.name)) {
        // Ids are per-database. A fixture seeded against compose is meaningless against KinD, and the
        // failure mode is a run where every request 404s.
        throw new Error(
            `fixtures/seed.json was seeded against '${fixture.environment}' but ENV is ` +
                `'${environment.name}' — re-seed, or point at the environment it belongs to.`
        );
    }
}

/**
 * Which *database* an environment name addresses. `compose` and `compose-host` are the same stack
 * reached from two places — the seeder runs on the host while the generator runs in the network —
 * so a fixture written by one is valid for the other. Only `kind` is a different database.
 */
function stackOf(name) {
    return name === 'compose-host' ? 'compose' : name;
}

/**
 * One line in `setup()` naming the run and the world it is about to drive. Every scenario prints it
 * — a summary six weeks later is worth much less if nobody can tell which fixture produced it.
 */
export function announce(scenario, extra = '') {
    console.log(
        `${scenario} · run '${runId}' · env '${environment.name}' · fixture '${fixture.runId}' · ` +
            `${fixture.restaurants.length} restaurants · ${fixture.customers.length} customers · ` +
            `${fixture.drivers.length} drivers${extra ? ` · ${extra}` : ''}`
    );

    if (__ENV.PROFILE && scenario !== 'mixed.js') {
        // A profile shapes the *mix* — its stages and per-phase thresholds only mean anything applied
        // to all five journeys at once. A single journey run with `--profile ramp` keeps its own
        // `vus`/`duration` and silently produces a flat run named after a ramp, which is the kind of
        // artifact that gets quoted a month later by someone who was not here.
        console.warn(
            `${scenario}: PROFILE='${__ENV.PROFILE}' is ignored outside scenarios/mixed.js — this ` +
                'run uses this script\'s own VUS/DURATION. Use `--vus`/`--duration`, or run mixed.js.'
        );
    }
}

/** A random seeded restaurant, or `null` when unseeded. */
export function randomRestaurant() {
    return pick(fixture.restaurants);
}

/** A random seeded customer credential, or `null` when unseeded. */
export function randomCustomer() {
    return pick(fixture.customers);
}

/** The seeded restaurant with this id, or `null` — how a browse result becomes an order payload. */
export function restaurantById(restaurantId) {
    return fixture.restaurants.find((restaurant) => restaurant.restaurantId === restaurantId) || null;
}

/**
 * A driver credential assigned round-robin by VU, so two VUs never drive the same driver.
 *
 * The same round-robin serves customers and managers below, and the rule it depends on is worth
 * stating once: k6 VU ids are unique **across the whole test**, so two VUs in one scenario always
 * land on different entries — *provided the scenario's VU count does not exceed the pool*. Past
 * that, two VUs share an identity, and for a driver that means two iterations racing each other's
 * offers. `scenarios/*.js` check this in `setup()` rather than leaving it to be discovered in the
 * results.
 *
 * `poolSize` bounds the mapping to the **first N** seeded drivers rather than spreading over all of
 * them. That is what makes the on-duty roster knowable in `setup()`, which runs before any VU exists
 * and therefore cannot ask a VU which driver it will be — see `onDutyDrivers` below and
 * `scenarios/driver.js` for what the roster is for.
 */
export function driverForVu(vuId, poolSize) {
    return roundRobin(onDutyDrivers(poolSize), vuId);
}

/** The drivers a run of `poolSize` VUs will actually drive: the first `poolSize` seeded ones. */
export function onDutyDrivers(poolSize) {
    return fixture.drivers.slice(0, poolSize || fixture.drivers.length);
}

/** The seeded drivers such a run will *not* drive. */
export function offDutyDrivers(poolSize) {
    return fixture.drivers.slice(poolSize || fixture.drivers.length);
}

/**
 * A customer credential assigned round-robin by VU — **stable for the VU's whole life**, which is
 * the point. Picking a random customer per iteration would mean a fresh ROPC login per iteration
 * (`lib/auth.js` caches per account per VU), and the run would degenerate into the PBKDF2 benchmark
 * that whole file exists to prevent.
 */
export function customerForVu(vuId) {
    return roundRobin(fixture.customers, vuId);
}

/**
 * A seeded restaurant's manager, round-robin by VU — the actor `scenarios/restaurant.js` runs as.
 * Returns the restaurant too, because a manager's whole world is that one restaurant.
 */
export function managerForVu(vuId) {
    const restaurant = roundRobin(fixture.restaurants, vuId);

    if (!restaurant) {
        return null;
    }

    return {
        email: restaurant.managerEmail,
        password: restaurant.managerPassword,
        restaurant,
    };
}

function roundRobin(items, vuId) {
    if (!items || items.length === 0) {
        return null;
    }

    return items[(vuId - 1) % items.length];
}

function pick(items) {
    if (!items || items.length === 0) {
        return null;
    }

    return items[Math.floor(Math.random() * items.length)];
}
