using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Auth;
using Grpc.Core;
using Improbable.Worker;
using RogueFleet.Core;
using SpawnerWorker.ECS;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SpawnerWorker
{
    public static class Startup
    {
        const string WorkerType = "spawner_worker";

        const string GoogleCredentialFile = "RogueFleetOnline-f755aa7ec387.json";
        const string FirebaseProjectId = "roguefleetonline";

        const int ErrorExitStatus = 1;

        static readonly Stopwatch stopwatch = new Stopwatch();

        static int Main(string[] args)
        {
            if (args.Length != 4)
            {
                PrintUsage();
                return ErrorExitStatus;
            }

            stopwatch.Restart();

            // Avoid missing component errors because no components are directly used in this project
            // and the GeneratedCode assembly is not loaded but it should be
            Assembly.Load("GeneratedCode");

            using (var connection = ConnectWithReceptionist(args[1], Convert.ToUInt16(args[2]), args[3]))
            {
                var connected = true;

                var channel = new Channel(FirestoreClient.DefaultEndpoint.Host, GoogleCredential.FromFile(Path.Combine(Directory.GetCurrentDirectory(), GoogleCredentialFile)).ToChannelCredentials());
                var database = FirestoreDb.Create(FirebaseProjectId, FirestoreClient.Create(channel));

                FirebaseJWT.PeriodicKeyUpdate();

                SpatialOSConnectionSystem.connection = connection;

                ShipsBasicSystem.Firestore = database;
                ShipsAdditionSystem.Firestore = database;

                var dispatcher = new Dispatcher();
                
                dispatcher.OnDisconnect(op =>
                {
                    Console.Error.WriteLine("[disconnect] " + op.Reason);
                    connected = false;
                });

                dispatcher.OnCommandRequest(PlayerSpawner.Commands.CreatePlayer.Metaclass, RequestsVerifierSystem.OnCreatePlayerRequest);
                dispatcher.OnCommandRequest(AsteroidSpawner.Commands.PopulateGridCell.Metaclass, AsteroidProcGenSystem.OnPopulateGridCell);

                stopwatch.Stop();

                connection.SendLogMessage(LogLevel.Info, "Initialization", string.Format("Init Time {0}ms", stopwatch.Elapsed.TotalMilliseconds.ToString("N0")));

                var maxWait = TimeSpan.FromMilliseconds(100);
                while (connected)
                {
                    stopwatch.Restart();

                    using (var opList = connection.GetOpList(0))
                    {
                        dispatcher.Process(opList);
                    }

                    Parallel.Invoke(
                        RequestsVerifierSystem.Update,
                        ShipsBasicSystem.Update,
                        ShipsAdditionSystem.Update,
                        ShipsConstructorSystem.Update,
                        AsteroidProcGenSystem.Update,
                        SpatialOSConnectionSystem.Update
                        );

                    stopwatch.Stop();

                    var frameTime = stopwatch.Elapsed;
                    if (frameTime > maxWait)
                    {
                        connection.SendLogMessage(LogLevel.Warn, "Game Loop", string.Format("Frame Time {0}ms", frameTime.TotalMilliseconds.ToString("N0")));
                    }
                    else Thread.Sleep(maxWait - frameTime);
                }
                
                SpatialOSConnectionSystem.connection = null;

                channel.ShutdownAsync().Wait();
            }

            // This means we forcefully disconnected
            return ErrorExitStatus;
        }

        static Connection ConnectWithReceptionist(string hostname, ushort port, string workerId)
        {
            var connectionParameters = new ConnectionParameters
            {
                WorkerType = WorkerType,
                Network =
                {
                    ConnectionType = NetworkConnectionType.Tcp,
                    UseExternalIp = false,
                }
            };

            using (var future = Connection.ConnectAsync(hostname, port, workerId, connectionParameters))
            {
                return future.Get();
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: mono Managed.exe receptionist <hostname> <port> <worker_id>");
            Console.WriteLine("Connects to SpatialOS");
            Console.WriteLine("    <hostname>      - hostname of the receptionist to connect to.");
            Console.WriteLine("    <port>          - port to use");
            Console.WriteLine("    <worker_id>     - name of the worker assigned by SpatialOS.");
        }
    }
}