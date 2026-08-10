// Where a run points, and who it authenticates as. Nothing in this tree hardcodes a URL: a script
// asks for `environment.gateway` and gets whichever of the modes below `-e ENV=` selected.
//
// The two compose modes are not cosmetic variants of each other:
//
//   compose       k6 runs *inside* the compose network, so it talks to `fooddeliveryservice.gateway`
//                 on the container port. This is the default for real runs — it takes Docker's host
//                 port-forwarding out of the measurement, and it is the only mode in which the
//                 service DNS names the rest of the stack already uses actually resolve.
//   compose-host  k6 runs on the host against the published ports (:3000 / :18080). Convenient while
//                 writing a script; every number it produces includes the port-forward hop.
//
// `kind` targets the NodePorts the Feature 2.5 cluster publishes (Gateway :8000, Identity :18080).

/**
 * The Gateway is the only address a scenario should normally use — Hard Rule 10 says all external
 * traffic goes through it, and a load test that skips it measures a system nobody runs.
 *
 * `GATEWAY_URL` / `IDENTITY_URL` override an entry so the *same* script can be pointed straight at a
 * service port (`http://fooddeliveryservice.restaurants.api:8080`). That is not the normal mode: it
 * exists because "what does the YARP hop cost?" is a number worth having once, and the only honest
 * way to get it is to run one script both ways.
 */
const environments = {
    compose: {
        gateway: 'http://fooddeliveryservice.gateway:8080',
        identity: 'http://fooddeliveryservice.identity:8080',
    },
    'compose-host': {
        gateway: 'http://localhost:3000',
        identity: 'http://localhost:18080',
    },
    kind: {
        gateway: 'http://localhost:8000',
        identity: 'http://localhost:18080',
    },
};

const DEFAULT_ENV = 'compose';

/** Duende's public client: ROPC, no secret. See `Identity/Config.cs`. */
export const PUBLIC_CLIENT_ID = __ENV.CLIENT_ID || 'fooddeliveryservice-public-client';

/** The scopes every API call needs; `fooddeliveryservice.api` is the audience the services validate. */
export const SCOPE = 'openid profile email fooddeliveryservice.api';

function resolveEnvironment() {
    const name = __ENV.ENV || DEFAULT_ENV;
    const selected = environments[name];

    if (!selected) {
        throw new Error(
            `unknown ENV '${name}' — expected one of: ${Object.keys(environments).join(', ')}`
        );
    }

    return {
        name,
        gateway: trimSlash(__ENV.GATEWAY_URL || selected.gateway),
        identity: trimSlash(__ENV.IDENTITY_URL || selected.identity),
    };
}

function trimSlash(url) {
    return url.endsWith('/') ? url.slice(0, -1) : url;
}

export const environment = resolveEnvironment();

/**
 * The credential a script falls back to when it has no fixture user of its own.
 *
 * Defaults to the compose admin seed (`AdminSeed` in Identity/Users `appsettings.Development.json`),
 * which is the only account guaranteed to exist against a database nobody has seeded yet — that is
 * what lets `smoke.js` run before Milestone B produces `fixtures/seed.json`. The KinD cluster applies
 * ASP.NET Identity's real password rules, so its admin password differs; pass it with
 * `-e LOADTEST_PASSWORD=`.
 *
 * The `LOADTEST_` prefix is not decoration. k6 folds the whole system environment into `__ENV`, and
 * `USERNAME` is set on every Windows machine — a plain `__ENV.USERNAME` silently authenticates as
 * whoever is logged in, every login fails, and the run reports a 100% error rate that looks like a
 * platform fault. Prefix anything whose bare name a shell might already own.
 */
export const credentials = {
    username: __ENV.LOADTEST_USERNAME || 'admin@fooddeliveryservice.com',
    password: __ENV.LOADTEST_PASSWORD || 'admin',
};

/**
 * Identifies one run in every place it shows up: the `X-Correlation-Id` of every request, the Seq
 * query that finds them, and the artifact filenames Milestone E writes.
 *
 * The runner scripts always pass `-e RUN_ID=`. The fallback exists so a bare `k6 run smoke.js` still
 * works, but it is computed in init context — which k6 executes once *per VU* — so a multi-VU run
 * without an explicit RUN_ID can end up with more than one id. Pass it; the scripts do.
 */
export const runId = sanitize(__ENV.RUN_ID || `local${Date.now().toString(36)}`);

/**
 * The correlation id travels in a header that `CorrelationIdMiddleware` only preserves when it is a
 * bounded run of ASCII letters, digits and `-_.:` — anything else is silently replaced by a
 * generated id and the run becomes unfindable in Seq. Cheaper to enforce here than to debug there.
 */
function sanitize(value) {
    return value.replace(/[^A-Za-z0-9\-_.:]/g, '-').slice(0, 40);
}
