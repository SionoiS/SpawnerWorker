using Google.Cloud.Firestore;
using Improbable.Worker;
using ItemGenerator;
using System.Collections.Generic;
using Xoshiro.Base;
using Xoshiro.PRNG32;

namespace SpawnerWorker.ECS
{
    public static class ShipsBasicSystem
    {
        static FirestoreDb firestore;
        public static FirestoreDb Firestore
        {
            set
            {
                firestore = value;
            }
        }

        static readonly string UserCollection = "users";
        static readonly string ShipCollection = "ships";

        static readonly object opLock = new object();
        static readonly List<(string tokenOutput, string clientWorkerId)> ops = new List<(string userDBId, string clientWorkerId)>();

        static readonly IRandomU random = new XoShiRo128starstar();

        internal static void AddOp(string tokenOutput, string clientWorkerId)
        {
            lock (opLock)
            {
                ops.Add((tokenOutput, clientWorkerId));
            }
        }

        internal static void Update()
        {
            ProcessOps();
        }

        static void ProcessOps()
        {
            var count = ops.Count;

            if (count <= 0)
            {
                return;
            }

            var refs = new DocumentReference[count];
            var ships = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                var (tokenOutput, clientWorkerId) = ops[i];

                var userDBId = tokenOutput.Substring(0, 20);

                string shipDBId;
                if (tokenOutput.Length > 20)
                {
                    shipDBId = tokenOutput.Substring(20, 20);
                }
                else
                {
                    shipDBId = Helpers.GenerateCloudFireStoreRandomDocumentId(random);
                }

                refs[i] = firestore.Collection(UserCollection).Document(userDBId).Collection(ShipCollection).Document(shipDBId);
                ships[i] = EntityTemplates.BasicScoutShip(userDBId, shipDBId, clientWorkerId);
            }

            lock (opLock)
            {
                ops.RemoveRange(0, count);
            }

            ShipsAdditionSystem.AddOps(refs, ships);
        }
    }
}
