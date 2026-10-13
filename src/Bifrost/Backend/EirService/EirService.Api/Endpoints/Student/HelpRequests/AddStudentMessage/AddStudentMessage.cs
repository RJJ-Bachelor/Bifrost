using EirService.Api.Authentication;
using EirService.HelpRequests.Application.Features.Common;
using EirService.HelpRequests.Application.Features.Student.AddStudentMessage;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.HelpRequests.AddStudentMessage;

internal sealed class AddStudentMessage : Endpoint<AddStudentMessageRequest, HelpRequestResult>
{
    public override void Configure()
    {
        Post("/students/requests/{id:guid}/messages");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(AddStudentMessage)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(AddStudentMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await new AddStudentMessageCommand(Route<Guid>("id"), User.GetRequiredUserId(), request.Text).ExecuteAsync(cancellationToken);
        await Send.OkAsync(result, cancellationToken);
    }
}
