// author: https://github.com/pabloFuente

using System;
using System.Collections.Generic;
using System.Text;
using LiveKit.Proto;
using LiveKit.Rtc.Internal;

namespace LiveKit.Rtc
{
    /// <summary>
    /// Default constants for E2EE key ratcheting.
    /// </summary>
    public static class E2EEDefaults
    {
        /// <summary>
        /// Default ratchet salt.
        /// </summary>
        public static readonly byte[] RatchetSalt = Encoding.UTF8.GetBytes("LKFrameEncryptionKey");

        /// <summary>
        /// Default ratchet window size.
        /// </summary>
        public const int RatchetWindowSize = 16;

        /// <summary>
        /// Default failure tolerance (-1 means unlimited).
        /// </summary>
        public const int FailureTolerance = -1;

        /// <summary>
        /// Default key ring size. Must be greater than zero: a value of 0 makes the FFI
        /// create an empty key ring, and no frames are ever delivered.
        /// </summary>
        public const int KeyRingSize = 16;
    }

    /// <summary>
    /// Options for the key provider.
    /// </summary>
    public class KeyProviderOptions
    {
        /// <summary>
        /// Key shared by every participant. When set, it encrypts and decrypts the media of all
        /// participants; when null, set a key per participant with <see cref="KeyProvider.SetKey"/>.
        /// </summary>
        public byte[]? SharedKey { get; set; }

        /// <summary>
        /// Salt used to ratchet keys. Defaults to <see cref="E2EEDefaults.RatchetSalt"/>, the value of the other
        /// LiveKit SDKs.
        /// </summary>
        public byte[] RatchetSalt { get; set; } = E2EEDefaults.RatchetSalt;

        /// <summary>
        /// Number of ratchet steps tried when a frame does not decrypt with the current key. Defaults to
        /// <see cref="E2EEDefaults.RatchetWindowSize"/>.
        /// </summary>
        public int RatchetWindowSize { get; set; } = E2EEDefaults.RatchetWindowSize;

        /// <summary>
        /// Number of consecutive decryption failures tolerated before the key is considered invalid; -1
        /// tolerates any number. Defaults to <see cref="E2EEDefaults.FailureTolerance"/>.
        /// </summary>
        public int FailureTolerance { get; set; } = E2EEDefaults.FailureTolerance;

        /// <summary>
        /// Number of keys kept per participant, addressed by key index. Must be greater than zero. Defaults
        /// to <see cref="E2EEDefaults.KeyRingSize"/>.
        /// </summary>
        public int KeyRingSize { get; set; } = E2EEDefaults.KeyRingSize;

        /// <summary>
        /// Key derivation function used to derive the frame keys. Defaults to PBKDF2, the default
        /// of every LiveKit SDK, so it only needs changing to interoperate with a client configured
        /// otherwise.
        /// </summary>
        public Proto.KeyDerivationFunction KeyDerivationFunction { get; set; } =
            Proto.KeyDerivationFunction.Pbkdf2;
    }

    /// <summary>
    /// Options for end-to-end encryption.
    /// </summary>
    public class E2EEOptions
    {
        /// <summary>
        /// Options of the key provider: shared key, ratcheting and key ring.
        /// </summary>
        public KeyProviderOptions KeyProviderOptions { get; set; } = new KeyProviderOptions();

        /// <summary>
        /// Encryption algorithm of the media. Defaults to <see cref="Proto.EncryptionType.Gcm"/>.
        /// </summary>
        public Proto.EncryptionType EncryptionType { get; set; } = Proto.EncryptionType.Gcm;

        /// <summary>
        /// Converts to proto options.
        /// </summary>
        internal LiveKit.Proto.E2eeOptions ToProto()
        {
            var options = new LiveKit.Proto.E2eeOptions
            {
                EncryptionType = EncryptionType,
                KeyProviderOptions = new LiveKit.Proto.KeyProviderOptions
                {
                    RatchetWindowSize = KeyProviderOptions.RatchetWindowSize,
                    FailureTolerance = KeyProviderOptions.FailureTolerance,
                    RatchetSalt = Google.Protobuf.ByteString.CopyFrom(
                        KeyProviderOptions.RatchetSalt ?? E2EEDefaults.RatchetSalt
                    ),
                    // A key ring size of 0 makes the FFI create an empty key ring, after which
                    // no frames are ever delivered, so it must always be sent as a positive value.
                    KeyRingSize = KeyProviderOptions.KeyRingSize,
                    KeyDerivationFunction = KeyProviderOptions.KeyDerivationFunction,
                },
            };

            if (KeyProviderOptions.SharedKey != null)
            {
                options.KeyProviderOptions.SharedKey = Google.Protobuf.ByteString.CopyFrom(
                    KeyProviderOptions.SharedKey
                );
            }

            return options;
        }
    }

