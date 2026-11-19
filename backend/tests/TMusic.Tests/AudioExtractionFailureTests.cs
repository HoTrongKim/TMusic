using TMusic.Infrastructure.Downloads;
using Xunit;

namespace TMusic.Tests;

public sealed class AudioExtractionFailureTests
{
    [Fact]
    public void MissingPublicSoundtrackStopsRetriesWithoutExposingEngineDetails()
    {
        var error = YtDlpAudioExtractor.ClassifyFailure("tiktok",
            "ERROR: TMUSIC_SOURCE_AUDIO_UNAVAILABLE: https://example.invalid/media?token=fixture-only");
        Assert.Equal("SOURCE_AUDIO_UNAVAILABLE", error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(error.Code, error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("tiktok", "ERROR: TMUSIC_SOURCE_ACCESS_CHECK_REQUIRED: source requires an access check")]
    [InlineData("youtube", "ERROR: TMUSIC_SOURCE_ACCESS_CHECK_REQUIRED")]
    [InlineData("tiktok", "ERROR: TMUSIC_SOURCE_ACCESS_CHECK_REQUIRED; Sign in to continue")]
    [InlineData("youtube", "ERROR: [youtube] rYWLIJB214Q: Sign in to confirm you're not a bot. Use --cookies for authentication.")]
    [InlineData("youtube", "ERROR: [youtube] rYWLIJB214Q: Sign in to confirm you\u2019re not a bot. Use --cookies for authentication.")]
    public void ExplicitAccessCheckMarkerStopsRetriesWithItsOwnCode(string provider, string standardError)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure(provider, standardError);

        Assert.Equal("SOURCE_ACCESS_CHECK_REQUIRED", error.Code);
        Assert.False(error.Retryable);
    }

    [Theory]
    [InlineData("ERROR: [TikTok] Unexpected response from webpage request")]
    [InlineData("ERROR: [TikTok] unexpected RESPONSE from webpage REQUEST")]
    public void UnrecognizedTikTokPageStopsRetriesWithoutClaimingAnAccessRestriction(string standardError)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure("tiktok", standardError);

        Assert.Equal("SOURCE_RESPONSE_UNSUPPORTED", error.Code);
        Assert.False(error.Retryable);
    }

    [Theory]
    [InlineData("youtube")]
    [InlineData("soundcloud")]
    public void TikTokPageFailureClassificationDoesNotApplyToOtherProviders(string provider)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure(provider, "Unexpected response from webpage request");

