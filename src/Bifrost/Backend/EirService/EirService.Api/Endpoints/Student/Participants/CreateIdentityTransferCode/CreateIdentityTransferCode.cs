using EirService.Api.Authentication;
using EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Participants.CreateIdentityTransferCode;

internal sealed class CreateIdentityTransferCode : EndpointWithoutRequest<CreateIdentityTransferCodeResult>
{
    public override void Configure()
    {
        Post("/students/identity-transfer-codes");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(CreateIdentityTransferCode)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await new CreateIdentityTransferCodeCommand(User.GetRequiredUserId()).ExecuteAsync(cancellationToken);
        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.OkAsync(result, cancellationToken);
    }
}
