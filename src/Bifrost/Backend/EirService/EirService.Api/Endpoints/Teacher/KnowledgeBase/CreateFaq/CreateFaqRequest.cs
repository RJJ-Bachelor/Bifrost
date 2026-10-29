namespace EirService.Api.Endpoints.Teacher.KnowledgeBase.CreateFaq;

public sealed class CreateFaqRequest
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}