        Assert.Equal("AUDIO_EXTRACTION_FAILED", error.Code);
        Assert.True(error.Retryable);
    }

    [Theory]
    [InlineData("This video is DRM protected")]
    [InlineData("ERROR: Private video")]
    [InlineData("ERROR: This video is members-only")]
    [InlineData("ERROR: Video unavailable")]
    [InlineData("ERROR: File is larger than max-filesize")]
    [InlineData("ERROR: PRIVATE VIDEO; Unexpected response from webpage request")]
    public void ExistingPermanentRestrictionsRemainNonretryable(string standardError)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure("tiktok", standardError);

        Assert.Equal("SOURCE_RESTRICTED_OR_UNSUPPORTED", error.Code);
        Assert.False(error.Retryable);
    }

    [Theory]
    [InlineData("youtube", "ERROR: HTTP Error 401: Unauthorized", "SOURCE_AUTHENTICATION_REQUIRED", false)]
    [InlineData("youtube", "ERROR: Sign in to confirm your account", "SOURCE_AUTHENTICATION_REQUIRED", false)]
    [InlineData("youtube", "ERROR: HTTP Error 403: Forbidden", "SOURCE_ACCESS_FORBIDDEN", false)]
    [InlineData("youtube", "ERROR: Sign in to confirm you're not a bot", "SOURCE_ACCESS_CHECK_REQUIRED", false)]
    [InlineData("youtube", "ERROR: Temporary failure in name resolution", "SOURCE_NETWORK_ERROR", true)]
    [InlineData("youtube", "ERROR: HTTP Error 502: Bad Gateway", "SOURCE_NETWORK_ERROR", true)]
    [InlineData("youtube", "ERROR: The read operation timed out", "SOURCE_NETWORK_ERROR", true)]
    public void AuthenticationAccessAndNetworkFailuresHaveSeparateRetryPolicies(
        string provider, string standardError, string expectedCode, bool retryable)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure(provider, standardError);

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(retryable, error.Retryable);
        Assert.Equal(retryable, YtDlpAudioExtractor.IsRetryableErrorCode(error.Code));
    }

    [Theory]
    [InlineData("youtube", "ERROR: HTTP Error 429: Too Many Requests", "RATE_LIMITED", true)]
    [InlineData("youtube", "ERROR: [youtube] Requested format is not available", "SOURCE_AUDIO_UNAVAILABLE", false)]
    [InlineData("youtube", "ERROR: [youtube] Unable to extract uploader id", "SOURCE_RESPONSE_UNSUPPORTED", false)]
    [InlineData("youtube", "TMUSIC_YTDLP_COMPATIBILITY_ERROR", "AUDIO_ENGINE_UNAVAILABLE", false)]
    public void ExtendedProviderFailuresClassifyAccurately(string provider, string standardError, string expectedCode, bool expectedRetryable)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure(provider, standardError);

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(expectedRetryable, error.Retryable);
        Assert.Equal(expectedRetryable, YtDlpAudioExtractor.IsRetryableErrorCode(error.Code));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(128, true)]
    [InlineData(192, true)]
    [InlineData(320, true)]
    [InlineData(64, false)]
    [InlineData(256, false)]
    [InlineData(0, false)]
    public void AudioBitrateValidationAcceptsOnlySupportedOptions(int? kbps, bool expected)
        => Assert.Equal(expected, AudioBitrate.IsValid(kbps));

    [Theory]
    [InlineData("")]
    [InlineData("ERROR: extractor failed unexpectedly")]
    public void UnclassifiedFailuresRetainBoundedRetryBehavior(string standardError)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure("tiktok", standardError);

        Assert.Equal("AUDIO_EXTRACTION_FAILED", error.Code);
        Assert.True(error.Retryable);
    }

    [Theory]
    [InlineData("ERROR: The read operation timed out")]
    [InlineData("ERROR: HTTP Error 503: Service Unavailable")]
    [InlineData("ERROR: Connection reset by peer")]
    public void NetworkFailuresUseTheNetworkCode(string standardError)
    {
        var error = YtDlpAudioExtractor.ClassifyFailure("tiktok", standardError);

        Assert.Equal("SOURCE_NETWORK_ERROR", error.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public void UnknownErrorCodesAreNotAutomaticallyRetried()
    {
        Assert.False(YtDlpAudioExtractor.IsRetryableErrorCode("UNRECOGNIZED_PERMANENT_ERROR"));
        Assert.False(YtDlpAudioExtractor.IsRetryableErrorCode("AUDIO_STORAGE_REJECTED"));
        Assert.True(YtDlpAudioExtractor.IsRetryableErrorCode("AUDIO_STORAGE_UNAVAILABLE"));
    }

    [Theory]
    [InlineData("TMUSIC_SOURCE_ACCESS_CHECK_REQUIRED")]
    [InlineData("SOURCE_AUTHENTICATION_REQUIRED")]
    [InlineData("SOURCE_ACCESS_FORBIDDEN")]
    [InlineData("Unexpected response from webpage request")]
    [InlineData("Private video")]
    [InlineData("The read operation timed out")]
    public void FailureDoesNotExposeEngineOutputOrSourceUrl(string reason)
    {
        const string privateSource = "https://example.invalid/media?token=fixture-only";
        var error = YtDlpAudioExtractor.ClassifyFailure("tiktok", $"ERROR: {reason}: {privateSource}");

        Assert.Equal(error.Code, error.Message);
        Assert.DoesNotContain(privateSource, error.ToString());
        Assert.Null(error.InnerException);
    }
}
