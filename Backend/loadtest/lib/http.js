// The HTTP wrappers every scenario goes through. They exist to enforce the three things a load
// script gets wrong exactly once each, and then quietly keeps getting wrong for the rest of the
// project.

import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { environment, runId } from '../config/environments.js';
import { SCOPE_JOURNEY } from '../config/thresholds.js';

/** Matches `CorrelationHeaders.CorrelationId` in `Common.Presentation/Correlation`. */
const CORRELATION_HEADER = 'X-Correlation-Id';

const PROBLEM_CONTENT_TYPE = 'application/problem+json';

/**
 * One correlation id per iteration.
 *
 * The Gateway preserves an inbound value (Telemetry D) and Telemetry G carries it across the
 * outbox/inbox boundary onto the `correlation_id` column — so a single Seq query
 * (`CorrelationId like 'loadtest-<runId>-%'`) pulls the whole asynchronous fan-out of one synthetic
 * order, including the legs that happen seconds later in another service. During the Milestone F
 * bottleneck hunt that is worth more than any dashboard.
 */
export function correlationId() {
    // Guarded because `setup()` and `teardown()` run outside the VU stage, where k6 refuses to answer
    // either question. A preflight request still deserves the run's prefix, so it gets `0-0`.
    let vu = 0;
    let iteration = 0;

    try {
        vu = exec.vu.idInTest;
        iteration = exec.scenario.iterationInTest;
    } catch (_) {
        // setup/teardown — keep the zeros.
    }

    return `loadtest-${runId}-${vu}-${iteration}`;
}

/** `GET url`. See {@link send} for options. */
export function get(url, options) {
    return send('GET', url, null, options);
}

/** `POST url` with a JSON body. See {@link send} for options. */
export function post(url, body, options) {
    return send('POST', url, JSON.stringify(body), {
        ...options,
        headers: { 'Content-Type': 'application/json', ...(options && options.headers) },
    });
}

/** `PATCH url` with a JSON body. See {@link send} for options. */
export function patch(url, body, options) {
    return send('PATCH', url, JSON.stringify(body), {
        ...options,
        headers: { 'Content-Type': 'application/json', ...(options && options.headers) },
    });
}

/** Resolves a path against the Gateway — the address every scenario should be using. */
export function gatewayUrl(path) {
    return `${environment.gateway}/${path.replace(/^\//, '')}`;
}

/**
 * Sends one request, tags it, correlates it, and checks it.
 *
 * @param {string} method
 * @param {string} url
 * @param {string|null} payload
 * @param {object} options
 * @param {string} options.name   REQUIRED. The bounded tag value this request is recorded under.
 * @param {string} [options.token] bearer token.
 * @param {number} [options.status] expected status code, default 200.
 * @param {Object<string, function(any, object): boolean>} [options.body]
 *        body-shape checks, keyed by description. The predicate receives the parsed JSON (or `null`
 *        if the body was not JSON) and the raw response.
 * @param {string} [options.scope] threshold bucket, default `journey`.
 * @param {object} [options.tags] extra tags. Keep the values bounded.
 * @param {object} [options.headers]
 * @returns {{response: object, ok: boolean, json: any}}
 */
export function send(method, url, payload, options = {}) {
    const {
        name,
        token,
        status = 200,
        body = {},
        scope = SCOPE_JOURNEY,
        tags = {},
        headers = {},
    } = options;

    // Bounded tag cardinality, enforced rather than encouraged. Without an explicit name k6 tags by
    // full URL, so every restaurant id becomes its own time series — the same rule CLAUDE.md states
    // for the server side, and it bites harder here because Milestone E ships these series into the
    // platform's own Prometheus.
    if (!name) {
        throw new Error(
            `${method} ${url}: every request needs an explicit \`name\` — e.g. 'GET /restaurants/:id'. ` +
                'Without it k6 tags by full URL and one run creates a time series per id.'
        );
    }

    const params = {
        tags: { name, scope, ...tags },
        headers: {
            [CORRELATION_HEADER]: correlationId(),
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
            ...headers,
        },
    };

    const response = http.request(method, url, payload, params);

    let parsed;
    const json = () => {
        if (parsed === undefined) {
            try {
                parsed = response.json();
            } catch (_) {
                parsed = null;
            }
        }

        return parsed;
    };

    const checks = {
        [`${name} → ${status}`]: (r) => r.status === status,

        // A `200 OK` carrying a ProblemDetails body is an application failure that `http_req_failed`
        // happily counts as a success. `ApiResults.Problem` shouldn't produce one, and this check is
        // how we find out the day something does.
        [`${name} → not a problem document`]: (r) =>
            r.status !== status ||
            !String(r.headers['Content-Type'] || '').includes(PROBLEM_CONTENT_TYPE),
    };

    for (const [description, predicate] of Object.entries(body)) {
        // Body shape is only meaningful on the expected status — a 500's body is not the contract.
        checks[`${name} → ${description}`] = (r) => r.status === status && predicate(json(), r);
    }

    const ok = check(response, checks, { name, scope });

    return { response, ok, get json() { return json(); } };
}
