using System.Collections.Generic;
using MechJebLib.Primitives;
using MuMech;
using Xunit;

namespace MechJebLibTest.StagingTests
{
    public class SolidBoosterSeparationFlightTests
    {
        // RealFuels exposes this method through SolverEngines, without a hard
        // assembly reference from MechJeb. It can report exhausted usable fuel
        // while the KSP resource amount is still positive.
        private class ResidualEngine : ModuleEngines
        {
            public bool UsablePropellant = true;
            public bool PropellantAvailable()
            {
                foreach (Propellant propellant in propellants)
                {
                    if (propellant.ignoreForIsp && propellant.totalResourceAvailable <= 0) return false;
                }
                return UsablePropellant;
            }
        }

        private class BrokenPropellantCheckEngine : ModuleEngines
        {
            public bool PropellantAvailable() => throw new System.InvalidOperationException();
        }

        private sealed class Rocket
        {
            public readonly SolidBoosterSeparationFlightAdapter Adapter = new SolidBoosterSeparationFlightAdapter();
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<ModuleEngines> Engines = new List<ModuleEngines>();
            public readonly ModuleEngines Booster;

            public Rocket(ModuleEngines? boosterEngine = null)
            {
                Part core = new Part { mass = 50, ResourceMass = 50 };
                Part booster = new Part { mass = 5, ResourceMass = 5, DroppedInStage = 2, parent = core };
                Parts.Add(core);
                Parts.Add(booster);
                Engines.Add(new ModuleEngines { part = core, finalThrust = 2000, engineType = EngineType.LiquidFuel });
                Booster = boosterEngine ?? new ModuleEngines();
                Booster.part = booster;
                Booster.finalThrust = 300;
                Engines.Add(Booster);
            }

            public bool Update(double time, int stage = 2) => Adapter.Update(
                Parts
                , Engines
                , stage
                , time
                , new V3(
                    0
                    , 1
                    , 0));

            public ModuleEngines AddBooster()
            {
                Part part = new Part { mass = 10, DroppedInStage = 2, parent = Parts[0] };
                ModuleEngines engine = new ModuleEngines { part = part, finalThrust = 300 };
                Parts.Add(part);
                Engines.Add(engine);
                return engine;
            }
        }

        private static void BurnOut(ModuleEngines engine)
        {
            engine.finalThrust = 0;
            engine.getFlameoutState = true;
            engine.propellants[0].totalResourceAvailable = 0;
        }

        [Fact]
        public void FirstBurnoutReleasesOtherUsefulBoostersWithoutTailoffDelay()
        {
            Rocket rocket = new Rocket();
            ModuleEngines second = rocket.AddBooster();
            Assert.False(rocket.Update(0));
            BurnOut(rocket.Booster);
            Assert.True(rocket.Update(1));
            Assert.True(rocket.Adapter.CanDrop(rocket.Booster.part));
            Assert.True(rocket.Adapter.CanDrop(second.part));
            Assert.False(rocket.Adapter.CanDrop(rocket.Parts[0]));
        }

        [Fact]
        public void RealFuelsResidualFuelDoesNotPreventBurnoutRelease()
        {
            ResidualEngine engine = new ResidualEngine();
            Rocket rocket = new Rocket(engine);
            rocket.AddBooster();
            rocket.Update(0);
            engine.finalThrust = 0;
            engine.getFlameoutState = true;
            engine.propellants[0].totalResourceAvailable = 2;
            engine.UsablePropellant = false;
            Assert.True(rocket.Update(1));
        }

        [Fact]
        public void StockFueloutCanLeaveAPositiveUnusableRemainder()
        {
            Rocket rocket = new Rocket();
            rocket.AddBooster();
            rocket.Update(0);
            rocket.Booster.finalThrust = 0;
            rocket.Booster.getFlameoutState = true;
            rocket.Booster.propellants[0].totalResourceAvailable = 0.001;
            rocket.Booster.propellants[0].isDeprived = true;
            Assert.True(rocket.Update(1));
        }

        [Fact]
        public void RealFuelsAuxiliaryStarvationIsNotSolidFuelBurnout()
        {
            Rocket rocket = new Rocket(new ResidualEngine());
            rocket.AddBooster();
            rocket.Update(0);
            rocket.Booster.finalThrust = 0;
            rocket.Booster.getFlameoutState = true;
            rocket.Booster.propellants.Add(new Propellant { ignoreForIsp = true, totalResourceAvailable = 0 });
            Assert.False(rocket.Update(1));
        }

        [Fact]
        public void AFlameoutWithUsableFuelDoesNotDiscardOtherBoosters()
        {
            ResidualEngine engine = new ResidualEngine();
            Rocket rocket = new Rocket(engine);
            rocket.AddBooster();
            rocket.Update(0);
            engine.finalThrust = 0;
            engine.getFlameoutState = true;
            Assert.False(rocket.Update(1));
            Assert.False(rocket.Update(2));
        }

        [Fact]
        public void AFailedPropellantCheckDoesNotDiscardOtherBoosters()
        {
            Rocket rocket = new Rocket(new BrokenPropellantCheckEngine());
            rocket.AddBooster();
            rocket.Update(0);
            BurnOut(rocket.Booster);
            Assert.False(rocket.Update(1));
        }

        [Fact]
        public void AnEmptyAuxiliaryResourceIsNotSolidFuelBurnout()
        {
            Rocket rocket = new Rocket();
            rocket.AddBooster();
            rocket.Update(0);
            rocket.Booster.finalThrust = 0;
            rocket.Booster.getFlameoutState = true;
            rocket.Booster.propellants.Add(new Propellant { ignoreForIsp = true, totalResourceAvailable = 0 });
            Assert.False(rocket.Update(1));
        }

