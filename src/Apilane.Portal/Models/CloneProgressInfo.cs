using System;

namespace Apilane.Portal.Models
{
    public class CloneProgressInfo
    {
        public string OperationId { get; set; } = null!;
        public CloneStatus Status { get; set; } = CloneStatus.Pending;
        public string? ErrorMessage { get; set; }

        // Schema creation progress
        public int TotalEntitiesToCreate { get; set; }
        public int EntitiesCreated { get; set; }
        public string? CurrentEntityCreatingName { get; set; }

        // Data cloning progress
        public int TotalEntitiesToCloneData { get; set; }
        public int EntitiesDataCloned { get; set; }
        public string? CurrentEntityCloningDataName { get; set; }
        public int CurrentEntityTotalRecords { get; set; }
        public int CurrentEntityImportedRecords { get; set; }

        // Timing for ETA calculation
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public int TotalRecordsAllEntities { get; set; }
        public int TotalRecordsImported { get; set; }

        // Result
        public string? ClonedApplicationToken { get; set; }

        public int OverallPercentage
        {
            get
            {
                if (Status == CloneStatus.Completed)
                {
                    return 100;
                }

                // During schema creation phase, use entity creation count
                if (Status == CloneStatus.CreatingApplication || Status == CloneStatus.CreatingEntities)
                {
                    if (TotalEntitiesToCreate == 0)
                    {
                        return 0;
                    }

                    // Schema creation is the first half of progress (0-50%) when data cloning follows,
                    // or the full progress (0-100%) when no data cloning is needed.
                    var schemaPercent = (int)((double)EntitiesCreated / TotalEntitiesToCreate * 100);
                    return TotalEntitiesToCloneData > 0
                        ? schemaPercent / 2
                        : schemaPercent;
                }

                // During data cloning phase
                if (Status == CloneStatus.CloningData)
                {
                    if (TotalRecordsAllEntities == 0)
                    {
                        // No records to clone, but we're in cloning phase
                        return TotalEntitiesToCloneData > 0
                            ? 50 + (int)((double)EntitiesDataCloned / TotalEntitiesToCloneData * 50)
                            : 100;
                    }

                    return 50 + (int)((double)TotalRecordsImported / TotalRecordsAllEntities * 50);
                }

                return 0;
            }
        }

        public double? EstimatedRemainingSeconds
        {
            get
            {
                if (Status == CloneStatus.Completed || Status == CloneStatus.Failed)
                {
                    return 0;
                }

                if (TotalRecordsImported == 0 || TotalRecordsAllEntities == 0)
                {
                    return null;
                }

                var elapsed = (DateTime.UtcNow - StartedAtUtc).TotalSeconds;
                var rate = TotalRecordsImported / elapsed;
                var remaining = TotalRecordsAllEntities - TotalRecordsImported;

                return rate > 0 ? remaining / rate : null;
            }
        }
    }

    public enum CloneStatus
    {
        Pending,
        CreatingApplication,
        CreatingEntities,
        CloningData,
        Completed,
        Failed
    }
}
