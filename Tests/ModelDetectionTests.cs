using System.Reflection;
using SakuraTranslate.Helpers;
using SakuraTranslate.Models;

internal static class ModelDetectionTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        foreach (var item in new[] {
            ("sakura-7b-qwen2.5-v1.0-iq4xs.gguf", "SakuraV1_0"),
            ("SakuraLLM/Sakura-GalTransl-7B-v3.7", "GalTranslV3"),
            ("D:\\models\\Sakura-Galtransl-7B-v3.7-IQ4_XS.gguf", "GalTranslV3"),
            ("GalTransl-v4-4B-2601-Q5_K_S.gguf", "GalTranslV3"),
            ("GalTransl-7B-v2.6-IQ4_XS.gguf", "GalTranslV2_6"),
            ("sakura-13b-lnovel-v0.9-Q4_K_M.gguf", "SakuraV0_9"),
            ("sakura-14b-qwen2beta-v0.10-IQ4_XS.gguf", "SakuraV0_10"),
            ("Sakura-14B-Qwen3-v1.5-Q4_K_M.gguf", "SakuraV1_5"),
        })
        {
            var (identifier, expected) = item;
            yield return ("模型识别 " + identifier, () => {
                if (!Detect(identifier, out var result) || result.ToString() != expected)
                    throw new Exception("没有匹配到预期模板 " + expected);
            });
        }
        foreach (var identifier in new[] { "unknown", "", "sukinishiro", "Qwen3-8B-Q4_K_M.gguf",
            "Sakura-4B-Qwen3-Base-v2.gguf", "Sakura-8B-Qwen3-v1.1-SFT.gguf", "GalTransl-v9.gguf" })
        {
            yield return ("拒绝猜测模板 " + identifier, () => {
                if (Detect(identifier, out _)) throw new Exception("未知模板不应被默认匹配");
            });
        }
    }

    private static bool Detect(string identifier, out TranslationModel model)
    {
        var method = typeof(TranslateModelHelper).GetMethod("TryDetectTranslationModel", BindingFlags.Public | BindingFlags.Static);
        if (method == null) throw new Exception("尚未实现自动识别入口");
        var arguments = new object[] { identifier, default(TranslationModel) };
        var found = (bool)method.Invoke(null, arguments);
        model = (TranslationModel)arguments[1];
        return found;
    }
}
