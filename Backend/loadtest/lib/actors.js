// Who this VU is, and its token.
//
// One rule holds the whole file together: **a VU keeps one identity for its entire life.** k6 gives
// every VU its own JavaScript runtime, `lib/auth.js` caches tokens in that runtime, and ASP.NET
// Identity burns CPU on PBKDF2 for every login — so a VU that picked a random fixture user per
// iteration would log in per iteration and turn the run into a password-hashing benchmark of one
// service. Identity would be the bottleneck in every result, for a reason that has nothing to do
// with the platform.
//
// So identities are assigned round-robin by VU id (`lib/fixtures.js`), which is stable, collision-
// free within a scenario, and reproducible between runs.

import exec from 'k6/execution';
import { credentials } from '../config/environments.js';
import { getToken } from './auth.js';
import { customerForVu, driverForVu, managerForVu } from './fixtures.js';

/** The customer this VU is. */
export function customerForThisVu() {
    return required(customerForVu(vuId()), 'customers');
}

/**
 * The driver this VU is, taken from the first `poolSize` seeded drivers — the on-duty roster
 * `scenarios/driver.js` clocks on before the run starts.
 */
export function driverForThisVu(poolSize) {
    return required(driverForVu(vuId(), poolSize), 'drivers');
}

/** The restaurant manager this VU is, plus the restaurant they run. */
export function managerForThisVu() {
    return required(managerForVu(vuId()), 'restaurants');
}

/**
 * A bearer token for a fixture actor — `{email, password}` from any of the above.
 *
 * @param {object} actor
 * @param {string} [scope] threshold bucket. Defaults to `auth`, the journey login. Pass `setup` for
 *   logins a `setup()` makes on behalf of accounts no VU will ever be: `driver.js` clocks a whole
 *   seeded roster on and off, and measured here, those 50 logins pushed `{scope:auth}` p95 from
 *   1.7 s to 3.4 s — a number describing the harness's preparation, sitting in the SLO that is
 *   supposed to describe a customer signing in.
 */
export function tokenFor(actor, scope) {
    return getToken(actor.email, actor.password, scope);
}

/**
 * The administrator token.
 *
 * Wanted by exactly one caller — `driver.js`, for the offer board — and it is not a journey: no real
 * driver app holds admin credentials. See that file for why the workaround exists. Defaults to the
 * `config/environments.js` credential, which is the compose admin seed.
 */
export function dispatchToken() {
    return getToken(credentials.username, credentials.password);
}

/**
 * How many distinct identities a scenario may run before two VUs share one. Scenarios assert
 * against this in `setup()`: a shared *customer* is only unrealistic, but two VUs driving the same
 * *driver* race each other's offers and produce failures that look like the platform's.
 */
export function poolSizes(fixture) {
    return {
        customers: fixture.customers.length,
        drivers: fixture.drivers.length,
        restaurants: fixture.restaurants.length,
    };
}

function vuId() {
    return exec.vu.idInTest;
}

function required(actor, kind) {
    if (!actor) {
        throw new Error(
            `fixtures/seed.json has no ${kind}. Seed first:\n` +
                '  dotnet run --project tools/FoodDeliveryService.LoadTest.Seeder'
        );
    }

    return actor;
}
