using EirService.Api.Authentication;
using FastEndpoints;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Teacher.Participants;

internal sealed class GetTeacherIdentity : EndpointWithoutRequest<TeacherIdentityResponse>
{
    public override void Configure()
    {
        Get("/teachers/me");
        Policies(EndpointTags.Teacher);
        Options(builder => builder.WithName(nameof(GetTeacherIdentity)).WithTags(EndpointTags.Teachers));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var response = new TeacherIdentityResponse(
            User.GetRequiredUserId(),
            User.Identity?.Name,
            User.FindAll("role").Select(claim => claim.Value).ToArray());
        await Send.OkAsync(response, cancellationToken);
    }
}
