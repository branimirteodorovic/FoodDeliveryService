// Browse — the read-heavy volume journey.
//
//   GET /restaurants?page=1&pageSize=20  →  GET /restaurants/{id}  →  GET /restaurants/{id}/menu
//
// with think time between the steps, because a customer reads the page before clicking.
//
// **This journey is also an experiment.** Of the three requests it makes, two are cached and one is
// not: `GetRestaurant` and `GetMenu` implement `ICachedQuery`; the **list** — the entry point of
// every browse iteration and, at 70% of the mix, the highest-volume request in the whole run — does
// not. So the `cache_hits_total` / `cache_misses_total` split under browse load, and where
// `app_request_duration_seconds{request="GetRestaurantsQuery"}` sits in the per-request panel, are
// what decides Milestone F #3. Do not "fix" that before the ramp has measured it: a cache added to
// the list query before there is a baseline is a change with no story attached.
//
// Runs standalone (`./run.sh scenarios/browse.js`) and composes into `mixed.js`, which imports
// `browseJourney` and ignores everything else in this file.

import { sleep } from 'k6';
import { runId } from '../config/environments.js';
import { OUTPUT_OPTIONS, summaryFor } from '../config/output.js';
import { sloThresholds } from '../config/thresholds.js';
import { customerForThisVu, tokenFor } from '../lib/actors.js';
import { pickRandom, thinkTime } from '../lib/domain.js';
import { announce, randomRestaurant, requireFixture, restaurantById } from '../lib/fixtures.js';
import { gatewayUrl, get } from '../lib/http.js';
import { browseDuration, browseEmpty } from '../lib/metrics.js';

/** Page size a customer's first screen asks for. Kept at the list query's practical default. */
const PAGE_SIZE = 20;

/** How deep a browsing customer goes. Page 1 is most of real traffic; the tail exercises OFFSET. */
const MAX_PAGE = Number(__ENV.BROWSE_MAX_PAGE || 3);

export const options = {
    vus: Number(__ENV.VUS || 10),
    duration: __ENV.DURATION || '1m',
    thresholds: sloThresholds(),
    tags: { testid: `browse-${runId}` },
    ...OUTPUT_OPTIONS,
};

/** Writes `results/…summary.{json,md}` and the terminal report. See `config/output.js`. */
export const handleSummary = summaryFor('scenarios/browse.js');

export function setup() {
    requireFixture('browse.js');
    announce('browse.js');

    return { startedOnUtc: new Date().toISOString() };
}

export default function () {
    browseJourney();
}

/**
 * One browse. Returns the fixture entry for the restaurant the customer landed on — `order.js`
 * continues straight from here, which is what makes "browse → place order" one funnel rather than
 * two unrelated scripts sharing a fixture.
 *
 * @param {string} [token] reuse a token the caller already holds; otherwise this VU's customer logs
 *   in (once, cached — see `lib/actors.js`).
 * @returns {object|null} `{restaurantId, menuItemIds, latitude, …}` or `null` when the catalogue had
 *   nothing to show or the restaurant is not one this fixture can order from.
 */
export function browseJourney(token) {
    const bearer = token || tokenFor(customerForThisVu());
    const startedAt = Date.now();

    // Not always page 1: the list query has no covering index beyond `ORDER BY name`, and OFFSET
    // paging degrades with depth. A run that only ever asks for page 1 never finds that out.
    const page = 1 + Math.floor(Math.random() * MAX_PAGE);

    const list = get(gatewayUrl(`restaurants?page=${page}&pageSize=${PAGE_SIZE}`), {
        name: 'GET /restaurants',
        token: bearer,
        body: { 'body is an array': (json) => Array.isArray(json) },
    });

    sleep(thinkTime(1, 3));

    const chosen = pickRandom(list.json) || randomRestaurant();

    if (!chosen) {
        browseEmpty.add(true);
        browseDuration.add(Date.now() - startedAt);

        return null;
    }

    browseEmpty.add(false);

    // A page past the end of a small catalogue returns `[]`, and the fixture fallback above then
    // hands back a seeded restaurant. Either shape has an id; only the list rows have `.id`.
    const restaurantId = chosen.id || chosen.restaurantId;

    get(gatewayUrl(`restaurants/${restaurantId}`), {
        // The literal `:id`, never the value — one time series per endpoint, not one per restaurant.
        name: 'GET /restaurants/:id',
        token: bearer,
        body: {
            'has the requested id': (json) => Boolean(json) && json.id === restaurantId,
            'has a name': (json) => Boolean(json) && typeof json.name === 'string' && json.name.length > 0,
        },
    });

    sleep(thinkTime(1, 3));

    const menu = get(gatewayUrl(`restaurants/${restaurantId}/menu`), {
        name: 'GET /restaurants/:id/menu',
        token: bearer,
        body: {
            "is the requested restaurant's menu": (json) => Boolean(json) && json.restaurantId === restaurantId,
            'carries categories': (json) => Boolean(json) && Array.isArray(json.categories),
        },
    });

    browseDuration.add(Date.now() - startedAt);

    sleep(thinkTime(1, 3));

    return landed(restaurantId, menu.json);
}

/**
 * What the customer is now looking at, in the shape `order.js` needs: the fixture entry (for the
 * address coordinates the order payload requires) plus the item ids the **live menu** just returned.
 *
 * Live ids rather than the fixture's, deliberately — the fixture is a snapshot and the menu is the
 * current truth, so ordering from what was just rendered is both more realistic and immune to a
 * fixture that has drifted. `null` when the restaurant is not in the fixture (someone else seeded
 * it) or its menu is empty: `order.js` needs coordinates and orderable items, and inventing either
 * would produce a 400 recorded as a platform failure.
 */
function landed(restaurantId, menu) {
    const seeded = restaurantById(restaurantId);

    if (!seeded) {
        return null;
    }

    const available = (menu && menu.categories ? menu.categories : [])
        .flatMap((category) => category.items || [])
        .filter((item) => item.isAvailable !== false)
        .map((item) => item.id);

    if (available.length === 0) {
        return null;
    }

    return { ...seeded, menuItemIds: available };
}

