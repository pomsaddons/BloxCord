using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BloxCord.Client.Services;

internal sealed class E2eeDmService : IDisposable
{
    private readonly ECDiffieHellman _ecdh;

    public E2eeDmService()
    {
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    }

    public string GetPublicKeyBase64()
    {
        var spki = _ecdh.ExportSubjectPublicKeyInfo();
        return Convert.ToBase64String(spki);
    }

    public string EncryptToEnvelope(string recipientPublicKeyBase64, string plaintext)
    {
        if (string.IsNullOrWhiteSpace(recipientPublicKeyBase64))
            throw new ArgumentException("Recipient public key required", nameof(recipientPublicKeyBase64));

        var recipientPub = Convert.FromBase64String(recipientPublicKeyBase64);
        using var recipientKey = ECDiffieHellman.Create();
        recipientKey.ImportSubjectPublicKeyInfo(recipientPub, out _);

        var shared = _ecdh.DeriveKeyMaterial(recipientKey.PublicKey);
        var key = SHA256.HashData(shared); // 32 bytes

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var envelope = new DmEnvelope
        {
            V = 1,
            SenderPubKey = GetPublicKeyBase64(),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext)
        };

        var json = JsonSerializer.Serialize(envelope);
        return "e2ee://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public bool TryDecryptEnvelope(string content, out string plaintext)
    {
        plaintext = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        if (!content.StartsWith("e2ee://", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var payloadB64 = content.Substring("e2ee://".Length);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payloadB64));
            var envelope = JsonSerializer.Deserialize<DmEnvelope>(json);
            if (envelope is null || envelope.V != 1)
                return false;

            var senderPub = Convert.FromBase64String(envelope.SenderPubKey);
            using var senderKey = ECDiffieHellman.Create();
            senderKey.ImportSubjectPublicKeyInfo(senderPub, out _);

            var shared = _ecdh.DeriveKeyMaterial(senderKey.PublicKey);
            var key = SHA256.HashData(shared);

            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            var plaintextBytes = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

            plaintext = Encoding.UTF8.GetString(plaintextBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _ecdh.Dispose();
    }

    private sealed class DmEnvelope
    {
        public int V { get; set; }
        public string SenderPubKey { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }
}
