#nullable disable
using System.Collections.Generic;
using MechJebLib.Primitives;

// Test-only KSP boundary for the linked flight adapter. The assembly
// grouping, live mass/thrust aggregation and separation policy remain real.
internal partial struct Vector3d
{
    public V3 Value;
    public static implicit operator Vector3d(V3 value) => new Vector3d { Value = value };
    public static double Dot(Vector3d a, Vector3d b) => V3.Dot(a.Value, b.Value);
    public static Vector3d operator -(Vector3d value) => -value.Value;
}

internal partial class Part
{
    public Part parent;
    public int DroppedInStage = -1;
    public float mass, ResourceMass;
    public float GetResourceMass() => ResourceMass;
}

internal enum EngineType { LiquidFuel, SolidBooster }

internal partial class Propellant
{
    public bool ignoreForIsp, isDeprived;
    public double totalResourceAvailable = 100, totalResourceCapacity = 100;
}

internal partial class ModuleEngines
{
    public bool EngineIgnited = true, isEnabled = true;
    public Part part;
    public EngineType engineType = EngineType.SolidBooster;
    public bool getFlameoutState, engineShutdown;
    public float finalThrust;
    public List<Propellant> propellants = new List<Propellant> { new Propellant() };
    public List<UnityEngine.Transform> thrustTransforms = new List<UnityEngine.Transform> { new UnityEngine.Transform() };
    public List<float> thrustTransformMultipliers = new List<float> { 1 };
}

namespace UnityEngine
{
    internal class Transform
    {
        public Vector3d forward = new V3(
            0
            , -1
            , 0);
    }
}

namespace MuMech
{
    internal static class StagingPartBoundary
    {
        public static bool IsDecoupledInStage(this Part part, int stage) =>
            part.DroppedInStage == stage || part.parent != null && part.parent.IsDecoupledInStage(stage);
    }
}
