using MTranslate.Infrastructure;
using Xunit;

namespace MTranslate.Infrastructure.Tests;

public sealed class LlamaServerRuntimeTests
{
    [Fact]
    public async Task Constructor_WithoutApiKey_Generates256BitKey()
    {
        var configuration = new InferenceRuntimeConfiguration("server", "model");

        await using var runtime = new LlamaServerRuntime(configuration);

        Assert.Equal(64, runtime.ApiKey.Length);
        Assert.All(runtime.ApiKey, character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public async Task Constructor_WithApiKey_PreservesConfiguredKey()
    {
        var configuration = new InferenceRuntimeConfiguration("server", "model", ApiKey: "integration-test-key");

        await using var runtime = new LlamaServerRuntime(configuration);

        Assert.Equal("integration-test-key", runtime.ApiKey);
    }

    [Fact]
    public void Configuration_RejectsNegativeGpuLayerCount()
    {
        var executablePath = Path.GetTempFileName();
        var modelPath = Path.GetTempFileName();
        try
        {
            var configuration = new InferenceRuntimeConfiguration(executablePath, modelPath, GpuLayers: -1);

            var exception = Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);

            Assert.Equal("GpuLayers", exception.ParamName);
        }
        finally
        {
            File.Delete(executablePath);
            File.Delete(modelPath);
        }
    }
}
