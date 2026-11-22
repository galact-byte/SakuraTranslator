using SakuraTranslate.Helpers;
using SimpleJSON;
using System;
using System.Collections;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.Common.Logging;

namespace SakuraTranslate
{
    public partial class SakuraTranslateEndpoint : ITranslateEndpoint
    {
        private static JSONNode ParseResponse(string text)
        {
            JSONNode root;
            try { root = JSON.Parse(text); }
            catch (Exception) { throw new InvalidOperationException("翻译服务没有返回有效的 JSON 对象，请检查 Endpoint 和后端日志。"); }
            if (root == null || !root.IsObject)
                throw new InvalidOperationException("翻译服务没有返回有效的 JSON 对象，请检查 Endpoint 和后端日志。");
            return root;
        }

        private void SelectBackendModel(string response)
        {
            var root = ParseResponse(response);
            var models = root["data"].AsArray;
            if (models == null || models.Count != 1 || !models[0].IsObject || !models[0]["id"].IsString)
                throw new InvalidOperationException("自动识别需要后端返回唯一模型；空列表或多模型服务请手动填写 ModelName 与 ModelVersion。");
            string identifier = models[0]["id"].Value;
            Models.TranslationModel detected;
            if (!TranslateModelHelper.TryDetectTranslationModel(identifier, out detected))
                throw new InvalidOperationException("无法确认后端模型的提示词模板：" + identifier + "。请使用已支持的模型，或手动填写 ModelName 与 ModelVersion。");
            if (_apiModelName != identifier)
                XuaLogger.AutoTranslator.Info("SakuraTranslate: 自动识别模型 " + identifier + "，模板 " + detected);
            _modelType = detected;
            _apiModelName = identifier;
        }

        public IEnumerator Translate(ITranslationContext context)
        {
            string original = context.UntranslatedText;
            if (_debug) XuaLogger.AutoTranslator.Debug("SakuraTranslate.Translate: untranslatedText: " + original);
            double penalty = 0;
            int retries = 0;
            string translated = null;
            while (true)
            {
                if (_autoDetectModel)
                {
                    var discovery = HttpRequestHelper.Send(_modelsEndpoint, "GET", null, _requestTimeoutMs, SelectBackendModel);
                    try { while (discovery.MoveNext()) yield return discovery.Current; }
                    finally { (discovery as IDisposable).Dispose(); }
                }
                string responseText = null;
                var operation = HttpRequestHelper.Send(_endpoint, "POST", MakeRequestJson(original, penalty), _requestTimeoutMs, value => responseText = value);
                try { while (operation.MoveNext()) yield return operation.Current; }
                finally { (operation as IDisposable).Dispose(); }
                if (_debug) XuaLogger.AutoTranslator.Debug("SakuraTranslate.Translate: responseText: " + responseText);
                var response = ParseResponse(responseText);
                var choices = response["choices"].AsArray;
                if (choices == null || choices.Count == 0 || !choices[0].IsObject || !choices[0]["message"].IsObject || !choices[0]["message"]["content"].IsString)
                    throw new InvalidOperationException("翻译服务没有返回有效的 choices[0].message.content，请检查后端接口和日志。");
                translated = choices[0]["message"]["content"].Value;
                if (translated.Trim().Length == 0)
                    throw new InvalidOperationException("翻译服务返回空译文，请检查后端模型和日志。");
                int tokens;
                bool capped = (int.TryParse(response["usage"]["completion_tokens"].Value, out tokens) && tokens >= GetMaxTokens(original))
                    || choices[0]["finish_reason"].Value == "length";
                if (!_fixDegeneration || !capped) break;
                if (retries >= DEGENERATION_MAX_RETRIES)
                {
                    XuaLogger.AutoTranslator.Warn("SakuraTranslate: 达到最大退化重试次数，保留最后一次译文。");
                    break;
                }
                penalty += 0.2;
                retries++;
                XuaLogger.AutoTranslator.Warn("SakuraTranslate: 检测到输出长度限制，重试 " + retries + "/" + DEGENERATION_MAX_RETRIES);
            }
            translated = TranslationHelper.FixTranslationEnd(original, translated);
            if (_debug) XuaLogger.AutoTranslator.Debug("SakuraTranslate.Translate: translatedText: " + translated);
            context.Complete(translated);
        }
    }
}
