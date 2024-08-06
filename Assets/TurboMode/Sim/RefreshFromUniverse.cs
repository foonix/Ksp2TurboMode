using KSP.Sim;
using Unity.Entities;

namespace TurboMode.Sim
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial class RefreshFromUniverse : SystemBase
    {
        protected override void OnUpdate()
        {
            var universeSim = SystemAPI.ManagedAPI.GetSingleton<UniverseRef>();

            Entities
                .WithName("UpdateSimObj")
                .ForEach(
                (in SimObject simObj) =>
                {
                    var obj = universeSim.universeModel.FindSimObject(simObj.guid);
                })
                .WithoutBurst()
                .Run();
        }
    }
}
