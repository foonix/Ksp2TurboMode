using KSP.Game;
using KSP.Sim;
using TurboMode.Sim.Components;
using Unity.Entities;

namespace TurboMode.Sim.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial class RefreshFromUniverse : SystemBase
    {
        protected override void OnUpdate()
        {
            var universeSim = SystemAPI.ManagedAPI.GetSingleton<UniverseRef>();

            var game = GameManager.Instance.Game;

            if (!game.IsSimulationRunning())
            {
                return;
            }

            Entities
                .WithName("UpdatePartData")
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
