using Improbable.Worker;
using RogueFleet.Core;
using System.Collections.Generic;
using System.Text;

namespace SpawnerWorker.ECS
{
    public static class RequestsVerifierSystem
    {
        const string LoggerName = "RequestsVerifierSystem";

        static readonly Queue<CommandRequestOp<PlayerSpawner.Commands.CreatePlayer, CreatePlayerRequest>> commandRequestOps = new Queue<CommandRequestOp<PlayerSpawner.Commands.CreatePlayer, CreatePlayerRequest>>();

        internal static void Update()
        {
            ProcessRequests(commandRequestOps);
        }

        public static void ProcessRequests(Queue<CommandRequestOp<PlayerSpawner.Commands.CreatePlayer, CreatePlayerRequest>> commandRequestOps)
        {
            while (commandRequestOps.Count > 0)
            {
                var op = commandRequestOps.Dequeue();

                if (op.CallerAttributeSet.Contains("UnityClient"))
                {
                    var data = op.Request.serializedArguments.BackingArray;

                    var token = Encoding.UTF8.GetString(data);

                    if (FirebaseJWT.Verify(token, out var tokenOutput))
                    {
                        ShipsBasicSystem.AddOp(tokenOutput, op.CallerWorkerId);
                    }
                    else SpatialOSConnectionSystem.logMessages.Enqueue(new LogMessage("Cannot verify id token " + tokenOutput, LoggerName, LogLevel.Warn));
                }
                else SpatialOSConnectionSystem.logMessages.Enqueue(new LogMessage("Command not issued by UnityClient, cannot proceed", LoggerName, LogLevel.Warn));

                SpatialOSConnectionSystem.createPlayerRequests.Add(op.RequestId, new CreatePlayerResponse());
            }
        }

        internal static void OnCreatePlayerRequest(CommandRequestOp<PlayerSpawner.Commands.CreatePlayer, CreatePlayerRequest> op)
        {
            commandRequestOps.Enqueue(op);
        }
    }
}
