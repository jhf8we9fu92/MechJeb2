using System.Collections.Generic;

namespace MuMech
{
    // Determines when every detached booster stops improving thrust acceleration.
    internal sealed class SolidBoosterSeparation
    {
        internal readonly struct Group
        {
            public readonly int Id;
            public readonly double Mass;
            public readonly double Thrust;
            public readonly bool Ready;

            public Group(
                int id
                , double mass
                , double thrust
                , bool ready)
            {
                Id = id;
                Mass = mass;
                Thrust = thrust;
                Ready = ready;
            }
        }

        private const double CONFIRMATION_TIME = 0.25;
        private readonly HashSet<int> _establishedGroups = new HashSet<int>();
        private double _lastTime = double.NaN;
        private double _belowThresholdSince = double.NaN;

        public bool Update(
            double time
            , double retainedMass
            , double retainedThrust
            , IReadOnlyList<Group> groups)
        {
            if (!Finite(time) || time < _lastTime) Reset();
            _lastTime = time;

            if (
                !Finite(time)
                || !Positive(retainedMass)
                || !Positive(retainedThrust)
                || groups.Count == 0)
                return CancelConfirmation();

            double retainedAcceleration = retainedThrust / retainedMass;
            if (!Positive(retainedAcceleration)) return CancelConfirmation();

            bool allBelowThreshold = true;
            foreach (Group group in groups)
            {
                if (
                    !group.Ready
                    || !Positive(group.Mass)
                    || !Finite(group.Thrust)
                    || group.Thrust < 0)
                {
                    allBelowThreshold = false;
                    continue;
                }

                double acceleration = group.Thrust / group.Mass;
                // Observe useful thrust first: zero thrust during ignition must not
                // look like tailoff. Each detached assembly arms independently.
                if (acceleration > retainedAcceleration) _establishedGroups.Add(group.Id);

                if (!_establishedGroups.Contains(group.Id) || !(acceleration < retainedAcceleration)) allBelowThreshold = false;
            }

            if (!allBelowThreshold) return CancelConfirmation();

            if (double.IsNaN(_belowThresholdSince)) _belowThresholdSince = time;

            return time - _belowThresholdSince >= CONFIRMATION_TIME;
        }

        public void Reset()
        {
            _establishedGroups.Clear();
            _lastTime = double.NaN;
            _belowThresholdSince = double.NaN;
        }

        private bool CancelConfirmation()
        {
            _belowThresholdSince = double.NaN;
            return false;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool Positive(double value) => Finite(value) && value > 0;
    }
}
