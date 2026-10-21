using System;
using System.Collections.Generic;
using System.Reflection;

namespace MuMech
{
    internal sealed class SolidBoosterSeparationFlightAdapter
    {
        private sealed class Assembly
        {
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<ModuleEngines> Engines = new List<ModuleEngines>();
        }

        private readonly SolidBoosterSeparation _policy = new SolidBoosterSeparation();
        private readonly List<Assembly> _assemblies = new List<Assembly>();
        private readonly List<Part> _retainedParts = new List<Part>();
        private readonly List<ModuleEngines> _retainedEngines = new List<ModuleEngines>();
        private readonly List<SolidBoosterSeparation.Group> _groups = new List<SolidBoosterSeparation.Group>();
        private readonly HashSet<Part> _allowedParts = new HashSet<Part>();
        private readonly HashSet<ModuleEngines> _burningEngines = new HashSet<ModuleEngines>();
        private readonly Dictionary<Type, MethodInfo> _propellantChecks = new Dictionary<Type, MethodInfo>();
        private int _stage = int.MinValue;
        private int _partCount;
        private int _engineCount;
        private double _lastTime = double.NaN;

        public bool Update(
            IReadOnlyList<Part> parts
            , IReadOnlyList<ModuleEngines> engines
            , int stage
            , double time
            , Vector3d axis)
        {
            if (
                time < _lastTime
                || double.IsNaN(time)
                || double.IsInfinity(time))
                Reset();
            if (double.IsNaN(time) || double.IsInfinity(time)) return false;

            if (
                _stage != stage
                || _partCount != parts.Count
                || _engineCount != engines.Count)
            {
                Rebuild(
                    parts
                    , engines
                    , stage);
            }
            _lastTime = time;

            _allowedParts.Clear();
            double retainedMass = 0;
            foreach (Part part in _retainedParts)
            {
                retainedMass += WetMass(part);
            }

            double retainedThrust = 0;
            foreach (ModuleEngines engine in _retainedEngines)
            {
                retainedThrust += AxialThrust(engine, axis);
            }

            _groups.Clear();
            bool allReady = true;
            bool firstBurnout = false;
            for (int i = 0; i < _assemblies.Count; i++)
            {
                Assembly assembly = _assemblies[i];
                if (assembly.Engines.Count == 0) continue;

                double mass = 0;
                foreach (Part part in assembly.Parts)
                {
                    mass += WetMass(part);
                }

                double thrust = 0;
                bool ready = true;
                foreach (ModuleEngines engine in assembly.Engines)
                {
                    thrust += AxialThrust(engine, axis);
                    bool solidReady = engine.engineType == EngineType.SolidBooster
                        && engine.EngineIgnited
                        && engine.isEnabled;
                    ready &= solidReady;
                    if (
                        solidReady
                        && engine.finalThrust > 0
                        && !float.IsInfinity(engine.finalThrust))
                        _burningEngines.Add(engine);
                    if (
                        solidReady
                        && _burningEngines.Contains(engine)
                        && HasRunOutOfPropellant(engine))
                        firstBurnout = true;
                }

                allReady &= ready;
                _groups.Add(new SolidBoosterSeparation.Group(
                    i
                    , mass
                    , thrust
                    , ready));
            }

            // Once a solid really burns out, release the whole stage promptly
            // instead of waiting for its still-useful peers and flying asymmetric.
            if (!(allReady && firstBurnout) && !_policy.Update(
                    time
                    , retainedMass
                    , retainedThrust
                    , _groups))
                return false;

            foreach (Assembly assembly in _assemblies)
            {
                foreach (ModuleEngines engine in assembly.Engines)
                {
                    _allowedParts.Add(engine.part);
                }
            }

            return true;
        }

        public bool CanDrop(Part part) => _allowedParts.Contains(part);

        public void Reset()
        {
            _policy.Reset();
            _assemblies.Clear();
            _retainedParts.Clear();
            _retainedEngines.Clear();
            _groups.Clear();
            _allowedParts.Clear();
            _burningEngines.Clear();
            _stage = int.MinValue;
            _lastTime = double.NaN;
        }

        private bool HasRunOutOfPropellant(ModuleEngines engine)
        {
            if (
                !engine.getFlameoutState
                || engine.engineShutdown
                || engine.finalThrust != 0)
                return false;

            // Auxiliary starvation may recover and must not discard fueled peers.
            foreach (Propellant propellant in engine.propellants)
            {
                if (propellant.ignoreForIsp && propellant.totalResourceAvailable <= 0) return false;
            }

            // SolverEngines/RealFuels know which fuel is usable after residuals.
            // Keep this optional so stock engines need no extra assembly reference.
            Type type = engine.GetType();
            if (!_propellantChecks.TryGetValue(type, out MethodInfo check))
            {
                check = type.GetMethod(
                    "PropellantAvailable"
                    , BindingFlags.Public | BindingFlags.Instance
                    , null
                    , Type.EmptyTypes
                    , null);
                _propellantChecks.Add(type, check);
            }

            if (check != null)
            {
                try
                {
                    return check.Invoke(engine, null) is bool available && !available;
                }
                catch (TargetInvocationException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            foreach (Propellant propellant in engine.propellants)
            {
                if (
                    !propellant.ignoreForIsp
                    && propellant.totalResourceCapacity > 0
                    && (propellant.isDeprived || propellant.totalResourceAvailable <= 0))
                    return true;
            }
            return false;
        }

        private void Rebuild(
            IReadOnlyList<Part> parts
            , IReadOnlyList<ModuleEngines> engines
            , int stage)
        {
            Reset();
            _stage = stage;
            _partCount = parts.Count;
            _engineCount = engines.Count;

            Dictionary<Part, Assembly> roots = new Dictionary<Part, Assembly>();
            Dictionary<Part, Assembly> membership = new Dictionary<Part, Assembly>();
            foreach (Part part in parts)
            {
                if (!part.IsDecoupledInStage(stage))
                {
                    _retainedParts.Add(part);
                    continue;
                }

                // Use the highest detached ancestor so nested decouplers and
                // multi-part boosters cannot count the same mass twice.
                Part root = part;
                while (root.parent != null && root.parent.IsDecoupledInStage(stage)) root = root.parent;

                if (!roots.TryGetValue(root, out Assembly assembly))
                {
                    assembly = new Assembly();
                    roots.Add(root, assembly);
                    _assemblies.Add(assembly);
                }

                assembly.Parts.Add(part);
                membership.Add(part, assembly);
            }

            foreach (ModuleEngines engine in engines)
            {
                if (membership.TryGetValue(engine.part, out Assembly assembly)) assembly.Engines.Add(engine);
                else _retainedEngines.Add(engine);
            }
        }

        // KSP has already applied CURRENT module mass modifiers to part.mass.
        // This also counts physicsless accessories exactly as Vessel.GetTotalMass does.
        private static double WetMass(Part part) => part.mass + part.GetResourceMass();

        private static double AxialThrust(ModuleEngines engine, Vector3d axis)
        {
            if (engine.finalThrust == 0) return 0;
            if (
                engine.finalThrust < 0
                || engine.thrustTransforms.Count == 0
                || engine.thrustTransforms.Count != engine.thrustTransformMultipliers.Count)
                return double.NaN;

            double projection = 0;
            for (int i = 0; i < engine.thrustTransforms.Count; i++)
            {
                projection += Vector3d.Dot(-engine.thrustTransforms[i].forward, axis) * engine.thrustTransformMultipliers[i];
            }

            return engine.finalThrust * projection;
        }
    }
}
