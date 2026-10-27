namespace EirService.Api.Endpoints.Teacher.Sessions.ArchiveSession;

public sealed class ArchiveSessionRequest
{
    public int ExpectedGeneration { get; set; }
    public string? Title { get; set; }
}
