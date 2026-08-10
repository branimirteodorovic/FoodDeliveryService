// Thresholds are the pass/fail gate, not decoration: k6 exits non-zero when one is breached, which
// is what makes a run something CI or a reviewer can trust rather than a wall of numbers someone has
// to interpret.
//
// Milestone D adds per-profile overrides on top of this shared block.

/**
 * Journey latency is measured *separately from login*, via the `scope` tag `lib/http.js` and
 * `lib/auth.js` put on every request.
 *
 * The reason is ASP.NET Identity: `POST /connect/token` verifies a PBKDF2 hash and deliberately
 * burns CPU doing it, so it is the most expensive endpoint in the system by an order of magnitude.
 * Averaged into `http_req_duration` it drags the journey percentiles around for a reason that has
 * nothing to do with the platform under test. Split out, login cost is its own visible line — which
 * is itself a capacity fact worth publishing (Milestone F #6).
 */
export const SCOPE_JOURNEY = 'journey';
export const SCOPE_AUTH = 'auth';
export const SCOPE_SETUP = 'setup';

/**
 * Requests **no real client would ever make** — today, exactly one: `driver.js` polling the
 * administrator's delivery board to find out which deliveries are currently offered.
 *
 * It exists because the platform has no per-driver "my offers" read model (see `scenarios/driver.js`)
 * — so it is harness scaffolding standing in for a push channel, not a journey. It still loads the
 * platform, so it stays inside `http_req_failed` and `checks`; it is kept out of the journey latency
 * SLO because a threshold on it would be a threshold on the workaround.
 */
export const SCOPE_DISPATCH = 'dispatch';

// Where these numbers stand today. They are budgets, not measurements — Milestone D's baseline run
// is what turns them into numbers with evidence behind them. For scale, `smoke.js` at 5 VUs against
// a warm compose stack (generator co-located, 30 s) measures:
//
//   journey  p95  33 ms   p99  81 ms   (list/detail/menu through the Gateway)
//   auth     p95 643 ms                (5 concurrent logins; PBKDF2, and it shows)
//
// So the journey budget has an order of magnitude of headroom and the auth budget about 3×. Both are
// deliberate at this stage: a smoke test that fails on ordinary host noise gets ignored within a week.
const DEFAULTS = {
    journeyP95: 500,
    journeyP99: 1500,
    // Token issuance is CPU-bound by design; it gets a budget of its own rather than an exemption.
    authP95: 2000,
    errorRate: 0.01,
    checkRate: 0.99,
    // Milestone D's ramp profile sets this: past the knee a run has already answered its question,
    // and ten more minutes of recording zeros adds nothing.
    abortOnFail: false,
    delayAbortEval: '30s',
};

/**
 * The shared SLO block, as a k6 `thresholds` object.
 *
 * @param {object} [overrides] any of the DEFAULTS above.
 */
export function sloThresholds(overrides = {}) {
    const o = { ...DEFAULTS, ...overrides };

    const errorRate = {
        threshold: `rate<${o.errorRate}`,
        abortOnFail: o.abortOnFail,
        delayAbortEval: o.delayAbortEval,
    };

    return {
        // Every request in the run, including setup: a broken preflight must not look like a pass.
        http_req_failed: [errorRate],

        [`http_req_duration{scope:${SCOPE_JOURNEY}}`]: [
            `p(95)<${o.journeyP95}`,
            `p(99)<${o.journeyP99}`,
        ],
        [`http_req_duration{scope:${SCOPE_AUTH}}`]: [`p(95)<${o.authP95}`],

        // A `200 OK` carrying a ProblemDetails body is still an application failure. `http_req_failed`
        // counts status codes and would call it a success; the checks in `lib/http.js` look at the
        // body, and this is the threshold that makes them matter.
        checks: [`rate>${o.checkRate}`],
    };
}
