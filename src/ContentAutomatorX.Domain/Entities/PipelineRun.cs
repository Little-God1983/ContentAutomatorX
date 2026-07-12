namespace ContentAutomatorX.Domain.Entities;

public enum RunStatus { Running, Succeeded, Failed, Partial }

public class PipelineRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Kind { get; set; }          // RunKinds.*
    public required string Trigger { get; set; }       // RunTriggers.*
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public string LogJson { get; set; } = "[]";        // array of step messages
}
