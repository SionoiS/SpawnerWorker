using WorldGenerator;
using Improbable;
using Improbable.Worker;
using RogueFleet.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpawnerWorker.ECS
{
    public static class AsteroidProcGenSystem
    {
        static readonly Queue<CommandRequestOp<AsteroidSpawner.Commands.PopulateGridCell, PopulateGridCellRequest>> commandRequestOps = new Queue<CommandRequestOp<AsteroidSpawner.Commands.PopulateGridCell, PopulateGridCellRequest>>();

        static readonly HashSet<GridCoords> populatedCells = new HashSet<GridCoords>();

        public static void Update()
        {
            ProcessCommandOps();
        }

        static void ProcessCommandOps()//One request per frame to prevent overload
        {
            if (commandRequestOps.Count < 1)
            {
                return;
            }

            var request = commandRequestOps.Dequeue().Request;

            var offsets = AsteroidGeneration.GridOffsets(new GridCoords(request.cellX, request.cellZ));

            var cells = new List<GridCoords>();
            for (int j = 0; j < offsets.Length; j++)
            {
                var offset = offsets[j];

                if (populatedCells.Add(offset))//check if cells are already populated
                {
                    cells.Add(offset);
                }
            }

            if (cells.Count <= 0)
            {
                return;
            }

            var featurePoints = new Coordinates[cells.Count * 9];
            var asteroidsCoords = new Coordinates[cells.Count * AsteroidGeneration.AsteroidNumber];

            Parallel.For(0, cells.Count, i =>
            {
                AsteroidGeneration.FeaturePoints(cells[i]).CopyTo(featurePoints, i * 9);
                AsteroidGeneration.RandomAsteroidsCoordinates(cells[i]).CopyTo(asteroidsCoords, i * AsteroidGeneration.AsteroidNumber);
            });

            Parallel.For(0, asteroidsCoords.Length, j =>
            {
                var points = new Coordinates[9];

                //use Span<Coordinates> instead to avoid allocation
                Array.Copy(featurePoints, (j / AsteroidGeneration.AsteroidNumber) * 9, points, 0, 9);

                AsteroidGeneration.PushAwayFromFeaturePoints(ref asteroidsCoords[j], points);
            });

            Parallel.For(0, asteroidsCoords.Length, j =>
            {
                SpatialOSConnectionSystem.entitiesToCreate.Enqueue(EntityTemplates.DefaultAsteroid(asteroidsCoords[j]));
            });
        }

        public static void OnPopulateGridCell(CommandRequestOp<AsteroidSpawner.Commands.PopulateGridCell, PopulateGridCellRequest> op)
        {
            commandRequestOps.Enqueue(op);
        }
    }
}