        [Fact]
        public void AShutDownEngineIsNotNormalBurnout()
        {
            Rocket rocket = new Rocket();
            rocket.AddBooster();
            rocket.Update(0);
            BurnOut(rocket.Booster);
            rocket.Booster.engineShutdown = true;
            Assert.False(rocket.Update(1));
        }

        [Fact]
        public void AnUnfiredEmptyBoosterDoesNotDiscardRunningBoosters()
        {
            Rocket rocket = new Rocket();
            rocket.AddBooster();
            BurnOut(rocket.Booster);
            Assert.False(rocket.Update(0));
            Assert.False(rocket.Update(1));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BurnoutDoesNotOverrideColdOrLiquidEngineProtection(bool liquid)
        {
            Rocket rocket = new Rocket();
            ModuleEngines second = rocket.AddBooster();
            rocket.Update(0);
            BurnOut(rocket.Booster);
            if (liquid) second.engineType = EngineType.LiquidFuel;
            else second.EngineIgnited = false;
            Assert.False(rocket.Update(1));
            Assert.False(rocket.Update(2));
        }

        [Fact]
        public void BurnoutReleaseDoesNotRequireAThrustingCore()
        {
            Rocket rocket = new Rocket();
            rocket.AddBooster();
            // The solids need not have exceeded the core's acceleration to
            // establish that they have actually burned.
            rocket.Engines[0].finalThrust = 4000;
            rocket.Update(0);
            rocket.Engines[0].finalThrust = 0;
            BurnOut(rocket.Booster);
            Assert.True(rocket.Update(1));
        }

        [Fact]
        public void ResetDiscardsBurnHistory()
        {
            Rocket rocket = new Rocket();
            rocket.AddBooster();
            rocket.Update(0);
            rocket.Adapter.Reset();
            BurnOut(rocket.Booster);
            Assert.False(rocket.Update(1));
            Assert.False(rocket.Update(2));
        }

        [Fact]
        public void ClockRewindDiscardsBurnHistory()
        {
            Rocket rocket = new Rocket();
            rocket.AddBooster();
            rocket.Update(10);
            BurnOut(rocket.Booster);
            Assert.False(rocket.Update(1));
            Assert.False(rocket.Update(2));
        }

        [Fact]
        public void ReadsLiveTailoffWithoutDependingOnAnEstimatedBurnTime()
        {
            Rocket rocket = new Rocket();
            Assert.False(rocket.Update(0));
            rocket.Booster.finalThrust = 150;
            Assert.False(rocket.Update(1));
            Assert.True(rocket.Update(1.3));
            Assert.True(rocket.Adapter.CanDrop(rocket.Booster.part));
            Assert.False(rocket.Adapter.CanDrop(rocket.Parts[0]));
        }

        [Fact]
        public void CountsPropellantAndAccessoriesBehindTheSameDecouplerOnce()
        {
            Rocket rocket = new Rocket();
            rocket.Parts.Add(new Part { mass = 5, ResourceMass = 5, parent = rocket.Booster.part });
            rocket.Booster.finalThrust = 600;
            rocket.Update(0);
            rocket.Booster.finalThrust = 250;
            rocket.Update(1);
            Assert.True(rocket.Update(1.3));
        }

        [Fact]
        public void BoosterMassIsUpdatedAsItsFuelBurns()
        {
            Rocket rocket = new Rocket();
            rocket.Update(0);
            rocket.Booster.finalThrust = 150;
            rocket.Update(1);
            // With no propellant remaining, 150 kN on a 5 t case is useful again.
            rocket.Booster.part.ResourceMass = 0;
            Assert.False(rocket.Update(1.3));
        }

        [Fact]
        public void EachDetachedBranchMustHaveStoppedHelping()
        {
            Rocket rocket = new Rocket();
            Part secondPart = new Part { mass = 10, DroppedInStage = 2, parent = rocket.Parts[0] };
            ModuleEngines secondEngine = new ModuleEngines { part = secondPart, finalThrust = 300 };
            rocket.Parts.Add(secondPart);
            rocket.Engines.Add(secondEngine);
            rocket.Update(0);
            rocket.Booster.finalThrust = 0;
            rocket.Update(1);
            Assert.False(rocket.Update(2));
            secondEngine.finalThrust = 150;
            rocket.Update(3);
            Assert.True(rocket.Update(3.3));
        }

        [Fact]
        public void CantedThrustIsProjectedOntoTheFlightAxis()
        {
            Rocket rocket = new Rocket();
            rocket.Booster.finalThrust = 600;
            rocket.Booster.thrustTransforms[0].forward = new V3(
                0.8660254037844386
                , -0.5
                , 0);
            rocket.Update(0);
            rocket.Booster.finalThrust = 300;
            rocket.Update(1);
            Assert.True(rocket.Update(1.3));
        }

        [Fact]
        public void AColdOrLiquidEngineOnADetachedAssemblyBlocksEarlyRelease()
        {
            Rocket rocket = new Rocket();
            rocket.Update(0);
            rocket.Booster.finalThrust = 150;
            rocket.Booster.EngineIgnited = false;
            rocket.Update(1);
            Assert.False(rocket.Update(2));
            rocket.Booster.EngineIgnited = true;
            rocket.Booster.engineType = EngineType.LiquidFuel;
            rocket.Update(3);
            Assert.False(rocket.Update(4));
        }

        [Fact]
        public void StageChangesInvalidatePreviouslyAllowedParts()
        {
            Rocket rocket = new Rocket();
            rocket.Update(0);
            rocket.Booster.finalThrust = 150;
            rocket.Update(1);
            Assert.True(rocket.Update(1.3));
            Assert.False(rocket.Update(2, stage: 1));
            Assert.False(rocket.Adapter.CanDrop(rocket.Booster.part));
        }
    }
}
