using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Seren.Application.Chat.Attachments;
using Seren.Application.Modules;
using Shouldly;
using Xunit;

namespace Seren.Modules.ChatAttachments.Tests;

/// <summary>
/// Unit tests for the <see cref="ChatAttachmentsModule"/> contract: the
/// validator + extractor pipeline must resolve to working singletons, and
/// the registry must expose every registered <see cref="IAttachmentTextExtractor"/>.
/// </summary>
public sealed class ChatAttachmentsModuleTests
{
    [Fact]
    public void Configure_RegistersValidatorAndExtractors()
    {
        var services = BuildServices();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAttachmentValidator>().ShouldBeOfType<AttachmentValidator>();
        provider.GetRequiredService<IAttachmentTextExtractorRegistry>()
            .ShouldBeOfType<AttachmentTextExtractorRegistry>();
    }

    [Fact]
    public void Configure_RegistersBothExtractorImplementations()
    {
        var services = BuildServices();
        var provider = services.BuildServiceProvider();

        var extractors = provider.GetServices<IAttachmentTextExtractor>().ToList();
        extractors.Count.ShouldBe(2);
        extractors.ShouldContain(e => e is PdfTextExtractor);
        extractors.ShouldContain(e => e is PlainTextExtractor);
    }

    [Fact]
    public void Module_Identity_IsStable()
    {
        var module = new ChatAttachmentsModule();
        module.Id.ShouldBe("chat-attachments");
        module.Version.ShouldNotBeNullOrWhiteSpace();
    }

    private static ServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSerenModules(configuration, typeof(ChatAttachmentsModule));

        return services;
    }
}