    /// <summary>
    /// Provides key management for E2EE.
    /// </summary>
    public class KeyProvider
    {
        private readonly ulong _roomHandle;
        private readonly KeyProviderOptions _options;

        /// <summary>
        /// Used by the SDK, which creates the key provider with the E2EE manager. Get it from
        /// <see cref="E2EEManager.KeyProvider"/> instead of constructing it.
        /// </summary>
        /// <param name="roomHandle">Native handle of the room, which the public API does not expose.</param>
        /// <param name="options">The key provider options.</param>
        public KeyProvider(ulong roomHandle, KeyProviderOptions options)
        {
            _roomHandle = roomHandle;
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Gets the key provider options.
        /// </summary>
        public KeyProviderOptions Options => _options;

        /// <summary>
        /// Sets the shared encryption key.
        /// </summary>
        /// <param name="key">The encryption key.</param>
        /// <param name="keyIndex">The key index.</param>
        public void SetSharedKey(byte[] key, int keyIndex = 0)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    SetSharedKey = new SetSharedKeyRequest
                    {
                        KeyIndex = keyIndex,
                        SharedKey = Google.Protobuf.ByteString.CopyFrom(key),
                    },
                },
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Exports the shared encryption key.
        /// </summary>
        /// <param name="keyIndex">The key index.</param>
        /// <returns>The exported key.</returns>
        public byte[] ExportSharedKey(int keyIndex = 0)
        {
            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    GetSharedKey = new GetSharedKeyRequest { KeyIndex = keyIndex },
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            return response.E2Ee.GetSharedKey.Key.ToByteArray();
        }

        /// <summary>
        /// Ratchets the shared encryption key.
        /// </summary>
        /// <param name="keyIndex">The key index.</param>
        /// <returns>The new ratcheted key.</returns>
        public byte[] RatchetSharedKey(int keyIndex = 0)
        {
            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    RatchetSharedKey = new RatchetSharedKeyRequest { KeyIndex = keyIndex },
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            return response.E2Ee.RatchetSharedKey.NewKey.ToByteArray();
        }

        /// <summary>
        /// Sets the encryption key for a specific participant.
        /// </summary>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="key">The encryption key.</param>
        /// <param name="keyIndex">The key index.</param>
        public void SetKey(string participantIdentity, byte[] key, int keyIndex = 0)
        {
            if (string.IsNullOrEmpty(participantIdentity))
                throw new ArgumentNullException(nameof(participantIdentity));
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    SetKey = new SetKeyRequest
                    {
                        ParticipantIdentity = participantIdentity,
                        KeyIndex = keyIndex,
                        Key = Google.Protobuf.ByteString.CopyFrom(key),
                    },
                },
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Exports the encryption key for a specific participant.
        /// </summary>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="keyIndex">The key index.</param>
        /// <returns>The exported key.</returns>
        public byte[] ExportKey(string participantIdentity, int keyIndex = 0)
        {
            if (string.IsNullOrEmpty(participantIdentity))
                throw new ArgumentNullException(nameof(participantIdentity));

            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    GetKey = new GetKeyRequest
                    {
                        ParticipantIdentity = participantIdentity,
                        KeyIndex = keyIndex,
                    },
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            return response.E2Ee.GetKey.Key.ToByteArray();
        }

        /// <summary>
        /// Ratchets the encryption key for a specific participant.
        /// </summary>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="keyIndex">The key index.</param>
        /// <returns>The new ratcheted key.</returns>
        public byte[] RatchetKey(string participantIdentity, int keyIndex = 0)
        {
            if (string.IsNullOrEmpty(participantIdentity))
                throw new ArgumentNullException(nameof(participantIdentity));

            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    RatchetKey = new RatchetKeyRequest
                    {
                        ParticipantIdentity = participantIdentity,
                        KeyIndex = keyIndex,
                    },
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            return response.E2Ee.RatchetKey.NewKey.ToByteArray();
        }
    }

    /// <summary>
    /// Frame cryptor for a specific participant.
    /// </summary>
    public class FrameCryptor
    {
        private readonly ulong _roomHandle;
        private readonly string _participantIdentity;
        private readonly string _trackSid;
        private int _keyIndex;
        private bool _enabled;

        /// <summary>
        /// Used by the SDK. Get the cryptors from <see cref="E2EEManager.GetFrameCryptors"/> instead: a cryptor
        /// created without a track SID cannot be addressed by <see cref="SetEnabled"/> and <see cref="SetKeyIndex"/>.
        /// </summary>
        /// <param name="roomHandle">Native handle of the room, which the public API does not expose.</param>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="keyIndex">The key index.</param>
        /// <param name="enabled">Whether encryption is enabled.</param>
        public FrameCryptor(
            ulong roomHandle,
            string participantIdentity,
            int keyIndex,
            bool enabled
        )
            : this(roomHandle, participantIdentity, string.Empty, keyIndex, enabled) { }

