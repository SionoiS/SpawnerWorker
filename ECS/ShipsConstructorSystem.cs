using Google.Cloud.Firestore;
using Improbable.Worker;
using ItemGenerator.Modules;
using ItemGenerator.Resources;
using RogueFleet.Items;
using RogueFleet.Ships.Modules;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpawnerWorker.ECS
{
    static class ShipsConstructorSystem
    {
        static readonly object opLock = new object();
        static readonly List<DocumentReference> shipRefs = new List<DocumentReference>();
        static readonly List<Entity> shipEntities = new List<Entity>();

        static readonly string ModuleCollection = "modules";
        static readonly string ResourceCollection = "ress";

        internal static void AddOps(List<DocumentReference> refs, List<Entity> ships)
        {
            lock (opLock)
            {
                shipRefs.AddRange(refs);
                shipEntities.AddRange(ships);
            }
        }

        internal static void Update()
        {
            ProcessOps();
        }

        static void ProcessOps()
        {
            var count = shipRefs.Count;

            if (count <= 0)
            {
                return;
            }

            //using a Span<T> to work on all the data could work well here.

            var getSnapshotTasks = new Task<QuerySnapshot>[count];
            for (int i = 0; i < count; i++)
            {
                getSnapshotTasks[i] = shipRefs[i].Collection(ModuleCollection).GetSnapshotAsync();
            }

            var resourceInventoryTasks = new Task<ResourceInventoryData>[count];
            for (int i = 0; i < count; i++)
            {
                resourceInventoryTasks[i] = GetResourceInventory(shipRefs[i]);
            }

            Task.WaitAll(getSnapshotTasks);

            var moduleInventoryTasks = new Task<ModuleInventoryData>[count];
            for (int i = 0; i < count; i++)
            {
                moduleInventoryTasks[i] = GetModuleInventory((DocumentSnapshot[])getSnapshotTasks[i].Result.Documents);
            }

            Task.WaitAll(Task.WhenAll(moduleInventoryTasks), Task.WhenAll(resourceInventoryTasks));

            for (int i = 0; i < count; i++)
            {
                var ship = shipEntities[i];

                var moduleSnapshots = (DocumentSnapshot[])getSnapshotTasks[i].Result.Documents;

                var modules = new Module[moduleSnapshots.Length];
                for (int j = 0; j < modules.Length; j++)
                {
                    modules[i] = moduleSnapshots[i].ConvertTo<Module>();
                }

                ship.Add(Damageable.Metaclass, new DamageableData());

                ship.Add(ModuleInventory.Metaclass, moduleInventoryTasks[i].Result);
                ship.Add(ResourceInventory.Metaclass, resourceInventoryTasks[i].Result);

                ship.Add(Modular.Metaclass, GetModularComponent(moduleSnapshots));

                ship.Add(Sensor.Metaclass, GetSensorComponent(modules));
                ship.Add(Scanner.Metaclass, GetScannerComponent(modules));
                ship.Add(Sampler.Metaclass, GetSamplerComponent(modules));

                SpatialOSConnectionSystem.entitiesToCreate.Enqueue(ship);
            }

            lock (opLock)
            {
                shipRefs.RemoveRange(0, count);
                shipEntities.RemoveRange(0, count);
            }
        }

        static async Task<ModuleInventoryData> GetModuleInventory(DocumentSnapshot[] moduleSnapshots)
        {
            var count = moduleSnapshots.Length;

            var modules = new Improbable.Collections.Map<int, ModuleInfo>(count);
            var modulesResources = new Improbable.Collections.Map<int, ModuleResources>(count);

            var tasks = new Task<ModuleResources>[count];
            for (int i = 0; i < count; i++)
            {
                var moduleSnapshot = moduleSnapshots[i];

                var module = moduleSnapshot.ConvertTo<Module>();

                var moduleInfo = new ModuleInfo(moduleSnapshot.Id, module.Name, module.Type, module.Creator, module.Properties);

                modules[i] = moduleInfo;

                tasks[i] = GetModuleResources(moduleSnapshot.Reference);
            }

            await Task.WhenAll(tasks);

            for (int i = 0; i < count; i++)
            {
                modulesResources[i] = tasks[i].Result;
            }

            return new ModuleInventoryData(modules, modulesResources);
        }

        static async Task<ModuleResources> GetModuleResources(DocumentReference moduleRef)
        {
            var resourceQuery = await moduleRef.Collection(ResourceCollection).GetSnapshotAsync();
            var resourceSnapshots = resourceQuery.Documents;

            var list = new Improbable.Collections.List<ResourceInfo>(resourceQuery.Count);
            for (int i = 0; i < resourceQuery.Count; i++)
            {
                var resourceSnapshot = resourceSnapshots[i];
                var resource = resourceSnapshot.ConvertTo<Resource>();

                list.Add(new ResourceInfo(resourceSnapshot.Id, resource.Type, resource.Quantity));
            }

            return new ModuleResources(list);
        }

        static async Task<ResourceInventoryData> GetResourceInventory(DocumentReference shipRef)
        {
            var resourceQuery = await shipRef.Collection(ResourceCollection).GetSnapshotAsync();
            var resourceSnapshots = (DocumentSnapshot[])resourceQuery.Documents;

            var resources = new Improbable.Collections.Map<int, ResourceInfo>(resourceQuery.Count);

            for (int i = 0; i < resourceQuery.Count; i++)
            {
                var snapshot = resourceSnapshots[i];

                var resource = snapshot.ConvertTo<Resource>();

                resources[i] = new ResourceInfo(snapshot.Id, resource.Type, resource.Quantity);
            }

            return new ResourceInventoryData(resources);
        }

        static ModularData GetModularComponent(DocumentSnapshot[] moduleSnapshots)
        {
            var installList = new Improbable.Collections.List<int>(moduleSnapshots.Length);

            for (int i = 0; i < moduleSnapshots.Length; i++)
            {
                moduleSnapshots[i].TryGetValue<bool>("equip", out var installed);

                if (installed)
                {
                    installList.Add(i);
                }
            }

            return new ModularData(installList, ShipType.Starter);
        }

        static SensorData GetSensorComponent(Module[] modules)
        {
            var sensors = new Improbable.Collections.Map<int, SensorStat>();

            for (int i = 0; i < modules.Length; i++)
            {
                if (modules[i].Type == ModuleType.Sensor)
                {
                    sensors[i] = Sensors.Craft(modules[i].Properties.BackingArray);
                }
            }

            return new SensorData(sensors);
        }

        static ScannerData GetScannerComponent(Module[] modules)
        {
            var scanners = new Improbable.Collections.Map<int, ScannerStat>();

            for (int i = 0; i < modules.Length; i++)
            {
                if (modules[i].Type == ModuleType.Scanner)
                {
                    scanners[i] = Scanners.Craft(modules[i].Properties.BackingArray);
                }
            }

            return new ScannerData(scanners);
        }

        static SamplerData GetSamplerComponent(Module[] modules)
        {
            var samplers = new Improbable.Collections.Map<int, SamplerStat>();

            for (int i = 0; i < modules.Length; i++)
            {
                if (modules[i].Type == ModuleType.Sampler)
                {
                    samplers[i] = Samplers.Craft(modules[i].Properties.BackingArray);
                }
            }

            return new SamplerData(samplers);
        }
    }
}
