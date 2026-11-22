using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SakuraTranslate;
using SakuraTranslate.Models;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 2 && args[0] == "--live")
        {
            EndpointTests.VerifyLiveBackend(args[1]);
            return 0;
        }
        var failures = 0;
        foreach (var culture in new[] { "zh-CN", "fr-FR", "de-DE" })
        {
            try
            {
                RequestJsonUsesInvariantNumbers(culture);
                Console.WriteLine("PASS request JSON culture " + culture);
            }
            catch (Exception error)
            {
                failures++;
                Console.WriteLine("FAIL request JSON culture " + culture + ": " + error.GetBaseException().Message);
            }
        }
        foreach (var test in ModelDetectionTests.Cases().Concat(EndpointTests.Cases()))
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error.GetBaseException().Message); }
        }
        return failures == 0 ? 0 : 1;
    }

    private static void RequestJsonUsesInvariantNumbers(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var endpoint = new SakuraTranslateEndpoint();
            var method = typeof(SakuraTranslateEndpoint).GetMethod("MakeRequestStr", BindingFlags.NonPublic | BindingFlags.Instance);
            var prompts = new List<PromptMessage> { new PromptMessage { Role = "user", Content = "引用\"与\\N[1]\n换行" } };
            var json = (string)method.Invoke(endpoint, new object[] { prompts, 512, 0.3, 0.8, 0.2 });
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("temperature").GetDouble() != 0.3 || root.GetProperty("top_p").GetDouble() != 0.8)
                throw new Exception("采样参数的数值发生变化");
            if (root.GetProperty("messages")[0].GetProperty("content").GetString() != prompts[0].Content)
                throw new Exception("控制符或引号未正确保留");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
