using Improbable;
using Improbable.Collections;
using Improbable.Worker;
using ItemGenerator;
using NetworkOptimization;
using RogueFleet.Asteroids;
using RogueFleet.Core;
using RogueFleet.Items;
using RogueFleet.Ships;
using RogueFleet.Ships.Modules;
using System;
using Xoshiro.Base;
using Xoshiro.PRNG32;

namespace SpawnerWorker
{
    static class EntityTemplates
    {
        static readonly WorkerAttributeSet ShipAttribute = new WorkerAttributeSet(new List<string>() { "Ship" });
        static readonly WorkerAttributeSet AsteroidAttribute = new WorkerAttributeSet(new List<string> { "Asteroid" });
        static readonly WorkerAttributeSet UnityGameLogicAttribute = new WorkerAttributeSet(new List<string>() { "UnityGameLogic" });
        static readonly WorkerAttributeSet UnityClientAttribute = new WorkerAttributeSet(new List<string>() { "UnityClient" });

        static readonly WorkerRequirementSet ShipRequirementSet = new WorkerRequirementSet(new List<WorkerAttributeSet>() { ShipAttribute });
        static readonly WorkerRequirementSet AsteroidRequirementSet = new WorkerRequirementSet(new List<WorkerAttributeSet>() { AsteroidAttribute });
        static readonly WorkerRequirementSet UnityGameLogicRequirementSet = new WorkerRequirementSet(new List<WorkerAttributeSet>() { UnityGameLogicAttribute });

        static readonly MobileData defaultShipMobileData = new MobileData(55f, (float)Math.PI / 13f, 0f, (float)Math.PI / 8f, 34f, 0f);

        static PhysicalData DefaultPhysicalData(Coordinates initPosition)
        {
            var bytesPos = Bytes.FromBackingArray(Encode.Vector3f((float)initPosition.x, (float)initPosition.y, (float)initPosition.z));
            var shipPhysicsData = new PhysicalData(bytesPos, Bytes.FromBackingArray(new byte[6]), Bytes.FromBackingArray(new byte[7]), Bytes.FromBackingArray(new byte[6]), 0f, 0f);

            return shipPhysicsData;
        }

        static readonly WorkerRequirementSet defaultShipReadACL = new WorkerRequirementSet(new List<WorkerAttributeSet>() { ShipAttribute, UnityGameLogicAttribute, UnityClientAttribute });

        static readonly Map<uint, WorkerRequirementSet> defaultShipWriteACL = new Map<uint, WorkerRequirementSet>()
        {
            { 50, ShipRequirementSet },//ACL
            { 53, ShipRequirementSet },//Metadata
            { 54, UnityGameLogicRequirementSet },//Position
            { 55, ShipRequirementSet },//Persistence
            { 58, ShipRequirementSet },//Interest

            { 103, ShipRequirementSet },//Identification

            { 1000, UnityGameLogicRequirementSet },//Physical
            { 1001, ShipRequirementSet },//Mobile
            { 1002, ShipRequirementSet },//Damageable
            { 1003, ShipRequirementSet },//Rechargeable
            { 1004, ShipRequirementSet },//Armed
            { 1005, ShipRequirementSet },//Toggleable
            { 1006, UnityGameLogicRequirementSet },//ExplorationPhysics
            //{ 1007, ShipRequirementSet },//MapContainer
            { 1008, ShipRequirementSet },//Modular
            { 1009, ShipRequirementSet },//Sensor
            { 1010, ShipRequirementSet },//Scanner
            { 1011, ShipRequirementSet },//Sampler
            { 1012, ShipRequirementSet },//Docked

            { 3000, ShipRequirementSet },//Inventory
            { 3001, ShipRequirementSet },//Crafting
            { 3002, ShipRequirementSet },//Resource Items
            { 3003, ShipRequirementSet },//Map Items
            { 3004, ShipRequirementSet },//Module Items
        };

        static readonly WorkerRequirementSet asteroidDefaultReadACL = new WorkerRequirementSet(new List<WorkerAttributeSet>() { AsteroidAttribute, UnityGameLogicAttribute, UnityClientAttribute });

        static readonly Map<uint, WorkerRequirementSet> defaultAsteroidWriteACL = new Map<uint, WorkerRequirementSet>()
        {
            { 50, AsteroidRequirementSet },//ACL
            { 53, AsteroidRequirementSet },//Metadata
            { 54, AsteroidRequirementSet },//Position
            { 55, AsteroidRequirementSet },//Persistence
            { 58, AsteroidRequirementSet },//Interest

            { 103, AsteroidRequirementSet },//Identification

            { 2000, AsteroidRequirementSet },//Harvestable

            { 3000, AsteroidRequirementSet },//Inventory
            { 3002, AsteroidRequirementSet },//Resource Items
        };

        static readonly string[] variants = new string[6] { "Asteroid1", "Asteroid2", "Asteroid3", "Asteroid4", "Asteroid5", "Asteroid6" };

        static readonly IRandomU random = new XoShiRo128starstar();//Random needs to be locked when accesed concurrently
        static readonly object randomLock = new object();

