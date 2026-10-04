using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// Where to put the copy of an application, and whether its records are copied too.
    /// </summary>
    public class CloneApplicationRequest
    {
        /// <summary>
        /// The API server that will host the clone (GET /api/v1/servers). It may be the server of
        /// the application that is cloned.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public long? ServerID { get; set; }

        /// <summary>
        /// Where the clone's data will live: SQLLite, SQLServer, MySQL or PostgreSQL. It may differ
        /// from the database type of the application that is cloned.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string DatabaseType { get; set; } = string.Empty;

        /// <summary>
        /// The connection string of an empty, existing database. Required unless DatabaseType is
        /// SQLLite, which ignores it.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// True copies the records too; false copies the schema only and leaves the clone empty.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public bool? CloneData { get; set; }

        /// <summary>
        /// The names of the entities whose records are copied when CloneData is true. Left out,
        /// null or empty copies the records of every entity. Files is never copied, listed or not.
        /// Ignored when CloneData is false. Every entity is created in the clone either way.
        /// </summary>
        public List<string>? Entities { get; set; }
    }

    /// <summary>
    /// A clone that was started. The work goes on after this answer.
    /// </summary>
    public class CloneStartedResponse
    {
        /// <summary>
        /// Identifies the operation: GET /api/v1/applications/{appToken}/clones/{operationId} reports its progress.
        /// </summary>
        [Required]
        public string OperationId { get; set; } = string.Empty;

        /// <summary>
        /// The token of the new application. It is listed for the caller at once, and it works on
        /// its API server only when the operation has completed.
        /// </summary>
        [Required]
        public string ClonedApplicationToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// How far a clone has come. The counters of a phase are 0 until that phase starts.
    /// </summary>
    public class CloneOperationResponse
    {
        [Required]
        public string OperationId { get; set; } = string.Empty;

        /// <summary>
        /// The phase: Pending, CreatingApplication, CreatingEntities, CloningData (only when records
        /// are copied), then Completed or Failed. The last two are final.
        /// </summary>
        [Required]
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Why the operation failed. Null unless Status is Failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 0 to 100. Creating the entities fills the whole range; when records are copied afterwards
        /// it starts again at 50.
        /// </summary>
        [Required]
        public int OverallPercentage { get; set; }

        /// <summary>
        /// How many entities the clone gets, system entities not counted.
        /// </summary>
        [Required]
        public int TotalEntitiesToCreate { get; set; }

        [Required]
        public int EntitiesCreated { get; set; }

        /// <summary>
        /// The entity being created right now, or null.
        /// </summary>
        public string? CurrentEntityCreatingName { get; set; }

        /// <summary>
        /// How many entities have their records copied.
        /// </summary>
        [Required]
        public int TotalEntitiesToCloneData { get; set; }

        [Required]
        public int EntitiesDataCloned { get; set; }

        /// <summary>
        /// The entity whose records are being copied right now, or null.
        /// </summary>
        public string? CurrentEntityCloningDataName { get; set; }

        [Required]
        public int CurrentEntityTotalRecords { get; set; }

        [Required]
        public int CurrentEntityImportedRecords { get; set; }

        /// <summary>
        /// How many records are to be copied, over all entities.
        /// </summary>
        [Required]
        public int TotalRecordsAllEntities { get; set; }

        [Required]
        public int TotalRecordsImported { get; set; }

        /// <summary>
        /// Null while it cannot be estimated (no record copied yet); 0 once the operation has
        /// completed or failed.
        /// </summary>
        public double? EstimatedRemainingSeconds { get; set; }

        [Required]
        public DateTime StartedAtUtc { get; set; }

        /// <summary>
        /// Null until Status is Completed. A failed operation has none.
        /// </summary>
        public DateTime? CompletedAtUtc { get; set; }

        /// <summary>
        /// The token of the new application. Open it once Status is Completed.
        /// </summary>
        [Required]
        public string ClonedApplicationToken { get; set; } = string.Empty;
    }
}
