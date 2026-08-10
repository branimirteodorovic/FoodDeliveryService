// Reads the dataset Milestone B's seeder produces.
//
// `open()` runs in init context only, which is exactly right: the file is read once per VU at
// startup and never touched during the test, so nothing in the hot path does I/O.
//
// Milestone A ships this file empty-tolerant on purpose. `smoke.js` has to run green against a
// database nobody has seeded — that is what lets the harness be reviewed and merged before the
// seeder exists, and it stays useful afterwards as the "is the stack even up?" check.

import { environment } from '../config/environments.js';

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

    if (fixture.environment && fixture.environment !== environment.name) {
        // Ids are per-database. A fixture seeded against compose is meaningless against KinD, and the
        // failure mode is a run where every request 404s.
        throw new Error(
            `fixtures/seed.json was seeded against '${fixture.environment}' but ENV is ` +
                `'${environment.name}' — re-seed, or point at the environment it belongs to.`
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

/** A driver credential assigned round-robin by VU, so two VUs never drive the same driver. */
export function driverForVu(vuId) {
    if (fixture.drivers.length === 0) {
        return null;
    }

    return fixture.drivers[(vuId - 1) % fixture.drivers.length];
}

function pick(items) {
    if (!items || items.length === 0) {
        return null;
    }

    return items[Math.floor(Math.random() * items.length)];
}
