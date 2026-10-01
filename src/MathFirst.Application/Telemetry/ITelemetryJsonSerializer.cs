namespace MathFirst.Application.Telemetry;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;

public interface ITelemetryJsonSerializer
{
    Task SerializeAsync(
        Stream output,
        string installationId,
        string appVersion,
        string buildClassification,
        DateTimeOffset exportedAt,
        IReadOnlyList<AttemptRecord> attempts,
        CancellationToken cancellationToken = default);
}
