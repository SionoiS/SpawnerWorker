using Google.Cloud.Firestore;
using Improbable;
using Improbable.Worker;
using System.Collections.Generic;

namespace SpawnerWorker.ECS
{
    static class ShipsAdditionSystem
    {
        static FirestoreDb firestore;
        public static FirestoreDb Firestore
        {
            set
            {
                firestore = value;
            }
        }

        static readonly object opLock = new object();
        static readonly List<DocumentReference> shipReferences = new List<DocumentReference>();
        static readonly List<Entity> shipEntities = new List<Entity>();

        static readonly FieldPath ShipCoordinates = new FieldPath("coords");

        internal static void AddOps(DocumentReference[] shiprefs, Entity[] ships)
        {
            lock (opLock)
            {
                shipReferences.AddRange(shiprefs);
                shipEntities.AddRange(ships);
            }
        }

        internal static void Update()
        {
            ProcessOps();
        }

        static async void ProcessOps()
        {
            if (shipReferences.Count < 1)
            {
                return;
            }

            var shipSnapshots = (DocumentSnapshot[])await firestore.GetAllSnapshotsAsync(shipReferences);
            var count = shipSnapshots.Length;

            var ships = new List<Entity>(count);
            for (int i = 0; i < count; i++)
            {
                var shipSnapshot = shipSnapshots[i];
                var ship = shipEntities[i];

                if (!shipSnapshot.Exists)//(new?) player with no ship
                {
                    EntityTemplates.SetRandomStartingPosition(ship);
                    EntityTemplates.AddStarterModules(ship);

                    SpatialOSConnectionSystem.entitiesToCreate.Enqueue(ship);
                }
                else//returning player with ship
                {
                    if (shipSnapshot.TryGetValue<double[]>(ShipCoordinates, out var xyz))
                    {
                        EntityTemplates.SetStartingPosition(new Coordinates(xyz[0], xyz[1], xyz[2]), ship);
                    }
                    else
                    {
                        EntityTemplates.SetRandomStartingPosition(ship);
                    }

                    ships.Add(ship);
                }
            }

            ShipsConstructorSystem.AddOps(shipReferences, ships);

            lock (opLock)
            {
                shipReferences.RemoveRange(0, count);
                shipEntities.RemoveRange(0, count);
            }
        }
    }
}
