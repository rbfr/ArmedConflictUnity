# `_plans/` — work in progress

One file per piece of work. A plan says **what is next and in what order**; the design docs at the
repo root say **what is true and why**. When a plan's work ships, its conclusions move into the
relevant design doc or `HANDOVER.md` and the plan is archived or deleted — a stale plan read as
current is worse than no plan.

Sits outside `Assets/`, so Unity never imports it.

## Conventions

- **Phases, not tickets.** Each phase states what blocks it and what it blocks. Dependency order is
  the point of writing it down.
- **Mark status in the file itself** as phases land — `DONE 2026-08-07` on the heading, with what
  actually shipped if it differed from the plan. The difference is usually the interesting part.
- **Record decisions taken with Rob at the top**, with the date. A plan whose premises are
  invisible cannot be re-judged when they change.
- **Nothing here overrides `GAME_DESIGN_LOCKS.md` or `CAMERA_ARCHITECTURE.md`.** A plan that needs
  a lock opened is a plan that needs an ask first.

## Current

| Plan | Covers | State |
|---|---|---|
| `FAIL_JUICE.md` | Fail teaching + nudge; kill-confirm / near-miss | Punch and scorch signed 2026-08-20; L1 Smoke nudge uncalled |
| `RANGE.md` | Campaign distance; L5 no tank + MG/sniper roles | L1 distance signed; L5 on phone 2026-08-21 ("fine for now"); ask next |
| `BACKLOG.md` | Asked for, not yet scheduled — one section per idea, with why | Not sequenced; pick one and give it its own plan file |

## Archived — `archive/`

Shipped work. Their conclusions live in the design docs and `HANDOVER.md`; these are kept for the
phase-by-phase record of what actually shipped and where it differed from the plan. **Nothing here
is a statement about the current state of the code** — that is what made archiving them necessary.

| Plan | Covers | Shipped |
|---|---|---|
| `archive/TIER0_PLAN.md` | `PRODUCT_DIRECTION.md` Tier 0 — the product spine (0.1–0.6) | Phases A–F, 2026-08-06; Tier 0 closed 2026-08-07 |
| `archive/TIER1_3_CONSUMABLES.md` | Tier 1.3 — the four base/tactical consumables | 2026-08-10, device-confirmed. Overwatch Flare deliberately held |
| `archive/AIRSTRIKE_PLANE.md` | The airstrike's aircraft and its own beat | 2026-08-10, reworked 2026-08-11, signed off "ok this will work" |
| `archive/MIDGROUND_VARIETY.md` | Per-biome mid-ground set; L1 car stays | 2026-08-19, Rob: "ok, looks nice." |

**Archived on 2026-08-11**, and the trigger is worth recording: `TIER0_PLAN.md` still said the
balance audit's device half was "still owed" four days after it had been run and signed off. That
is exactly the failure the paragraph at the top of this file warns about — **a stale plan read as
current is worse than no plan** — and it went unnoticed because a finished plan sitting beside a
live one looks live.
