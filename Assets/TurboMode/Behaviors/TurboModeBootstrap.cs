using KSP.Game;
using KSP.Messages;
using TurboMode.Models;
using UnityEngine;

namespace TurboMode.Behaviors
{
    public class TurboModeBootstrap : MonoBehaviour
    {
        GameInstance gameInstance;

        private void Start()
        {
            Debug.Log("TM: bootstrap starting");
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (!GameManager.Instance || !GameManager.Instance.Game)
            {
                return;
            }

            gameInstance = GameManager.Instance.Game;

            if (!gameInstance.IsInitialized)
            {
                return;
            }

            Debug.Log("TM: bootstrapping gameInstance events");

            gameInstance.Messages.Subscribe<VesselCreatedMessage>((message) =>
            {
                var createdMessage = message as VesselCreatedMessage;
                Debug.Log($"TM: Vessel created message {createdMessage.vehicle} {createdMessage.SerializedVessel} {createdMessage.serializedLocation} ({Time.frameCount})");
            });

            gameInstance.Messages.Subscribe<PartBehaviourInitializedMessage>((message) =>
            {
                var msg = message as PartBehaviourInitializedMessage;
                var part = msg.Part;
                Debug.Log($"TM: Part initialized message {part.name} ({Time.frameCount})");

                // The game will "smear" part add overhead across multiple frames before calling
                // VesselBehaviorInitializedMessage.  So smear collider ignore in the same way.
                var simObj = msg.Part.SimObjectComponent.SimulationObject;
                if (!simObj.TryFindComponent<VesselSelfCollide>(out VesselSelfCollide vsc))
                {
                    simObj.AddComponent(
                    vsc = new VesselSelfCollide(part.vessel.SimObjectComponent),
                        0 // fixme
                        );
                }
                vsc.AddPartAdditive(part);
            });

            gameInstance.Messages.Subscribe<VesselBehaviorInitializedMessage>((message) =>
            {
                var msg = message as VesselBehaviorInitializedMessage;
                Debug.Log($"TM: Vessel initialized message {msg.NewVesselBehavior.name} ({Time.frameCount})");

                var simObj = msg.NewVesselBehavior.SimObjectComponent.SimulationObject;
                if (!simObj.TryFindComponent<VesselSelfCollide>(out var _))
                {
                    msg.NewVesselBehavior.SimObjectComponent.SimulationObject.AddComponent(
                        new VesselSelfCollide(msg.NewVesselBehavior.SimObjectComponent),
                        0 // fixme
                        );
                }
            });

            gameInstance.Messages.Subscribe<VesselDockedMessage>((message) =>
            {
                var msg = message as VesselDockedMessage;
                Debug.Log($"TM: Vessel docked message {msg.VesselOne.name} {msg.VesselTwo} ({Time.frameCount})");
            });

            gameInstance.Messages.Subscribe<VesselSplitMessage>((message) =>
            {
                var msg = message as VesselSplitMessage;
                Debug.Log($"TM: Vessel split message {msg.remainingVessel.Name} {msg.newVessel.Name} {msg.isNewVesselFromSubVessel} ({Time.frameCount})");
                var simObj = msg.newVessel.SimulationObject;
                if (!simObj.TryFindComponent<VesselSelfCollide>(out var vsc))
                {
                    
                    simObj.AddComponent(
                        vsc = new VesselSelfCollide(msg.newVessel),
                        0 // fixme
                        );

                    vsc.TrackPartsAfterSplit();
                }
            });

            gameObject.SetActive(false); // suppress Update() overhead
        }
    }
}
