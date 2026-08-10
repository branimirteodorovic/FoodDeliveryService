// The platform's wire format, in one place: the two status enums the journeys branch on, and the
// request payloads they build. Scripts import from here rather than inlining a magic number, so the
// day an enum gains a member there is exactly one file to change.

/**
 * `Modules.Orders.Domain.Orders.OrderStatus`. Deliberately starts at 1 — the enum does.
 */
export const ORDER_STATUS = {
    Pending: 1,
    Accepted: 2,
    Rejected: 3,
    Preparing: 4,
    ReadyForPickup: 5,
    OutForDelivery: 6,
    Delivered: 7,
    Cancelled: 8,
};

/** An order in one of these is finished — nothing further will move it, so stop polling. */
export const TERMINAL_ORDER_STATUSES = ['Rejected', 'Delivered', 'Cancelled'];

/** `Modules.Delivery.Domain.Deliveries.DeliveryStatus`. Starts at 0. */
export const DELIVERY_STATUS = {
    Pending: 0,
    Offered: 1,
    Assigned: 2,
    PickedUp: 3,
    Delivered: 4,
    Unassigned: 5,
    Cancelled: 6,
};

export const TERMINAL_DELIVERY_STATUSES = ['Delivered', 'Cancelled'];

/**
 * `Modules.Delivery.Domain.Drivers.DriverStatus`. Starts at 1, and only an **Available** driver who
 * is also reporting a position is an assignment candidate.
 */
export const DRIVER_STATUS = {
    Offline: 1,
    Available: 2,
    Busy: 3,
};

/**
 * True when `value` — as it came off the wire — is any of `names`.
 *
 * **Why this is tolerant of both forms.** No host registers a `JsonStringEnumConverter`, so
 * System.Text.Json serialises these enums as *numbers* today and that is what a script actually
 * receives. But that is a default, not a contract: the day somebody adds the converter for
 * readability, every `status === 5` in this tree becomes silently false and the journeys stop
 * advancing while every request still returns `200`. That failure is invisible in a load summary —
 * throughput stays high, orders just never reach `Delivered` — so the comparison is written once,
 * here, and accepts either form.
 *
 * @param {number|string} value the `status` field from a response
 * @param {Object<string, number>} table {@link ORDER_STATUS} or {@link DELIVERY_STATUS}
 * @param {...string} names status names to match
 */
export function isStatus(value, table, ...names) {
    return names.some((name) => value === table[name] || value === name);
}

/** The status name for logging and metric tags. Falls back to the raw value if it is unknown. */
export function statusName(value, table) {
    if (typeof value === 'string') {
        return value;
    }

    const match = Object.keys(table).find((name) => table[name] === value);

    return match || `unknown(${value})`;
}

/**
 * The delivery address for an order at `restaurant`.
 *
 * Pinned near the restaurant on purpose. `PlaceOrderCommandValidator` requires the coordinate, and
 * the Delivery service's assignment routine searches a radius **around the restaurant** for
 * available drivers — so a drop-off in another city does not break assignment, but a *fixture*
 * seeded that way would, and keeping the pin local keeps the generated world coherent with the
 * drivers the seeder positioned. `spreadKm` scatters the pins so 500 customers are not all at the
 * same three coordinates.
 */
export function deliveryAddressFor(restaurant, spreadKm = 2) {
    const [latitude, longitude] = jitter(restaurant.latitude, restaurant.longitude, spreadKm);

    return {
        street: `Load Test Street ${Math.floor(Math.random() * 200) + 1}`,
        city: restaurant.city,
        postalCode: restaurant.postalCode,
        country: restaurant.country,
        notes: null,
        latitude,
        longitude,
    };
}

/**
 * A coordinate up to `km` from the given one. Crude equirectangular maths — at city scale the error
 * is metres, and nothing here needs better than that.
 */
export function jitter(latitude, longitude, km) {
    const latitudeDegrees = km / 111;
    const longitudeDegrees = km / (111 * Math.max(Math.cos((latitude * Math.PI) / 180), 0.1));

    return [
        clamp(latitude + (Math.random() * 2 - 1) * latitudeDegrees, -90, 90),
        clamp(longitude + (Math.random() * 2 - 1) * longitudeDegrees, -180, 180),
    ];
}

/** Uniform think time, in seconds — the argument `sleep()` wants. */
export function thinkTime(min, max) {
    return min + Math.random() * (max - min);
}

/** A random element, or `null` for an empty/absent array. */
export function pickRandom(items) {
    if (!items || items.length === 0) {
        return null;
    }

    return items[Math.floor(Math.random() * items.length)];
}

/** Up to `count` random elements, without repetition. */
export function pickSome(items, count) {
    const pool = [...(items || [])];
    const chosen = [];

    while (chosen.length < count && pool.length > 0) {
        chosen.push(...pool.splice(Math.floor(Math.random() * pool.length), 1));
    }

    return chosen;
}

function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
}
