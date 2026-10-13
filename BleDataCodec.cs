using System.Text;
using System.Text.Json;

namespace CranialTherapy;

// 只转换原始数据，不自动添加换行、协议头、校验或分包。
public static class BleDataCodec
{
    private static readonly JsonSerializerOptions DisplayOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static byte[] Encode(string input, bool isHex, int maxBytes)
    {
        byte[] bytes;
        if (isHex)
        {
            string hex = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
            if (hex.Length == 0) throw new ArgumentException("请输入发送内容。");
            if (hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit))
                throw new ArgumentException("HEX 格式不正确，请输入完整字节，例如 68 65 6C 6C 6F。");
            bytes = Convert.FromHexString(hex);
        }
        else
        {
            if (input.Length == 0) throw new ArgumentException("请输入发送内容。");
            bytes = Encoding.UTF8.GetBytes(input);
        }

        if (bytes.Length > maxBytes)
            throw new ArgumentException($"内容为 {bytes.Length} 字节，本次连接单次最多发送 {maxBytes} 字节，请缩短内容。");
        return bytes;
    }

    public static string FormatPayload(byte[] bytes)
    {
        // 转义换行等控制字符，防止原始数据破坏记录格式；HEX 始终保留原始字节。
        string text = JsonSerializer.Serialize(Encoding.UTF8.GetString(bytes), DisplayOptions);
        string hex = string.Join(" ", bytes.Select(value => value.ToString("X2")));
        return $"UTF-8: {text}   HEX: {hex}";
    }
}
