// author: https://github.com/pabloFuente

using System.Text;
using LiveKit.Rtc;
using Xunit;

namespace LiveKit.Rtc.Tests;

/// <summary>
/// Unit tests that verify <see cref="RoomOptions"/> is correctly mapped into the FFI
/// <c>Proto.RoomOptions</c> that is sent inside the <c>ConnectRequest</c>.
///
/// Regression coverage for https://github.com/pabloFuente/livekit-server-sdk-dotnet/issues/97:
/// <c>RoomOptions.ToProto()</c> used to drop the <c>E2EE</c> options entirely, so the FFI
/// <c>ConnectRequest</c> carried no E2EE configuration. The Rust SDK never attached frame
/// cryptors, and media was silently sent/received in plaintext even though the public API
/// (<c>Room.E2EEManager</c>, <c>KeyProvider</c>) suggested E2EE was active.
///
/// The options are mapped into the newer <c>encryption</c> proto field rather than the
/// deprecated <c>e2ee</c> one: the FFI reads <c>encryption</c> for both media and data-channel
/// encryption, whereas <c>e2ee</c> only enables media encryption as a fallback.
/// </summary>
public class E2EEOptionsMappingTests
{
    private static byte[] MakeKey(int seed)
    {
        var key = new byte[32];
        new Random(seed).NextBytes(key);
        return key;
    }

    [Fact]
    public void RoomOptions_ToProto_WithoutE2EE_LeavesEncryptionUnset()
    {
        var proto = new RoomOptions().ToProto();

        Assert.Null(proto.Encryption);
    }

    [Fact]
    public void RoomOptions_ToProto_MapsE2EEOptionsIntoProto()
    {
        var sharedKey = MakeKey(42);

        var options = new RoomOptions
        {
            E2EE = new E2EEOptions
            {
                EncryptionType = Proto.EncryptionType.Gcm,
                KeyProviderOptions = new KeyProviderOptions { SharedKey = sharedKey },
            },
        };

        var proto = options.ToProto();

        // The core of issue #97: without this mapping the FFI never enables E2EE and media is
        // transmitted as plaintext while the public API pretends encryption is on.
        Assert.NotNull(proto.Encryption);
        Assert.Equal(Proto.EncryptionType.Gcm, proto.Encryption.EncryptionType);
        Assert.NotNull(proto.Encryption.KeyProviderOptions);
        Assert.Equal(sharedKey, proto.Encryption.KeyProviderOptions.SharedKey.ToByteArray());
    }

    [Fact]
    public void RoomOptions_ToProto_UsesNonDeprecatedEncryptionField()
    {
        var options = new RoomOptions
        {
            E2EE = new E2EEOptions
            {
                KeyProviderOptions = new KeyProviderOptions { SharedKey = MakeKey(1) },
            },
        };

        var proto = options.ToProto();

        // The FFI only enables data-channel encryption when the (non-deprecated) `encryption`
        // field is set; the deprecated `e2ee` field must be left unset so we don't regress to a
        // configuration that only encrypts media.
        Assert.NotNull(proto.Encryption);
#pragma warning disable CS0612 // asserting the deprecated field is intentionally NOT populated
        Assert.Null(proto.E2Ee);
#pragma warning restore CS0612
    }

    [Fact]
    public void RoomOptions_ToProto_UsesNonZeroKeyRingSizeByDefault()
    {
        var options = new RoomOptions
        {
            E2EE = new E2EEOptions
            {
                KeyProviderOptions = new KeyProviderOptions { SharedKey = MakeKey(2) },
            },
        };

        var proto = options.ToProto();

        Assert.NotNull(proto.Encryption);
        // A KeyRingSize of 0 creates an empty key ring in the FFI and no frames are delivered at
        // all, so mapping E2EE without a sane key ring size would just trade one silent failure
        // for another.
        Assert.True(
            proto.Encryption.KeyProviderOptions.KeyRingSize > 0,
            "KeyRingSize must be non-zero, otherwise the FFI delivers no frames"
        );
    }

    [Fact]
    public void RoomOptions_ToProto_MapsKeyProviderDetails()
    {
        var salt = Encoding.UTF8.GetBytes("custom-salt-value");

        var options = new RoomOptions
        {
            E2EE = new E2EEOptions
            {
                KeyProviderOptions = new KeyProviderOptions
                {
                    SharedKey = MakeKey(7),
                    RatchetSalt = salt,
                    RatchetWindowSize = 8,
                    FailureTolerance = 3,
                    KeyRingSize = 24,
                },
            },
        };

        var proto = options.ToProto();

        Assert.NotNull(proto.Encryption);
        var keyProvider = proto.Encryption.KeyProviderOptions;
        Assert.Equal(salt, keyProvider.RatchetSalt.ToByteArray());
        Assert.Equal(8, keyProvider.RatchetWindowSize);
        Assert.Equal(3, keyProvider.FailureTolerance);
        Assert.Equal(24, keyProvider.KeyRingSize);
    }
}
