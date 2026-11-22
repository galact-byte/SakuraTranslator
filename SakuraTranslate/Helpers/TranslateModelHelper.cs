using SakuraTranslate.Models;
using System;
using System.Text.RegularExpressions;

namespace SakuraTranslate.Helpers
{
    public static class TranslateModelHelper
    {
        public static bool TryDetectTranslationModel(string identifier, out TranslationModel model)
        {
            model = default(TranslationModel);
            if (string.IsNullOrEmpty(identifier)) return false;
            var name = identifier.Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            if (Regex.IsMatch(name, @"(?:^|[-_])base(?:[-_.]|$)", RegexOptions.IgnoreCase)) return false;
            if (name.IndexOf("galtransl", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (HasVersion(name, "2[._]6")) model = TranslationModel.GalTranslV2_6;
                else if (HasVersion(name, "3(?:[._][0-9]+)?") || HasVersion(name, "4(?:[._][0-9]+)?")) model = TranslationModel.GalTranslV3;
                else return false;
                return true;
            }
            if (!name.StartsWith("sakura-", StringComparison.OrdinalIgnoreCase)) return false;
            if (HasVersion(name, "0[._]9")) model = TranslationModel.SakuraV0_9;
            else if (HasVersion(name, "0[._]10")) model = TranslationModel.SakuraV0_10;
            else if (HasVersion(name, "1[._]0")) model = TranslationModel.SakuraV1_0;
            else if (name.IndexOf("qwen3", StringComparison.OrdinalIgnoreCase) >= 0 && HasVersion(name, "1[._]5")) model = TranslationModel.SakuraV1_5;
            else return false;
            return true;
        }

        private static bool HasVersion(string name, string version)
        {
            return Regex.IsMatch(name, @"(?:^|[-_])v" + version + @"(?:[-_.]|$)", RegexOptions.IgnoreCase);
        }

        public static TranslationModel GetTranslationModel(string modelName, string modelVersion)
        {
            switch (modelName.ToLower())
            {
                case "sakura":
                    switch (modelVersion)
                    {
                        case "0.9": return TranslationModel.SakuraV0_9;
                        case "0.10": return TranslationModel.SakuraV0_10;
                        case "1.0": return TranslationModel.SakuraV1_0;
                        case "1.5": return TranslationModel.SakuraV1_5;
                        default: return TranslationModel.SakuraV1_0;
                    }
                case "galtransl":
                    switch (modelVersion)
                    {
                        case "2.6": return TranslationModel.GalTranslV2_6;
                        case "3": return TranslationModel.GalTranslV3;
                        default: return TranslationModel.GalTranslV3;
                    }
                default:
                    return TranslationModel.SakuraV1_0;
            }
        }
    }
}
