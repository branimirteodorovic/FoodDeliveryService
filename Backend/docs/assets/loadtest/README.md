# Load-test graphs

Where the pictures behind `README.md` and `docs/load-testing.md` live — the Grafana panels a published
number came from.

They are committed for one reason: **Prometheus keeps 7 days**, on a volume `docker-compose.yml`
calls disposable. A graph linked from documentation and not exported is a broken image with a
month's notice.

## Producing one

The stack does not install `grafana-image-renderer`, so Grafana cannot render a PNG on its own and no
script here pretends otherwise. Two supported routes:

1. **Grafana's share menu**, while the run is still in the retention window. Open
   <http://localhost:3100/d/fds-load>, set the time range to the run (the phase timetable the run
   printed gives the offsets), pick the `Run` variable value for its `testid`, then *Share → Export →
   Save as image* on the panel. Name the file for the run — `ramp-02-client-vs-server.png` — so it can
   be traced back to the summary in `loadtest/results/published/`.
2. **Re-plot from the data**, which does not expire: `--prometheus` writes
   `loadtest/results/{run}.platform.json`, holding the same series the dashboard draws, as
   Prometheus `query_range` responses.

Whatever ends up here, the environment goes with it — host CPU/RAM, replica count, compose or KinD,
generator co-located or not. A graph without that is a shape, not a measurement.
