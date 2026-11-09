# Actual application screenshots

The five PNGs in `docs/screenshots/` are captures of the real running React/ASP.NET Core/PostgreSQL application, using only the Development seed. They are not mockups and do not use a developer's existing database.

From `frontend/`, with Docker running and the reserved test ports free:

```sh
npm ci
npx playwright install chromium
npm run screenshots
```

The script owns a disposable `fleetflow-e2e` Compose stack on API 5180/PostgreSQL 5434 and a Vite server on 5174. It applies migrations, fictional logistics and generated Identity accounts, signs in as Dispatcher, captures dashboard, shipment list, one real Planned shipment's detail, vehicle directory and mobile dashboard, then removes only its own stack/volume. Node 24's native TypeScript stripping imports the same setup used by Playwright. It refuses an existing test project or preview listener.

Desktop captures use 1440×1040, en-GB, Europe/Vienna and reduced motion. The detail is a full-page capture; other screens preserve the actual viewport. Mobile uses 390×844. Dates and KPIs are relative to capture time; no CSS/data alteration is used to invent a fuller dashboard. The API remains on loopback throughout.

`npm run screenshots -- --preview` optionally keeps that disposable preview open until Enter. Use the normal development stack for persistent work. If interrupted forcefully, follow the disposable cleanup instructions in the testing guide. Review every screenshot for fictional data, legibility, clipping and unexpected debug/error states before committing it.

These images do not claim real mobile-device testing, a deployed demo or performance benchmarks. Regenerate deliberately when appearance or seed data changes; do not silently replace them with generated artwork.

## M7 public entry

`portfolio-entry.png` was captured from the actual locally built Production SPA at `https://localhost:5443/demo`, using the separate fictional portfolio stack and no inference credentials. It contains real available-slot counts at capture time. The local self-signed certificate is a rehearsal detail; no public site is deployed.

To recapture only the public entry after starting the local portfolio stack: from `frontend`, run `node scripts/portfolio-production-smoke.mjs --accept-local-test-certificate --screenshot`. It uses the built SPA, verifies real role sessions in all three browsers and never generates AI.
