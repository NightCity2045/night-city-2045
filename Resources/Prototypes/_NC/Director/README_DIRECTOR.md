# Global Director

`Resources/Prototypes/_NC/Director` defines Night City ambient incidents for the `_NC` Global Director system.

The runtime is owned by:
- `Content.Server/_NC/Director/GlobalDirectorSystem.cs`
- `Content.Shared/_NC/Director/*.cs`

The content contract is:
- `gamerule.yml` defines the round-level scheduler rule entity.
- `markers.yml` defines map markers used as spawn anchors.
- `templates.yml` defines abstract reusable event bases.
- `*.yml` event files define concrete incidents.

## Mental model

Each `directorEvent` is a small state machine.

1. The `GlobalDirector` gamerule waits for its scheduler timer.
2. It filters all `directorEvent` prototypes by eligibility.
3. It starts one eligible event by weight.
4. The event enters its `startPhase`.
5. Each phase may:
   - announce text,
   - spawn entities,
   - change faction relationships,
   - apply an HTN domain,
   - move to the next phase by timer or trigger.
6. The event ends when there is no valid next phase.

## Scheduler fields

Configured on the `GlobalDirector` component in `gamerule.yml`:

- `minDelay`, `maxDelay`: seconds between scheduler attempts.
- `minPlayers`: global hard gate for the scheduler.
- `maxConcurrentEvents`: maximum active director events at once.
- `roundStartDelay`: seconds after round start before the scheduler becomes active.
- `defaultAnnouncerId`, `announcementColor`: fallback announcement identity.

## Event fields

Configured on `directorEvent` prototypes:

- `enabled`: if false, the event can only be started manually.
- `weight`: random selection weight.
- `minPlayers`, `maxPlayers`: per-event player gate.
- `minRoundDuration`: earliest round time when the event may start.
- `cooldown`: delay before the same prototype may start again.
- `maxOccurrences`: max starts per round. `0` means unlimited.
- `maxSimultaneous`: max concurrent instances of the same prototype.
- `cleanupOnEnd`: delete remaining event-owned entities when the event finishes.
- `startPhase`: first phase id.
- `phases`: state machine body.

`directorEvent` supports inheritance through `parent`, so common defaults should live in abstract templates.

## Phase fields

Configured inside `phases.<PhaseId>`:

- `duration`: seconds until automatic advance. Omit for trigger-only phases.
- `announcement`: locale key broadcast on phase entry.
- `meetLocationTags`: acceptable tags for the shared scene point.
- `entryLocationTags`: acceptable phase-level entry tags when a group does not define its own side.
- `aiDomain`: HTN compound id applied to event-owned entities.
- `cleanup`: if true, all currently owned spawned entities are deleted before the next phase.
- `spawns`: entity groups spawned on phase entry.
- `triggers`: trigger rules that can advance the phase.
- `nextPhases`: weighted next phase table.
- `factionOverrides`: remap specific `groupTag` values to new factions.

## Spawn groups

Each item in `spawns` supports:

- `prototype`: entity prototype id.
- `groupTag`: logical label used by `factionOverrides` or trigger filters.
- `entryLocationTags`: preferred per-group entry tags.
- `exitLocationTags`: preferred per-group retreat tags.
- `faction`: optional faction applied immediately after spawn.
- `amount`: number of entities to spawn.

Spawn resolution order is:

1. `entryLocationTags` on the spawn group.
2. `entryLocationTags` on the phase.
3. `meetLocationTags` on the phase as the final fallback anchor.

This lets multiple groups approach the same incident from different sides while still sharing a single scene center.

Retreat resolution order is:

1. `exitLocationTags` on the spawn group.
2. The resolved entry point for that same group.
3. The phase anchor as the last fallback.

This means a group can spawn from `GangAEntry`, walk to the shared meeting point, then withdraw to `GangA` before cleanup deletes it.

## Triggers

Supported trigger types:

- `MobKilled`
- `EntityDestroyed`

Trigger fields:

- `target`: legacy prototype filter.
- `targetPrototype`: preferred prototype filter.
- `targetGroup`: optional `groupTag` filter.
- `count`: occurrences required before the phase advances.

Trigger counting is phase-scoped. Kills or deletions from older phases do not leak into the current phase.

## Mapping contract

Place `DirectorSpawnPoint` on the map.

Supported marker fields:

