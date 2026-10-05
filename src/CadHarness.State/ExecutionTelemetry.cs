using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace CadHarness.State;

public enum ExecutionPhase { Runtime, Validation, Rebuild, Recovery, Rollback, Observation, Llm }
public sealed record ExecutionTiming(double RuntimeWallMs, double ValidationWallMs, double RebuildWallMs,
    double RecoveryWallMs, double RollbackWallMs, double ObservationWallMs, double LlmWallMs, int Rebuilds, int Recoveries, int Rollbacks);

// Optional instrumentation. Nested spans are exclusive: a rebuild inside
// validation is counted once, in Rebuild. No CAD behavior depends on metrics.
public sealed class ExecutionTelemetry : IDisposable
{
    private static readonly AsyncLocal<ExecutionTelemetry?> active = new();
    private readonly ExecutionTelemetry? previous;
    private readonly Dictionary<ExecutionPhase, double> times = new();
    private Span? top;
    private int rebuilds;
    private int recoveries;
    private int rollbacks;
    private ExecutionTelemetry() { previous = active.Value; active.Value = this; }
    public static ExecutionTelemetry Start() => new();
    public static IDisposable Measure(ExecutionPhase phase) => active.Value is { } collector ? new Span(collector, phase) : NoSpan.Instance;
    public static bool Rebuild(Func<bool> rebuild)
    {
        if (active.Value is { } collector) collector.rebuilds++;
        using var span = Measure(ExecutionPhase.Rebuild); return rebuild();
    }
    public static bool Recover(Func<bool> recover)
    {
        if (active.Value is { } collector) collector.recoveries++;
        using var span = Measure(ExecutionPhase.Recovery); return recover();
    }
    public static void Rollback(Action rollback)
    {
        if (active.Value is { } collector) collector.rollbacks++;
        using var span = Measure(ExecutionPhase.Rollback); rollback();
    }
    public ExecutionTiming Snapshot()
    {
        double Read(ExecutionPhase phase) => times.GetValueOrDefault(phase);
        return new(Read(ExecutionPhase.Runtime), Read(ExecutionPhase.Validation), Read(ExecutionPhase.Rebuild),
            Read(ExecutionPhase.Recovery), Read(ExecutionPhase.Rollback), Read(ExecutionPhase.Observation), Read(ExecutionPhase.Llm), rebuilds, recoveries, rollbacks);
    }
    public void Dispose()
    {
        if (top is not null) throw new InvalidOperationException("Telemetry span is still active.");
        active.Value = previous;
    }
    private sealed class Span : IDisposable
    {
        private readonly ExecutionTelemetry collector;
        private readonly ExecutionPhase phase;
        private readonly Span? parent;
        private readonly long start = Stopwatch.GetTimestamp();
        private double children;
        private bool disposed;
        internal Span(ExecutionTelemetry collector, ExecutionPhase phase)
        { this.collector = collector; this.phase = phase; parent = collector.top; collector.top = this; }
        public void Dispose()
        {
            if (disposed) return;
            if (collector.top != this) throw new InvalidOperationException("Telemetry spans must close in order.");
            disposed = true;
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            collector.times[phase] = collector.times.GetValueOrDefault(phase) + Math.Max(0, elapsed - children);
            if (parent is not null) parent.children += elapsed;
            collector.top = parent;
        }
    }
    private sealed class NoSpan : IDisposable
    { internal static readonly NoSpan Instance = new(); public void Dispose() { } }
}
