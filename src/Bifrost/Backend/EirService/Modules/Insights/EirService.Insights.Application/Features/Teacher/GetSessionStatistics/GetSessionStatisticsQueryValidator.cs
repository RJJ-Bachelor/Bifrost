using FluentValidation;

namespace EirService.Insights.Application.Features.Teacher.GetSessionStatistics;

public sealed class GetSessionStatisticsQueryValidator : AbstractValidator<GetSessionStatisticsQuery>
{
    public GetSessionStatisticsQueryValidator()
    {
        RuleFor(query => query.SessionId).NotEmpty();
        RuleFor(query => query.TeacherId).NotEmpty().MaximumLength(100);
    }
}
