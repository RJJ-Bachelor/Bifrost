using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Participants;

internal sealed class GetStudentIdentity : EndpointWithoutRequest<StudentIdentityResponse>
{
    public override void Configure()
    {
        Get("/students/me");
        Policies(EndpointTags.Student);
        Options(builder => builder.WithName(nameof(GetStudentIdentity)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = new StudentIdentityResponse(
            User.FindFirst("sub")!.Value,
            User.FindAll("role").Select(claim => claim.Value).ToArray());
        await Send.OkAsync(response, cancellationToken);
    }
}
