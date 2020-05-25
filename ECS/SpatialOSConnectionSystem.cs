using Improbable.Worker;
using RogueFleet.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SpawnerWorker.ECS
{
    public readonly struct LogMessage
    {
        public readonly string message;
        public readonly string logger;
        public readonly LogLevel logLevel;

        public LogMessage(string message, string logger, LogLevel logLevel)
        {
            this.message = message;
            this.logger = logger;
            this.logLevel = logLevel;
        }
    }

    public static class SpatialOSConnectionSystem
    {
        public static Connection connection;

        public static readonly Dictionary<RequestId<IncomingCommandRequest<PlayerSpawner.Commands.CreatePlayer>>, CreatePlayerResponse> createPlayerRequests = new Dictionary<RequestId<IncomingCommandRequest<PlayerSpawner.Commands.CreatePlayer>>, CreatePlayerResponse>();

        public static readonly ConcurrentQueue<Entity> entitiesToCreate = new ConcurrentQueue<Entity>();

        public static readonly Queue<LogMessage> logMessages = new Queue<LogMessage>();

        public static void Update()
        {
            SendCommandResponses();

            SendCreateEntityRequests();
            
            SendLogMessages();
        }

        static void SendLogMessages()
        {
            while (logMessages.Count > 0)
            {
                var op = logMessages.Dequeue();

                connection.SendLogMessage(op.logLevel, op.logger, op.message);
            }
        }

        static void SendCreateEntityRequests()
        {
            while (entitiesToCreate.TryDequeue(out var entity))
            {
                connection.SendCreateEntityRequest(entity, null, null);
            }
        }

        static void SendCommandResponses()
        {
            foreach (var request in createPlayerRequests)
            {
                connection.SendCommandResponse(PlayerSpawner.Commands.CreatePlayer.Metaclass, request.Key, request.Value);
            }

            createPlayerRequests.Clear();
        }
    }
}
