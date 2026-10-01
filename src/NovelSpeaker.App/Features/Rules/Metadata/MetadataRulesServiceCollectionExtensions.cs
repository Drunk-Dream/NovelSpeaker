using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public static class MetadataRulesServiceCollectionExtensions
{
    public static IServiceCollection AddMetadataRulesFeature(this IServiceCollection services)
    {
        services.TryAddTransient<FileNameMetadataRulesViewModel>();
        services.TryAddTransient<TextHeaderMetadataRulesViewModel>();
        services.TryAddTransient<FileNameMetadataRulesPage>();
        services.TryAddTransient<TextHeaderMetadataRulesPage>();
        return services;
    }
}