        const float spawnRadius = 2500f;

        internal static Entity DefaultAsteroid(Coordinates initPosition)
        {
            var entity = new Entity();

            entity.Add(EntityAcl.Metaclass, new EntityAclData(asteroidDefaultReadACL, defaultAsteroidWriteACL));
            entity.Add(Position.Metaclass, new PositionData(initPosition));

            var index = 0;

            lock (randomLock)
            {
                index = random.Next(variants.Length);
            }

            entity.Add(Metadata.Metaclass, new MetadataData(variants[index]));

            entity.Add(Harvestable.Metaclass, new HarvestableData());

            return entity;
        }

        internal static Entity BasicScoutShip(string userDBId, string shipDBId, string clientWorkerId)
        {
            var clientRequirementSet = new WorkerRequirementSet(new List<WorkerAttributeSet> { new WorkerAttributeSet(new List<string>() { "workerId:" + clientWorkerId }) });

            var shipWriteACL = new Map<uint, WorkerRequirementSet>(defaultShipWriteACL)
            {
                { 102, clientRequirementSet },//ClientConnection

                { 1500, clientRequirementSet },//FlightController
                { 1501, clientRequirementSet },//EnergyContoller
                { 1502, clientRequirementSet },//TriggerContoller
                { 1503, clientRequirementSet },//ToggleContoller
                { 1504, clientRequirementSet },//TurretsContoller
                { 1505, clientRequirementSet },//TargetingContoller
                //{ 1506, clientRequirementSet },//ScannersController
                { 1507, clientRequirementSet },//FlaksController

                { 3500, clientRequirementSet },//Crafting Controller
                { 3501, clientRequirementSet },//Mapping Controller
            };

            var entity = new Entity();

            entity.Add(EntityAcl.Metaclass, new EntityAclData(defaultShipReadACL, shipWriteACL));
            entity.Add(Metadata.Metaclass, new MetadataData("StarterShip"));

            entity.Add(ClientConnection.Metaclass, new ClientConnectionData());
            entity.Add(Identification.Metaclass, new IdentificationData(userDBId, shipDBId, clientWorkerId));
            entity.Add(ExplorationPhysics.Metaclass, new ExplorationPhysicsData(null, null));

            entity.Add(Mobile.Metaclass, defaultShipMobileData);

            entity.Add(FlightController.Metaclass, new FlightControllerData(21));// 21 == no action in bits
            entity.Add(TriggerController.Metaclass, new TriggerControllerData());

            return entity;
        }

        internal static void SetRandomStartingPosition(Entity ship)
        {
            var coords = RandomShipSpawn();

            SetStartingPosition(coords, ship);
        }

        internal static void SetStartingPosition(Coordinates coordinates, Entity ship)
        {
            ship.Add(Position.Metaclass, new PositionData(coordinates));
            ship.Add(Physical.Metaclass, DefaultPhysicalData(coordinates));
        }

        internal static void AddStarterModules(Entity ship)
        {
            ship.Add(Damageable.Metaclass, new DamageableData());

            ship.Add(Rechargeable.Metaclass, new RechargeableData
            {
                moduleIds = new List<int>() { 0, 1, 2 },

                drainsSustained = new List<int>() { 0, 0, 0 },
                drainsLeft = new List<int>() { 0, 0, 0 },
                drainsRates = new List<int>() { 0, 0, 0 },

                useDrainsTotal = new List<int>() { 30000, 30000, 30000 },
                useDrainsRate = new List<int>() { 30000, 30000, 30000 },
                thresholds = new List<int>() { 0, 0, 0, },
            });

            ship.Add(Modular.Metaclass, new ModularData
            {
                installedModuleIds = new List<int>() { 0, 1, 2},
            });

            ship.Add(Sensor.Metaclass, new SensorData
            {
                sensors = new Map<int, SensorStat>()
                {
                    { 0, new SensorStat(10, new List<Dimensionality> { new Dimensionality(1000, 250), new Dimensionality(360, 45) })}
                },
            });

            ship.Add(Scanner.Metaclass, new ScannerData
            {
                scanners = new Map<int, ScannerStat>()
                {
                    { 1, new ScannerStat(100, 100, 200, (int)ResourceType.Random) },
                },
            });

            ship.Add(Sampler.Metaclass, new SamplerData
            {
                samplers = new Map<int, SamplerStat>()
                {
                    { 2, new SamplerStat(100) },
                },
            });
        }

        static Coordinates RandomShipSpawn()
        {
            var u = 0d;
            var v = 0d;

            lock (randomLock)
            {
                u = random.NextFloat();
                v = random.NextFloat();
            }
            
            //Uniformly distributed spherical coordinates
            var theta = 2d * Math.PI * u;
            var phi = Math.Acos(2d * v - 1d);

            //Spherical to Cartesian
            var x = spawnRadius * Math.Cos(theta) * Math.Sin(phi);
            var y = spawnRadius * Math.Sin(theta) * Math.Sin(phi);
            var z = spawnRadius * Math.Cos(phi);

            return new Coordinates(x, y, z);
        }
    }
}
