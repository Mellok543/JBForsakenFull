# Economy Expansion Design

Date: 2026-09-19
Status: Approved by user, ready for implementation plan

## Goal

Add more ways to earn credits (round-team-win, rebel kill, warden kill/defense,
Special Day win, playtime, daily bonus) while rebalancing the existing passive
income so a cheap shop item (~20 credits) still takes roughly 6-7 rounds to
afford from passive play alone. Active/skilled play should earn faster, but
the passive floor should not speed up.

## Current state (before this change)

Credit-earning already lives in `JBF.Shop`, not a separate module:

- `ShopRewardsConfig` (`JBF.Shop/Models/ShopRewardsConfig.cs`): `RoundParticipation` (5),
  `RoundSurvival` (3), `InmateKillGuard` (4), `LrParticipation` (3), `LrWin` (15).
- `ShopService` (`JBF.Shop/Services/ShopService.cs`) exposes `RewardRoundPlayers()`,
  `RewardKill(victim, attacker)`, `RewardLrMatch(LrMatchEndedEvent)`, all reading from
  `ShopRewardsConfig` and calling a shared private `RewardPlayer(...)` helper.
- `JBFShop.cs` wires these to CS2 game events: `OnRoundStart` → `ResetRound()`,
  `OnRoundEnd` → `RewardRoundPlayers()` + `ResetRound()`, `OnPlayerDeath` → `RewardKill(...)`.
  LR is wired via a deferred-capability subscription pattern (`EnsureLrSubscription`,
  polled every 2s via `AddTimer` until `ILrApi` becomes available, since `JBF.LR` may
  load after `JBF.Shop`).
- Credits are also granted by `JBF.BattlePass` reward claims (`RewardType.Credits`),
  and by admin commands in `JBF.Admin` — both call `IShopApi.TryAddCredits` directly
  and are unaffected by this change.
- Shop prices (`JBF.Shop/Models/ShopItemsConfig.cs`): 20-45 credits per item. Not
  changed by this design — see "Explicitly out of scope" below.

## New sources

All new numbers live in `ShopRewardsConfig` (live-reloadable via `css_jbf_shopreload`,
same as existing fields — no rebuild needed to retune).

1. **Round team win bonus** — `RoundTeamWin` (new field, default 2). Paid to every
   usable T/CT player on `@event.Winner`'s team in `RewardRoundPlayers`, on top of
   participation/survival.
2. **Rebel kill** — `RebelKill` (new field, default 5). Paid to the killer when
   `IPlayerStateApi.RebelKilled` fires (`RebelKilledEvent.Killer`), if usable.
3. **Warden killed** — `WardenKilled` (new field, default 8). Paid to the attacker
   when `IWardenApi.WardenKilled` fires (`WardenKilledEvent.Attacker`), if usable.
4. **Warden survived the round** — `WardenSurvivedBonus` (new field, default 3).
   Paid to every usable, alive CT at round end if `IWardenApi.Warden` is non-null
   (warden did not die and did not resign) at that moment.
5. **Special Day win bonus** — `SpecialDayWinBonus` (new field, default 5). Paid
   on top of the normal round-team-win reward, only to the winning side, only when
   the round that just ended was a Special Day round.
6. **Playtime tick** — `PlaytimeTick` (new field, default 5), every 25 minutes
   (`PlaytimeIntervalMinutes`, new field, default 25). A single repeating server
   timer scans currently-connected usable players on T or CT (not spectators, not
   bots) and pays each one. This is a coarse global tick, not per-player elapsed
   time — someone who joined seconds before a tick still gets the full amount.
   Acceptable simplification per user's "не слишком точно" framing; flag if they
   want per-player precision later.
7. **Daily bonus** — `DailyBonus` (new field, default 20). Paid once per calendar
   day (UTC) on a player's first usable connection/spawn that day. Requires
   persisting `LastDailyBonusUtc` per player (new field on `ShopPlayerState`,
   persisted via `ShopStorage`) so it survives reconnects and server restarts.

## Rebalanced existing fields

- `RoundParticipation`: 5 → 2
- `RoundSurvival`: 3 → 1

