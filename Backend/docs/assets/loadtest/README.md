# Load-test graphs

Where the pictures behind the project `README.md` and `docs/load-testing.md` live.

They are committed for one reason: **Prometheus keeps 7 days**, on a volume `docker-compose.yml`
calls disposable. A graph linked from documentation and not exported is a broken image with a
month's notice.

## What is here

| File | Drawn from | Says |
|---|---|---|
| `knee-cliff-vs-plateau.svg` | `g-before-01`, `g-after-01` | Requests served and journey p95 per ramp step, with and without the Gateway's admission control. The cliff at 32 customers/s, and the plateau that replaced it. |
| `round-one-fixes.svg` | `f-before-02`, `f-pipeline-01`, `f-pools-01` | Journey p95, journey p99 and `POST /orders` p95 across round one's three controlled runs. |
| `overload-expressed.svg` | `g-before-01`, `g-after-01` | The same overload said two ways — 32% of a step failing, against 5% deliberately shed. Two panels, two scales, both labelled. |

All three regenerate with **`node scripts/plot.mjs`** from `loadtest/`, reading only
`loadtest/results/published/*.summary.json`. No stack, no Grafana, no network, and byte-identical
output for the same inputs — so a regeneration that changes a file means the data changed. That is
route 3 below, and it is the one the published graphs actually use.

## Producing one

The stack does not install `grafana-image-renderer`, so Grafana cannot render a PNG on its own. Three
supported routes, the third being the one above:

1. **Grafana's share menu**, while the run is still in the retention window. Open
   <http://localhost:3100/d/fds-load>, set the time range to the run (the phase timetable the run
   printed gives the offsets), pick the `Run` variable value for its `testid`, then *Share → Export →
   Save as image* on the panel. Name the file for the run — `ramp-02-client-vs-server.png` — so it can
   be traced back to the summary in `loadtest/results/published/`.
2. **Re-plot the platform's series**, which do not expire: `--prometheus` writes
   `loadtest/results/{run}.platform.json`, holding the same series the dashboard draws, as
   Prometheus `query_range` responses. This is the route for anything server-side — per-service p95,
   outbox backlog, cache hit rate — because a k6 summary does not contain it.
3. **Re-plot the client's series** with `scripts/plot.mjs`, from a published summary. This is the
   route the three files above take, and the only one that works with nothing running.

Whatever ends up here, the environment goes with it — host CPU/RAM, replica count, compose or KinD,
generator co-located or not. Each SVG carries it on its own footer line for that reason, so the
picture stays honest when it is dragged out of the repository and into a slide. A graph without it is
a shape, not a measurement.

Two conventions the script follows and a hand-made addition should too: an **explicit white ground and
dark ink**, because GitHub renders these in both its light and its dark theme and a chart that inherits
the theme is unreadable in one of them; and **stated axes on any panel pair with different scales** —
`overload-expressed.svg` puts 35% above 6%, and says so in the caption rather than letting the shapes
imply a comparison the numbers do not support.
