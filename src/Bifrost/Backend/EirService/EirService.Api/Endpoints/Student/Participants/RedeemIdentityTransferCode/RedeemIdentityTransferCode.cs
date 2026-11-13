using EirService.Participants.Application.Features.Student.RedeemIdentityTransferCode;
using FastEndpoints;
using Shared.Api.Authentication;
using EndpointTags = EirService.Api.Endpoints.Tags;

namespace EirService.Api.Endpoints.Student.Participants.RedeemIdentityTransferCode;

internal sealed class RedeemIdentityTransferCode(StudentIdentityCookie studentCookie)
    : Endpoint<RedeemIdentityTransferCodeRequest>
{
    public override void Configure()
    {
        Post("/students/identity-transfer-codes/redeem");
        AllowAnonymous();
        Options(builder => builder.WithName(nameof(RedeemIdentityTransferCode)).WithTags(EndpointTags.Students));
    }

    public override async Task HandleAsync(RedeemIdentityTransferCodeRequest request, CancellationToken cancellationToken)
    {
        if (!HttpContext.Request.HasJsonContentType())
        {
            await Send.ResultAsync(Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
            return;
        }

        var studentId = await new RedeemIdentityTransferCodeCommand(request.Code).ExecuteAsync(cancellationToken);
        studentCookie.AppendToResponse(HttpContext, studentId);
        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.NoContentAsync(cancellationToken);
    }
}