Everything else existing (`InmateKillGuard` 4, `LrParticipation` 3, `LrWin` 15)
is unchanged.

Expected passive pace with these numbers: participation(2) + survival(1, if alive)
+ team-win(2, if winning side) averages roughly 3-4 credits/round depending on
win rate and survival rate, landing a 20-credit item at roughly 5-7 rounds —
matches the target. This is a starting point; because every number is in
`ShopRewardsConfig` and reloadable, it can be retuned from live play without a
rebuild.

## Architecture

No new plugin/module. Everything extends `JBF.Shop`, following the existing
pattern (`ShopRewardsConfig` field → `ShopService` reward method → `JBFShop.cs`
event wiring), the same way `InmateKillGuard`/`LrParticipation`/`LrWin` already work.

- `JBF.Shop/Models/ShopRewardsConfig.cs`: add the 7 new fields listed above
  (`RoundTeamWin`, `RebelKill`, `WardenKilled`, `WardenSurvivedBonus`,
  `SpecialDayWinBonus`, `PlaytimeTick`, `PlaytimeIntervalMinutes`, `DailyBonus`).
- `JBF.Shop/Models/ShopPlayerState.cs`: add `LastDailyBonusUtc` (nullable DateTime
  or similar), persisted through `ShopStorage`.
- `JBF.Shop/Services/ShopService.cs`:
  - `RewardRoundPlayers(CsTeam winner, bool wasSpecialDay)` — extend existing
    method's signature to take the winner and special-day flag; add team-win
    and Special-Day-win bonus logic alongside existing participation/survival.
  - `RewardRebelKill(CCSPlayerController? killer)` — new.
  - `RewardWardenKilled(CCSPlayerController? attacker)` — new.
  - `RewardWardenSurvived(CCSPlayerController? warden)` — new, iterates live CTs.
  - `TryGrantDailyBonus(CCSPlayerController player)` — new, checked on spawn/connect.
  - A repeating timer (owned by `JBFShop.cs`, started in `Load`) driving the
    playtime tick, calling a new `ShopService.RewardActivePlayers()`.
- `JBF.Shop/JBFShop.cs`:
  - `OnRoundEnd` passes `@event.Winner` and a special-day flag into
    `RewardRoundPlayers`.
  - New deferred-subscription pairs for `IWardenApi.WardenKilled` and
    `IPlayerStateApi.RebelKilled`, following the exact same pattern already used
    for `EnsureLrSubscription`/`UnsubscribeFromLr` (poll via timer until the
    capability appears, since load order across plugins isn't guaranteed).
  - `OnPlayerSpawn` (new handler) calls `TryGrantDailyBonus`.
  - New repeating timer for the playtime tick.

## Known risk to verify live

`ISpecialDaysApi` has no "day ended, here's the winner" event — only
`IsActive`/`ActiveDayName`/`PendingDayName`. The plan is to snapshot
`SpecialDaysCapability.Api.Get()?.IsActive` in `OnRoundStart` (before the pending
day, if any, actually starts) and use that snapshot at `OnRoundEnd` to decide
whether to apply `SpecialDayWinBonus`. This avoids a race on `IsActive` flipping
false by the time `Finish()` has already run inside the Special Day module.
If, during live testing, this snapshot approach also doesn't align with the
special day module's lifecycle, the fix is small — narrow to checking
`ActiveDayName` at the exact round-start point instead of `IsActive`.

## Explicitly out of scope (for this change)

- Shop item prices (`ShopItemsConfig`) are not changed. If the live-tested pace
  still feels off after retuning `ShopRewardsConfig`, that's a separate follow-up
  so the effect of reward changes vs. price changes doesn't get conflated.
- Per-player precise playtime tracking (vs. the coarse global tick) — not needed
  per user's stated tolerance ("не слишком точно").
- No new capability/event is added to `JBF.Api` for Special Days — the snapshot
  approach avoids touching `ISpecialDay`/`ISpecialDaysApi`/`ISpecialDayContext`,
  which keeps this change contained to `JBF.Shop`.
