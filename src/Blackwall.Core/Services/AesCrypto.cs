using System.Security.Cryptography;

namespace Blackwall.Core.Services;

public static class AesCrypto {

    /// <summary>
    /// Decodes a Base64-encoded string into a raw byte array, suitable for use as an AES key or IV.
    /// </summary>
    /// <param name="str">A Base64-encoded string representing the key or IV.</param>
    /// <returns>The decoded byte array.</returns>
    public static byte[] GetBytes(string str) => Convert.FromBase64String(str);
    
    /// <summary>
    /// Encrypts a plain-text string using AES with the provided key and IV.
    /// </summary>
    /// <param name="plainText">The string to encrypt.</param>
    /// <param name="key">The AES encryption key.</param>
    /// <param name="iv">The AES initialisation vector.</param>
    /// <returns>The encrypted data as a Base64-encoded string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plainText"/>, <paramref name="key"/>, or <paramref name="iv"/> is null or empty.</exception>
    public static string EncryptString(string plainText, byte[] key, byte[] iv) {
        if (string.IsNullOrEmpty(plainText)) throw new ArgumentNullException(nameof(plainText));
        if (key == null || key.Length == 0) throw new ArgumentNullException(nameof(key));
        if (iv == null || iv.Length == 0) throw new ArgumentNullException(nameof(iv));

        byte[] encryptedBytes;

        using (var aes = Aes.Create()) {
            aes.Key = key;
            aes.IV = iv;
            
            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            
            using (var memoryStream = new MemoryStream()) {
                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write)) {
                    using (var streamWriter = new StreamWriter(cryptoStream)) {
                        streamWriter.Write(plainText);
                    }
                    encryptedBytes = memoryStream.ToArray();
                }
            }
        }
        
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// Decrypts a Base64-encoded AES cipher text back to the original plain-text string.
    /// </summary>
    /// <param name="cipherText">The Base64-encoded cipher text to decrypt.</param>
    /// <param name="key">The AES decryption key.</param>
    /// <param name="iv">The AES initialisation vector.</param>
    /// <returns>The decrypted plain-text string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cipherText"/>, <paramref name="key"/>, or <paramref name="iv"/> is null or empty.</exception>
    public static string DecryptString(string cipherText, byte[] key, byte[] iv) {
        if (string.IsNullOrEmpty(cipherText)) throw new ArgumentNullException(nameof(cipherText));
        if (key == null || key.Length == 0) throw new ArgumentNullException(nameof(key));
        if (iv == null || iv.Length == 0) throw new ArgumentNullException(nameof(iv));
        
        var cipherBytes = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        
        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        
        using var memoryStream = new MemoryStream(cipherBytes);
        using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
        using var streamReader = new StreamReader(cryptoStream);
        return streamReader.ReadToEnd();
    }
    
}