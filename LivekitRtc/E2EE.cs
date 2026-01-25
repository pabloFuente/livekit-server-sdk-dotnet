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
    }

    /// <summary>
    /// Options for the key provider.
    /// </summary>
    public class KeyProviderOptions
    {
        /// <summary>
        /// Gets or sets the shared key.
        /// </summary>
        public byte[]? SharedKey { get; set; }

        /// <summary>
        /// Gets or sets the ratchet salt.
        /// </summary>
        public byte[] RatchetSalt { get; set; } = E2EEDefaults.RatchetSalt;

        /// <summary>
        /// Gets or sets the ratchet window size.
        /// </summary>
        public int RatchetWindowSize { get; set; } = E2EEDefaults.RatchetWindowSize;

        /// <summary>
        /// Gets or sets the failure tolerance.
        /// </summary>
        public int FailureTolerance { get; set; } = E2EEDefaults.FailureTolerance;
    }

    /// <summary>
    /// Options for end-to-end encryption.
    /// </summary>
    public class E2EEOptions
    {
        /// <summary>
        /// Gets or sets the key provider options.
        /// </summary>
        public KeyProviderOptions KeyProviderOptions { get; set; } = new KeyProviderOptions();

        /// <summary>
        /// Gets or sets the encryption type.
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
                        KeyProviderOptions.RatchetSalt
                    ),
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
        /// Initializes a new key provider.
        /// </summary>
        /// <param name="roomHandle">The room handle.</param>
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
        private int _keyIndex;
        private bool _enabled;

        /// <summary>
        /// Initializes a new frame cryptor.
        /// </summary>
        /// <param name="roomHandle">The room handle.</param>
        /// <param name="participantIdentity">The participant identity.</param>
        /// <param name="keyIndex">The key index.</param>
        /// <param name="enabled">Whether encryption is enabled.</param>
        public FrameCryptor(
            ulong roomHandle,
            string participantIdentity,
            int keyIndex,
            bool enabled
        )
        {
            _roomHandle = roomHandle;
            _participantIdentity = participantIdentity;
            _keyIndex = keyIndex;
            _enabled = enabled;
        }

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
        /// Initializes a new E2EE manager.
        /// </summary>
        /// <param name="roomHandle">The room handle.</param>
        /// <param name="options">The E2EE options (null to disable).</param>
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
                        cryptor.KeyIndex,
                        cryptor.Enabled
                    )
                );
            }

            return cryptors;
        }
    }
}
