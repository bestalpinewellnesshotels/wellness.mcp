using System.Diagnostics;
using HotelChatbot.Application.DTOs;

namespace HotelChatbot.Application.Diagnostics;

/// <summary>
/// Sammelt Pipeline-Schritte und meldet sie optional live (SSE).
/// </summary>
public sealed class PipelineTraceCollector
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly Func<PipelineTraceStepDto, Task>? _onStepAsync;
    private readonly object _gate = new();

    public PipelineTraceCollector(Func<PipelineTraceStepDto, Task>? onStepAsync = null)
    {
        _onStepAsync = onStepAsync;
    }

    public List<PipelineTraceStepDto> Steps { get; } = new();

    public long ElapsedMs => _sw.ElapsedMilliseconds;

    public async Task EmitStartAsync(
        string agent,
        string kind,
        string detail,
        Dictionary<string, object?>? meta = null)
    {
        await EmitAsync(new PipelineTraceStepDto
        {
            Agent = agent,
            Kind = kind,
            Phase = "start",
            Detail = detail,
            ElapsedMs = ElapsedMs,
            Status = "running",
            Meta = meta
        });
    }

    public async Task EmitEndAsync(
        string agent,
        string kind,
        string detail,
        long durationMs,
        string status,
        Dictionary<string, object?>? meta = null)
    {
        await EmitAsync(new PipelineTraceStepDto
        {
            Agent = agent,
            Kind = kind,
            Phase = "end",
            Detail = detail,
            ElapsedMs = ElapsedMs,
            DurationMs = durationMs,
            Status = status,
            Meta = meta
        });
    }

    public async Task<T> MeasureAsync<T>(
        string agent,
        string kind,
        string detailStart,
        Func<Task<(T Result, string Status, string DetailEnd, Dictionary<string, object?>? Meta)>> work)
    {
        await EmitStartAsync(agent, kind, detailStart);
        var t0 = _sw.ElapsedMilliseconds;
        try
        {
            var (result, status, detailEnd, meta) = await work();
            await EmitEndAsync(agent, kind, detailEnd, _sw.ElapsedMilliseconds - t0, status, meta);
            return result;
        }
        catch (Exception ex)
        {
            await EmitEndAsync(agent, kind, ex.Message, _sw.ElapsedMilliseconds - t0, "error");
            throw;
        }
    }

    private async Task EmitAsync(PipelineTraceStepDto step)
    {
        lock (_gate)
        {
            Steps.Add(step);
        }

        if (_onStepAsync != null)
            await _onStepAsync(step);
    }
}
