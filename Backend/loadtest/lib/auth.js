// ROPC token acquisition against Duende, with per-VU caching.
//
// This is the single most important file in Milestone A, for one reason: ASP.NET Identity hashes
// passwords with PBKDF2 and deliberately burns CPU doing it. A script that logs in every iteration
// turns the whole exercise into a password-hashing benchmark of one service — Identity pins a core,
// everything queues behind it, and every run "finds" the same bottleneck for a reason that has
// nothing to do with the platform.
//
// So: one login per VU, cached until the token actually expires.

import http from 'k6/http';
import { fail } from 'k6';
import exec from 'k6/execution';
import { PUBLIC_CLIENT_ID, SCOPE, environment } from '../config/environments.js';
import { phaseTag } from '../config/profiles.js';
import { SCOPE_AUTH } from '../config/thresholds.js';

/**
 * Refresh this far before the stated expiry. A token that expires mid-request produces a 401 the
 * run records as a platform failure, which is a lie about the platform.
 */
const EXPIRY_SKEW_MS = 60_000;

/**
 * VU-local. k6 gives every VU its own JavaScript runtime, so module state is per-VU by construction
 * — this map is exactly "the tokens this one virtual user holds", never a shared pool.
 */
const tokens = {};

/**
 * A bearer token for `username`, logging in only if there isn't a live one cached for this VU.
 *
 * @param {string} username
 * @param {string} password
 * @param {string} [scope] threshold bucket. Defaults to `auth`; `setup()` passes `setup` for its
 *   warm-up login, because the first token request against a freshly started Identity measures
 *   Duende's startup and not the cost of issuing a token.
 * @returns {string} the access token
 */
export function getToken(username, password, scope = SCOPE_AUTH) {
    const cached = tokens[username];

    if (cached && cached.expiresAt > Date.now()) {
        return cached.accessToken;
    }

    const response = http.post(
        `${environment.identity}/connect/token`,
        {
            client_id: PUBLIC_CLIENT_ID,
            scope: SCOPE,
            grant_type: 'password',
            username,
            password,
        },
        {
            // Its own tag *and* its own threshold (config/thresholds.js): login cost stays a visible
            // line instead of being smeared into the journey percentiles. The phase tag is empty
            // unless a staged profile is running, and it is here for one reason: without it
            // `http_req_failed{phase:…}` would cover every request in a step *except* the logins,
            // and a step whose failures were all 5xx tokens would read as clean.
            tags: { name: 'POST /connect/token', scope, ...phaseTag() },
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        }
    );

    if (response.status !== 200) {
        const detail =
            `token request failed for '${username}': ${response.status} ${truncate(response.body)} ` +
            `(identity=${environment.identity}, client_id=${PUBLIC_CLIENT_ID})`;

        // A rejected credential is a configuration error, not a measurement, and it must stop the
        // whole run rather than the iteration. Measured the hard way: failing only the iteration
        // makes k6 immediately start the next one, which logs in again — five VUs turned one wrong
        // password into ~7,000 PBKDF2 verifications in thirty seconds, a denial-of-service against
        // Identity dressed up as a load test, and a summary claiming a 100% platform error rate.
        if (response.status === 400 || response.status === 401) {
            exec.test.abort(detail);
        }

        // Anything else (a 5xx, a timeout) is the platform genuinely struggling under load, which is
        // exactly what the run is here to record. Fail the iteration and let the test continue.
        fail(detail);
    }

    const body = response.json();

    if (!body || !body.access_token) {
        fail(`token response for '${username}' carried no access_token: ${truncate(response.body)}`);
    }

    tokens[username] = {
        accessToken: body.access_token,
        expiresAt: Date.now() + Math.max(body.expires_in * 1000 - EXPIRY_SKEW_MS, 0),
    };

    return body.access_token;
}

/** Drops the cached token, so the next `getToken` logs in again. For testing the auth path itself. */
export function forgetToken(username) {
    delete tokens[username];
}

function truncate(body) {
    if (!body) {
        return '<empty body>';
    }

    return body.length > 300 ? `${body.slice(0, 300)}…` : body;
}