- `locationTag`: legacy single tag.
- `locationTags`: preferred list of tags.

Recommended event marker sets:

- `BackalleyDeal`: `DirectorSpawnPointBackalleyDealMeet`, `DirectorSpawnPointBackalleyDealGangAEntry`, `DirectorSpawnPointBackalleyDealGangBEntry`, `DirectorSpawnPointBackalleyDealGangAExit`, `DirectorSpawnPointBackalleyDealGangBExit`.
- `CheckpointShakedown`: `DirectorSpawnPointCheckpointShakedownMeet`, `DirectorSpawnPointCheckpointShakedownInspectorsEntry`, `DirectorSpawnPointCheckpointShakedownInspectorsExit`.
- `CorporateSweep`: `DirectorSpawnPointCorporateSweepMeet`, `DirectorSpawnPointCorporateSweepEntry`, `DirectorSpawnPointCorporateSweepExit`.
- `HijackedShipment`: `DirectorSpawnPointHijackedShipmentMeet`, `DirectorSpawnPointHijackedShipmentCouriersEntry`, `DirectorSpawnPointHijackedShipmentCouriersExit`, `DirectorSpawnPointHijackedShipmentHijackersEntry`, `DirectorSpawnPointHijackedShipmentHijackersExit`.
- `ScavengerRush`: `DirectorSpawnPointScavengerRushMeet`, `DirectorSpawnPointScavengerRushScavsAEntry`, `DirectorSpawnPointScavengerRushScavsAExit`, `DirectorSpawnPointScavengerRushScavsBEntry`, `DirectorSpawnPointScavengerRushScavsBExit`.
- `StreetTaxCollection`: `DirectorSpawnPointStreetTaxCollectionMeet`, `DirectorSpawnPointStreetTaxCollectionCollectorsEntry`, `DirectorSpawnPointStreetTaxCollectionCollectorsExit`.
- `TestLivingWorldEvent`: `DirectorSpawnPointTestLivingWorldEventMeet`, `DirectorSpawnPointTestLivingWorldEventBanditsEntry`, `DirectorSpawnPointTestLivingWorldEventBanditsExit`.

Recommended standard tags:

- `Alley`
- `Hidden`
- `Street`
- `Market`
- `Checkpoint`
- `Rooftop`
- `Warehouse`

Use a controlled tag vocabulary. Director content breaks down quickly if mappers invent near-duplicate tags.

## Authoring template

```yaml
- type: directorEvent
  id: MyStreetIncident
  parent: NCBaseDirectorEvent
  name: "Street Incident"
  weight: 12
  announcerId: "CitiNet"
  startPhase: "Gathering"
  phases:
    Gathering:
      duration: 120
      announcement: "my-incident-gathering"
      meetLocationTags: ["Alley", "Street"]
      aiDomain: "DirectorGatherCompound"
      spawns:
        - prototype: MobNCBanditPistolUnlootable
          groupTag: "Dealers"
          faction: "Passive"
          exitLocationTags: ["DealersHome"]
          entryLocationTags: ["DealersEntry"]
          amount: 2
      nextPhases:
        Fight: 60
        Disperse: 40

    Fight:
      duration: 180
      announcement: "my-incident-fight"
      aiDomain: "NCTacticalCombatCompound"
      factionOverrides:
        Dealers: "NCBandit"
      triggers:
        - type: MobKilled
          targetGroup: "Dealers"
          count: 2
      nextPhases:
        Cleanup: 100

    Cleanup:
      duration: 45
      announcement: "my-incident-cleanup"
      aiDomain: "FleeToExtraction"
      cleanup: true
```

## Admin commands

- `startdirectorevent <prototypeId>`
- `advancedirectorevent <entityUid>`
- `canceldirectorevent <entityUid>`
- `directorstatus`

Use `directorstatus` first when an event does not start. It reports scheduler state, active events, and ineligibility reasons for every prototype.

## Content rules

- Use `_NC` NPC prototypes, not generic station content, unless there is a deliberate design reason.
- Prefer unlootable mobs for ambient incidents unless the event is intended as a reward source.
- Keep phase logic in YAML and engine logic in the `_NC` Director systems.
- Reuse abstract templates for shared defaults instead of copy-pasting scheduler gates into every event.
- Use phase `meetLocationTags` for the shared scene anchor, `entryLocationTags` for side-specific entry points, and group `exitLocationTags` for retreat destinations.
