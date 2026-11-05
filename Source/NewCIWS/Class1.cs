using CombatExtended;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static NewCIWS.CompProperties_CIWSAnchor;


namespace NewCIWS
{

    
    public class MortarTargetData
    {
        public int thingID;
        public IntVec3 lastCell;
        public Vector3 lastExactPos;
        public Vector3 velocity;
        public int lastSeenTick;
        public bool isHostile;
        public int claimedByAnchorId = -1;
        public int claimedTick = -1;
        public int resolveTick = -1;
        public bool interceptResolved = false;
    }
    public class CompProperties_CIWSAnchor : CompProperties
    {

        public float radius = 45f;          // cells
        public float interceptChance = 0.65f; // 0..1
        public bool debugAnnounce = true;
        public enum CIWSTargetAllowance
        {
            All = 0,
            HostileOnly = 1
        }
        public CompProperties_CIWSAnchor()
        {
            compClass = typeof(CompCIWSAnchor);
        }
        public enum CIWSEngagementAllowance
        {
            ProjectileOnly = 0,
            GroundOnly = 1,
            Both = 2
        }
    }

    public class CompCIWSAnchor : ThingComp
    {
        public CompProperties_CIWSAnchor Props => (CompProperties_CIWSAnchor)props;
        public int busyUntilTick = 0;
        public int currentTargetShellId = -1; // optional: for debugging
        public CIWSEngagementAllowance engagementAllowance = CIWSEngagementAllowance.ProjectileOnly;
        public CIWSTargetAllowance targetAllowance = CIWSTargetAllowance.HostileOnly;
        public int visualBurstShotsRemaining = 0;
        public int visualBurstSpacing = 3;
        public int nextVisualBurstTick = -1;
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref busyUntilTick, "ciwsBusyUntilTick", 0);
            Scribe_Values.Look(ref currentTargetShellId, "ciwsCurrentTargetShellId", -1);
            Scribe_Values.Look(ref targetAllowance, "ciwsTargetMode", CIWSTargetAllowance.HostileOnly);
            Scribe_Values.Look(ref engagementAllowance, "ciwsEngagementAllowance", CIWSEngagementAllowance.ProjectileOnly);
        }
        public bool HasAmmoForIntercept()
        {
            Verb v = TryGetAttackVerb();

            if (v == null)
            {
                //Log.Message($"[NewCIWS] AMMO CHECK {parent.LabelCap}: Verb=NULL");
                return true;
            }
            /*
            Log.Message(
                $"[NewCIWS] AMMO CHECK {parent.LabelCap}: " +
                $"Verb={v.GetType().Name}, " +
                $"EquipmentSource={(v.EquipmentSource != null ? v.EquipmentSource.LabelCap : "NULL")}"
            );
            */
            ThingWithComps equipment = v.EquipmentSource as ThingWithComps;

            if (equipment == null)
            {
                Log.Message($"[NewCIWS] AMMO CHECK {parent.LabelCap}: EquipmentSource is NULL/not ThingWithComps");
                return true;
            }

            CompAmmoUser ammoUser = equipment.TryGetComp<CompAmmoUser>();

            if (ammoUser == null)
            {
                /*Log.Message($"[NewCIWS] AMMO CHECK {parent.LabelCap}: gun CompAmmoUser=NULL");*/
                return true;
            }

            int shots = Mathf.Max(1, v.verbProps.burstShotCount);
            bool result = ammoUser.CurMagCount >= shots;

            /*Log.Message(
                $"[NewCIWS] AMMO CHECK {parent.LabelCap}: " +
                $"gun={equipment.LabelCap}, " +
                $"CurMagCount={ammoUser.CurMagCount}, " +
                $"RequiredShots={shots}, " +
                $"Result={result}"
            );*/

            return result;
        }
        public bool TryConsumeAmmoForIntercept()
        {
            Verb v = TryGetAttackVerb();

            if (v == null || v.verbProps == null)
                return false;

            ThingWithComps equipment = v.EquipmentSource as ThingWithComps;

            if (equipment == null)
                return false;

            CompAmmoUser ammoUser = equipment.TryGetComp<CompAmmoUser>();

            if (ammoUser == null)
                return false;

            int shots = Mathf.Max(1, v.verbProps.burstShotCount);

            if (ammoUser.CurMagCount < shots)
                return false;

            ammoUser.CurMagCount -= shots;

            if (Props.debugAnnounce)
            {
                /*Log.Message(
                    $"[NewCIWS] {parent.LabelCap} consumed {shots} rounds for CIWS intercept. " +
                    $"Remaining={ammoUser.CurMagCount}"
                );*/
            }

            return true;
        }
        public override void CompTick()
        {
            base.CompTick();

            if (visualBurstShotsRemaining <= 0)
                return;

            int now = Find.TickManager.TicksGame;

            if (now < nextVisualBurstTick)
                return;

            if (parent.Spawned && parent.Map != null)
            {
                FleckMaker.Static(parent.DrawPos, parent.Map, FleckDefOf.ShotFlash, 1f);
                FleckMaker.ThrowMicroSparks(parent.DrawPos, parent.Map);
            }

            visualBurstShotsRemaining--;
            nextVisualBurstTick = now + visualBurstSpacing;
        }
        private string GetEngagementLabel()
        {
            switch (engagementAllowance)
            {
                case CIWSEngagementAllowance.ProjectileOnly:
                    return "P";
                case CIWSEngagementAllowance.GroundOnly:
                    return "G";
                case CIWSEngagementAllowance.Both:
                    return "B";
                default:
                    return "?";
            }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
                yield return g;

            yield return new Command_Action
            {
                defaultLabel = $"CIWS Targeting: {(targetAllowance == CIWSTargetAllowance.All ? "A" : "H")}",
                defaultDesc = "Toggle CIWS shell targeting. A = all tracked overhead shells, H = hostile shells only.",
                icon = TexCommand.Attack,
                action = () =>
                {
                    targetAllowance = (targetAllowance == CIWSTargetAllowance.All)
                        ? CIWSTargetAllowance.HostileOnly
                        : CIWSTargetAllowance.All;
                }
            };
            yield return new Command_Action
            {
                defaultLabel = $"CIWS Domain: {GetEngagementLabel()}",
                defaultDesc = "Cycle CIWS engagement mode: projectile-only, ground-only, or both.",
                icon = TexCommand.FireAtWill,
                action = () =>
                {
                    switch (engagementAllowance)
                    {
                        case CIWSEngagementAllowance.ProjectileOnly:
                            engagementAllowance = CIWSEngagementAllowance.GroundOnly;
                            break;
                        case CIWSEngagementAllowance.GroundOnly:
                            engagementAllowance = CIWSEngagementAllowance.Both;
                            break;
                        default:
                            engagementAllowance = CIWSEngagementAllowance.ProjectileOnly;
                            break;
                    }
                }
            };
        }

        public Verb TryGetAttackVerb()
        {
            Building_Turret turret = parent as Building_Turret;
            if (turret != null)
                return turret.AttackVerb;

            return null;
        }


        public void GetVerbTiming(out int warmupTicks, out int firingTicks, out int cooldownTicks)
        {
            warmupTicks = 0;
            firingTicks = 0;
            cooldownTicks = 0;

            Verb v = TryGetAttackVerb();
            if (v == null || v.verbProps == null)
                return;

            warmupTicks = Mathf.RoundToInt(v.verbProps.warmupTime * 60f);

            int shots = Mathf.Max(1, v.verbProps.burstShotCount);
            int between = Mathf.Max(0, v.verbProps.ticksBetweenBurstShots);
            firingTicks = (shots - 1) * between;

            // If your RW version lacks defaultCooldownTime, set cooldownTicks = 0 for now.
            cooldownTicks = Mathf.RoundToInt(v.verbProps.defaultCooldownTime * 60f);
        }

        public void PlayCIWSFireFeedback(Map map)
        {
            Verb v = TryGetAttackVerb();

            int shots = 1;
            int spacing = 3;

            if (v != null && v.verbProps != null)
            {
                shots = Mathf.Max(1, v.verbProps.burstShotCount);
                spacing = Mathf.Max(1, v.verbProps.ticksBetweenBurstShots);

                if (Props.debugAnnounce)
                {
                    Log.Message(
                        $"[NewCIWS] Verb type={v.GetType().Name} " +
                        $"burst={v.verbProps.burstShotCount} " +
                        $"between={v.verbProps.ticksBetweenBurstShots}"
                    );
                }
            }

            visualBurstShotsRemaining = shots;
            visualBurstSpacing = spacing;
            nextVisualBurstTick = Find.TickManager.TicksGame;
        }
    }

    public class MapComponent_MortarTracker : MapComponent
    {
        private Dictionary<int, MortarTargetData> trackedShells = new Dictionary<int, MortarTargetData>();
        private Dictionary<int, Thing> projectileByIdThisTick = new Dictionary<int, Thing>();

        public MapComponent_MortarTracker(Map map) : base(map)
        {
            Log.Message($"[NewCIWS] MortarTracker created for map {map.uniqueID}");
        }

        private bool IsCIWSOnline(CompCIWSAnchor a)
        {
            if (a == null || a.parent == null || a.parent.Destroyed) return false;
            if (!a.parent.Spawned) return false;

            // Optional: only player-owned CIWS engages
            // if (a.parent.Faction != Faction.OfPlayer) return false;

            CompPowerTrader power = a.parent.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
                return false;

            return true;
        }
        public override void MapComponentTick()
        {
            base.MapComponentTick();

            // Tick heartbeat (every 300 ticks ~= 5 sec at 60 TPS)
            if (Find.TickManager.TicksGame % 150 == 0)
            {
                Log.Message($"[NewCIWS] MortarTracker tick: tracking {trackedShells.Count} shell(s) on map {map.uniqueID}");
            }

            TrackMortarShells();
            CleanupGarbageShells();
            CheckInterceptabilityAndClaim();
            ResolvePendingIntercepts();
        }

        private void TrackMortarShells()
        {
            projectileByIdThisTick.Clear();

            List<Thing> projectiles = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
            int currentTick = Find.TickManager.TicksGame;

            for (int i = 0; i < projectiles.Count; i++)
            {
                Thing t = projectiles[i];
                if (t?.def == null)
                    continue;

                // Track only NLOS projectiles
                if (t.def.projectile == null || !t.def.projectile.flyOverhead)
                    continue;

                // Cache the exact Thing for this tick
                projectileByIdThisTick[t.thingIDNumber] = t;

                RegisterOrUpdateShell(t, currentTick);
            }
        }

        private void RegisterOrUpdateShell(Thing shell, int currentTick)
        {
            int id = shell.thingIDNumber;
            Vector3 pos = shell.DrawPos;

            if (!trackedShells.TryGetValue(id, out MortarTargetData data))
            {
                data = new MortarTargetData
                {
                    thingID = id,
                    lastCell = shell.Position,
                    lastExactPos = pos,
                    velocity = Vector3.zero,
                    lastSeenTick = currentTick,
                    isHostile = IsHostileShell(shell)
                };

                trackedShells.Add(id, data);

                // One-time detection log
                Log.Message($"[NewCIWS] Detected {shell.def.defName} id={id} at {shell.Position} hostile={data.isHostile}");
                return;
            }

            int dt = currentTick - data.lastSeenTick;
            if (dt > 0)
                data.velocity = (pos - data.lastExactPos) / dt;

            data.lastExactPos = pos;
            data.lastCell = shell.Position;
            data.lastSeenTick = currentTick;
        }

        private bool IsHostileShell(Thing shell)
        {
            // First try CE projectile
            ProjectileCE ceProj = shell as ProjectileCE;

            Thing launcher = null;
            Thing equipment = null;

            if (ceProj != null)
            {
                launcher = ceProj.launcher;      
                equipment = ceProj.equipment;    
            }
            else
            {
                // Attempt fallback to vanilla projectil
                Projectile proj = shell as Projectile;
                if (proj != null)
                {
                    launcher = proj.Launcher;
                    equipment = null; // vanilla has no Equipment
                }
            }

            Faction sourceFaction = null;

            // Buildings are prefered to better accomodate automated systems
            if (launcher is Building && launcher.Faction != null)
                sourceFaction = launcher.Faction;
            else if (launcher is Pawn && launcher.Faction != null)
                sourceFaction = launcher.Faction;
            else if (equipment != null && equipment.Faction != null)
                sourceFaction = equipment.Faction;

            if (sourceFaction == null)
                return false; 

            return sourceFaction.HostileTo(Faction.OfPlayer);
        }

        private void CleanupGarbageShells()
        {
            int currentTick = Find.TickManager.TicksGame;
            if (trackedShells.Count == 0) return;

            // Remove projectiles not seen for a couple ticks (they were likely destroyed or left map)
            var toRemove = new List<int>();

            foreach (var kvp in trackedShells)
            {
                if (currentTick - kvp.Value.lastSeenTick > 2)
                    toRemove.Add(kvp.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
                trackedShells.Remove(toRemove[i]);
        }

        private List<CompCIWSAnchor> GetCIWSAnchors()
        {
            var anchors = new List<CompCIWSAnchor>();

            // This includes turrets, mortars, any building with comps
            List<Thing> things = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);

            for (int i = 0; i < things.Count; i++)
            {
                ThingWithComps twc = things[i] as ThingWithComps;
                if (twc == null) continue;

                CompCIWSAnchor comp = twc.GetComp<CompCIWSAnchor>();
                if (comp != null && IsCIWSOnline(comp))
                    anchors.Add(comp);
            }

            return anchors;
        }
        private void CheckInterceptabilityAndClaim()
        {
            List<CompCIWSAnchor> anchors = GetCIWSAnchors();
            if (anchors.Count == 0) return;

            int currentTick = Find.TickManager.TicksGame;

            foreach (var kvp in trackedShells)
            {
                MortarTargetData data = kvp.Value;

                // Already claimed: (later you’ll have the owner turret do effects / roll intercept)
                if (data.claimedByAnchorId != -1)
                    continue;

                Vector3 shellPos = data.lastExactPos;

                // Find all eligible turrets in range this tick
                List<CompCIWSAnchor> candidates = null;

                for (int i = 0; i < anchors.Count; i++)
                {
                    CompCIWSAnchor a = anchors[i];

                    if (a.busyUntilTick > currentTick)
                        continue;
                    if (!a.HasAmmoForIntercept())
                        continue;
                    if (a.engagementAllowance == CIWSEngagementAllowance.GroundOnly)
                        continue;
                    if (a.targetAllowance == CIWSTargetAllowance.HostileOnly && !data.isHostile)
                        continue;

                    float r = a.Props.radius;
                    float sqrR = r * r;

                    Vector3 center = a.parent.Position.ToVector3Shifted();
                    if ((shellPos - center).sqrMagnitude <= sqrR)
                    {
                        if (candidates == null) candidates = new List<CompCIWSAnchor>();
                        candidates.Add(a);
                    }
                }

                if (candidates == null || candidates.Count == 0)
                    continue;

                // If multiple turrets can claim simultaneously, pick randomly
                CompCIWSAnchor chosen = (candidates.Count == 1) ? candidates[0] : candidates[Rand.Range(0, candidates.Count)];

                int warmup, firing, cooldown;
                chosen.GetVerbTiming(out warmup, out firing, out cooldown);

                chosen.busyUntilTick = currentTick + warmup + firing + cooldown;
                chosen.currentTargetShellId = data.thingID;
                data.claimedByAnchorId = chosen.parent.thingIDNumber;
                data.claimedTick = currentTick;

                // This is the key line:
                data.resolveTick = currentTick + warmup;
                data.interceptResolved = false;

                if (chosen.Props.debugAnnounce)
                {
                    Log.Message($"[NewCIWS] Shell id={data.thingID} CLAIMED by {chosen.parent.LabelCap} at tick {currentTick}");
                }
            }
        }
        private void InterceptTrackedShell(MortarTargetData data)
        {
            if (data == null) return;

            Thing shell;
            if (!projectileByIdThisTick.TryGetValue(data.thingID, out shell))
                return; // not present this tick (already impacted/destroyed/etc.)

            if (shell == null || shell.Destroyed)
                return;

            shell.Destroy(DestroyMode.Vanish);
        }

        public void ClearClaim(MortarTargetData data)
        {
            if (data == null) return;

            data.claimedByAnchorId = -1;
            data.claimedTick = -1;
            data.resolveTick = -1;
            data.interceptResolved = false;
        }
        private void ResolvePendingIntercepts()
        {
            int now = Find.TickManager.TicksGame;

            foreach (var kvp in trackedShells)
            {
                MortarTargetData data = kvp.Value;

                if (data.claimedByAnchorId == -1) continue;
                if (data.interceptResolved) continue;
                if (data.resolveTick < 0 || data.resolveTick > now) continue;

                data.interceptResolved = true;

                // Find the turret that claimed this shell
                CompCIWSAnchor owner = FindAnchorById(data.claimedByAnchorId);

                // Turret disappeared/went offline before resolving
                if (owner == null || !IsCIWSOnline(owner))
                {
                    ClearClaim(data);
                    continue;
                }

                // Safety check:
                // Ammo existed when the turret claimed the shell, but it may have
                // been consumed by normal firing before the intercept resolved.
                if (!owner.TryConsumeAmmoForIntercept())
                {
                    if (owner.Props.debugAnnounce)
                    {
                        Log.Message(
                            $"[NewCIWS] {owner.parent.LabelCap} lacks ammo at resolve for " +
                            $"shell id={data.thingID}; releasing claim."
                        );
                    }

                    owner.currentTargetShellId = -1;
                    ClearClaim(data);
                    continue;
                }

                // Ammo has now been consumed, so the CIWS actually "fires"
                owner.PlayCIWSFireFeedback(map);

                bool success = Rand.Chance(owner.Props.interceptChance);

                if (owner.Props.debugAnnounce)
                {
                    Log.Message(
                        $"[NewCIWS] Resolve shell id={data.thingID} by {owner.parent.LabelCap} " +
                        $"success={success} at tick {now}"
                    );
                }

                if (success)
                {
                    InterceptTrackedShell(data);
                }
                else
                {
                    if (owner.Props.debugAnnounce)
                    {
                        Log.Message(
                            $"[NewCIWS] Shell id={data.thingID} missed by " +
                            $"{owner.parent.LabelCap}, releasing claim."
                        );
                    }

                    owner.currentTargetShellId = -1;
                    ClearClaim(data);
                }
            }
        }
        private CompCIWSAnchor FindAnchorById(int anchorId)
        {
            List<CompCIWSAnchor> anchors = GetCIWSAnchors();
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].parent.thingIDNumber == anchorId)
                    return anchors[i];
            }
            return null;
        }
    }
}