        /// <summary>
        /// Used by the SDK. Get the cryptors from <see cref="E2EEManager.GetFrameCryptors"/> instead of
        /// constructing them.
        /// </summary>
        /// <param name="roomHandle">Native handle of the room, which the public API does not expose.</param>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="trackSid">The SID of the track the cryptor encrypts or decrypts.</param>
        /// <param name="keyIndex">The key index.</param>
        /// <param name="enabled">Whether encryption is enabled.</param>
        public FrameCryptor(
            ulong roomHandle,
            string participantIdentity,
            string trackSid,
            int keyIndex,
            bool enabled
        )
        {
            _roomHandle = roomHandle;
            _participantIdentity = participantIdentity;
            _trackSid = trackSid ?? string.Empty;
            _keyIndex = keyIndex;
            _enabled = enabled;
        }

        /// <summary>
        /// Gets the SID of the track this cryptor encrypts or decrypts.
        /// </summary>
        public string TrackSid => _trackSid;

        /// <summary>
        /// Gets the participant identity.
        /// </summary>
        public string ParticipantIdentity => _participantIdentity;

        /// <summary>
        /// Gets the key index.
        /// </summary>
        public int KeyIndex => _keyIndex;

        /// <summary>
        /// Gets whether encryption is enabled.
        /// </summary>
        public bool Enabled => _enabled;

        /// <summary>
        /// Sets whether encryption is enabled.
        /// </summary>
        /// <param name="enabled">True to enable, false to disable.</param>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;

            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    CryptorSetEnabled = new FrameCryptorSetEnabledRequest
                    {
                        ParticipantIdentity = _participantIdentity,
                        TrackSid = _trackSid,
                        Enabled = enabled,
                    },
                },
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Sets the key index.
        /// </summary>
        /// <param name="keyIndex">The key index.</param>
        public void SetKeyIndex(int keyIndex)
        {
            _keyIndex = keyIndex;

            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    CryptorSetKeyIndex = new FrameCryptorSetKeyIndexRequest
                    {
                        ParticipantIdentity = _participantIdentity,
                        TrackSid = _trackSid,
                        KeyIndex = keyIndex,
                    },
                },
            };

            FfiClient.Instance.SendRequest(request);
        }
    }

    /// <summary>
    /// Manager for end-to-end encryption.
    /// </summary>
    public class E2EEManager
    {
        private readonly ulong _roomHandle;
        private readonly E2EEOptions? _options;
        private readonly KeyProvider? _keyProvider;
        private bool _enabled;

        /// <summary>
        /// Used by the SDK, which creates the manager when <see cref="Room.ConnectAsync"/> runs. Get it from
        /// <see cref="Room.E2EEManager"/> instead of constructing it.
        /// </summary>
        /// <param name="roomHandle">Native handle of the room, which the public API does not expose.</param>
        /// <param name="options">The E2EE options, or null when encryption is disabled.</param>
        public E2EEManager(ulong roomHandle, E2EEOptions? options)
        {
            _roomHandle = roomHandle;
            _options = options;
            _enabled = options != null;

            if (options != null)
            {
                _keyProvider = new KeyProvider(roomHandle, options.KeyProviderOptions);
            }
        }

        /// <summary>
        /// Gets the key provider.
        /// </summary>
        public KeyProvider? KeyProvider => _keyProvider;

        /// <summary>
        /// Gets whether E2EE is enabled.
        /// </summary>
        public bool Enabled => _enabled;

        /// <summary>
        /// Gets the E2EE options.
        /// </summary>
        public E2EEOptions? Options => _options;

        /// <summary>
        /// Sets whether E2EE is enabled.
        /// </summary>
        /// <param name="enabled">True to enable, false to disable.</param>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;

            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    ManagerSetEnabled = new E2eeManagerSetEnabledRequest { Enabled = enabled },
                },
            };

            FfiClient.Instance.SendRequest(request);
        }

        /// <summary>
        /// Gets the list of frame cryptors for all participants.
        /// </summary>
        /// <returns>A list of frame cryptors.</returns>
        public List<FrameCryptor> GetFrameCryptors()
        {
            var request = new FfiRequest
            {
                E2Ee = new E2eeRequest
                {
                    RoomHandle = _roomHandle,
                    ManagerGetFrameCryptors = new E2eeManagerGetFrameCryptorsRequest(),
                },
            };

            var response = FfiClient.Instance.SendRequest(request);
            var cryptors = new List<FrameCryptor>();

            foreach (var cryptor in response.E2Ee.ManagerGetFrameCryptors.FrameCryptors)
            {
                cryptors.Add(
                    new FrameCryptor(
                        _roomHandle,
                        cryptor.ParticipantIdentity,
                        cryptor.TrackSid,
                        cryptor.KeyIndex,
                        cryptor.Enabled
                    )
                );
            }

            return cryptors;
        }
    }
}
