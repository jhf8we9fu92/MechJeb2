using System;
using MuMech;
using Xunit;

namespace MechJebLibTest.StagingTests
{
    public class SolidBoosterSeparationTests
    {
        private static SolidBoosterSeparation.Group Booster(
            double thrust
            , double mass = 10
            , int id = 1
            , bool ready = true) =>
            new SolidBoosterSeparation.Group(
                id
                , mass
                , thrust
                , ready);

        [Fact]
        public void DropsAnEstablishedBoosterThatStillHasMoreThrustThanItsWeight()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            Assert.False(separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300) }));
            Assert.False(separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(150) }));
            Assert.True(separation.Update(
                1.3
                , 100
                , 2000
                , new[] { Booster(150) }));
        }

        [Fact]
        public void RetainsABoosterBelowOneGThatStillHelpsALowThrustCore()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 500
                , new[] { Booster(300) });
            separation.Update(
                1
                , 100
                , 500
                , new[] { Booster(80) });
            Assert.False(separation.Update(
                2
                , 100
                , 500
                , new[] { Booster(80) }));
        }

        [Fact]
        public void UsesTheWholeDetachedAssemblyMass()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(600, 20) });
            separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(250, 20) });
            Assert.True(separation.Update(
                1.3
                , 100
                , 2000
                , new[] { Booster(250, 20) }));
        }

        [Fact]
        public void OneWeakBoosterDoesNotDiscardAnotherUsefulBoosterInTheSameStage()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300), Booster(300, id: 2) });
            separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(0), Booster(250, id: 2) });
            Assert.False(separation.Update(
                2
                , 100
                , 2000
                , new[] { Booster(0), Booster(250, id: 2) }));
            Assert.False(separation.Update(
                3
                , 100
                , 2000
                , new[] { Booster(0), Booster(150, id: 2) }));
            Assert.True(separation.Update(
                3.3
                , 100
                , 2000
                , new[] { Booster(0), Booster(150, id: 2) }));
        }

        [Fact]
        public void IgnitionBuildupCannotBeMistakenForTailoff()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(0) });
            Assert.False(separation.Update(
                2
                , 100
                , 2000
                , new[] { Booster(100) }));
            Assert.False(separation.Update(
                3
                , 100
                , 2000
                , new[] { Booster(300) }));
            Assert.False(separation.Update(
                4
                , 100
                , 2000
                , new[] { Booster(150) }));
            Assert.True(separation.Update(
                4.3
                , 100
                , 2000
                , new[] { Booster(150) }));
        }

        [Fact]
        public void UnignitedOrMixedEngineAssembliesCannotBeReleasedEarly()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300) });
            separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(100, ready: false) });
            Assert.False(separation.Update(
                2
                , 100
                , 2000
                , new[] { Booster(100, ready: false) }));
        }

        [Fact]
        public void ThrustRecoveryRestartsTheConfirmationPeriod()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300) });
            separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(150) });
            Assert.False(separation.Update(
                1.2
                , 100
                , 2000
                , new[] { Booster(250) }));
            Assert.False(separation.Update(
                1.3
                , 100
                , 2000
                , new[] { Booster(150) }));
            Assert.False(separation.Update(
                1.4
                , 100
                , 2000
                , new[] { Booster(150) }));
            Assert.True(separation.Update(
                1.6
                , 100
                , 2000
                , new[] { Booster(150) }));
        }

        [Fact]
        public void CoreThrottleReductionCanMakeTheBoosterUsefulAgain()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300) });
            separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(150) });
            Assert.False(separation.Update(
                2
                , 100
                , 1000
                , new[] { Booster(150) }));
        }

        [Fact]
        public void EqualAccelerationDoesNotDiscardRemainingImpulse()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300) });
            separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(200) });
            Assert.False(separation.Update(
                2
                , 100
                , 2000
                , new[] { Booster(200) }));
        }

        [Theory]
        [InlineData(
            0
            , 2000
            , 10
            , 150)]
        [InlineData(
            -1
            , 2000
            , 10
            , 150)]
        [InlineData(
            double.NaN
            , 2000
            , 10
            , 150)]
        [InlineData(
            100
            , 0
            , 10
            , 150)]
        [InlineData(
            100
            , double.PositiveInfinity
            , 10
            , 150)]
        [InlineData(
            100
            , 2000
            , 0
            , 150)]
        [InlineData(
            100
            , 2000
            , double.NaN
            , 150)]
        [InlineData(
            100
            , 2000
            , 10
            , double.NaN)]
        [InlineData(
            100
            , 2000
            , 10
            , double.PositiveInfinity)]
        [InlineData(
            100
            , 2000
            , 10
            , -1)]
        public void InvalidMassOrThrustFailsClosed(
            double coreMass
            , double coreThrust
            , double boosterMass
            , double boosterThrust)
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300) });
            separation.Update(
                1
                , coreMass
                , coreThrust
                , new[] { Booster(boosterThrust, boosterMass) });
            Assert.False(separation.Update(
                2
                , coreMass
                , coreThrust
                , new[] { Booster(boosterThrust, boosterMass) }));
        }

        [Fact]
        public void AnEmptyStageCannotTriggerEarlySeparation()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            Assert.False(separation.Update(
                1
                , 100
                , 2000
                , Array.Empty<SolidBoosterSeparation.Group>()));
            Assert.False(separation.Update(
                2
                , 100
                , 2000
                , Array.Empty<SolidBoosterSeparation.Group>()));
        }

        [Fact]
        public void ResetForANewStageOrModeDiscardsOldArmingAndCountdown()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                0
                , 100
                , 2000
                , new[] { Booster(300) });
            separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(100) });
            separation.Reset();
            Assert.False(separation.Update(
                2
                , 100
                , 2000
                , new[] { Booster(100) }));
            Assert.False(separation.Update(
                3
                , 100
                , 2000
                , new[] { Booster(100) }));
        }

        [Fact]
        public void ClockRewindCannotReuseAConfirmationFromTheFuture()
        {
            SolidBoosterSeparation separation = new SolidBoosterSeparation();
            separation.Update(
                10
                , 100
                , 2000
                , new[] { Booster(300) });
            separation.Update(
                11
                , 100
                , 2000
                , new[] { Booster(100) });
            Assert.False(separation.Update(
                1
                , 100
                , 2000
                , new[] { Booster(100) }));
            Assert.False(separation.Update(
                2
                , 100
                , 2000
                , new[] { Booster(100) }));
        }
    }
}
