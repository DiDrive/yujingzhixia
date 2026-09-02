using System.IO;
using YooAsset;

/// <summary>
/// Bundle 加密解密（XOR 示例，实际项目请替换为更安全的算法和密钥）
/// </summary>
public static class BundleEncryption
{
    private const byte XOR_KEY = 0x5A;

    /// <summary>
    /// 构建时加密器（在 Bundle Builder 的 "Bundle Encryptor" 里选择）
    /// </summary>
    public class Encryptor : IBundleEncryptor
    {
        public BundleEncryptResult Encrypt(BundleEncryptArgs args)
        {
            byte[] data = File.ReadAllBytes(args.FilePath);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= XOR_KEY;
            return new BundleEncryptResult(true, data);
        }
    }

    /// <summary>
    /// 运行时解密器（内存模式）
    /// </summary>
    public class Decryptor : IBundleMemoryDecryptor
    {
        public byte[] GetDecryptedData(BundleDecryptArgs args)
        {
            byte[] data = args.FileData;
            if (data == null)
                data = File.ReadAllBytes(args.FilePath);

            for (int i = 0; i < data.Length; i++)
                data[i] ^= XOR_KEY;
            return data;
        }
    }
}
