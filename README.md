# YoPay

Payment automation and reconciliation for Bangladeshi merchants. Money goes to the
merchant's own wallet; YoPay detects the incoming payment, matches it to an invoice,
confirms the order and keeps the books straight. YoPay never holds, transfers or
settles funds.

Phase T0-T2 of the engineering roadmap: solution skeleton, domain model and
persistence. See `docs/` in the planning PDFs for the full phase plan.

## Layout

    src/YoPay.Domain          entities, enums, invoice state machine (no dependencies)
    src/YoPay.Application     business rules and ports: IPaymentRail, match rules,
                              invoice window, heartbeat policy, ISecretProtector
    src/YoPay.Contracts       wire DTOs only - API, webhook, device ingest (SDKs generate from here)
    src/YoPay.Infrastructure  EF Core 10, DbContext, configurations, migrations
    src/YoPay.Api             merchant REST API                       (filled in T4)
    src/YoPay.Ingest.Api      device-only ingest endpoint             (filled in T6)
    src/YoPay.Checkout        hosted payment page                     (filled in T5)
    src/YoPay.Worker          parser, matcher, webhook dispatcher     (filled in T7-T9)
    src/YoPay.Scheduler       session expiry, retries, billing        (filled in T12)
    src/YoPay.Application     ... plus Parsing: bKash templates and the message parser
    tests/YoPay.UnitTests     state machine, match rules, window, heartbeat, parser
    tests/fixtures/bkash      golden corpus of real bKash messages
    docs/                     T0A Android capture spike brief

## How work lands

One phase, one branch, one pull request, CI green before merge:

    git checkout -b phase/t3-security
    ... implement ...
    dotnet build && dotnet test
    git add -A && git commit -m "T3: secret vault, nonce store, outbound URL guard"
    git push -u origin phase/t3-security && gh pr create --fill

T0A (the Android capture spike) runs alongside T0 and is never merged - it answers a
question and is thrown away.

## Getting started

    docker compose up -d
    dotnet restore
    dotnet build
    dotnet tool install --global dotnet-ef
    dotnet ef migrations add InitialCreate -p src/YoPay.Infrastructure -s src/YoPay.Api
    dotnet ef database update -p src/YoPay.Infrastructure -s src/YoPay.Api
    dotnet test

The design-time factory reads `ConnectionStrings__Postgres` from the environment and
falls back to the local compose database, so migrations work without starting the API.

## Rules that do not bend

- YoPay never takes custody of funds.
- Correctness lives in PostgreSQL: unique constraints and a single transaction are the
  authority. Redis and any message broker are optimisations, never correctness.
- Matching uses the device-reported event time, never `now()`, so a phone that was
  offline for hours still matches correctly when it uploads its backlog.
- No customer or merchant MFS PIN or OTP is ever requested, stored or forwarded.
